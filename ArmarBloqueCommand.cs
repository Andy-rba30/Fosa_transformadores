using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace BlockRebar
{
    /// <summary>
    /// Comando "Bloques con foso": selecciona cimentaciones estructurales, lee su geometria
    /// (sin transaccion), abre la lamina y, con "Armar", crea las barras con una subtransaccion
    /// por elemento (o se arma entero y bien, o no se arma). "Borrar armado del plugin" quita
    /// solo los conjuntos marcados por el plugin en los elementos seleccionados.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    public class ArmarBloqueCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null) { message = "No hay ningun documento abierto."; return Result.Failed; }
            Document doc = uidoc.Document;
            Log.Write("---- Bloques con foso: inicio (" + doc.Title + ") ----");

            AppConfig cfg;
            try { cfg = AppConfig.Load(); }
            catch (Exception ex)
            {
                message = "No se pudo leer config.json (" + AppConfig.ConfigPath() + "): " + ex.Message;
                Log.Error("AppConfig.Load", ex);
                return Result.Failed;
            }

            IList<Element> hosts;
            try { hosts = GetHosts(uidoc); }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }

            if (hosts.Count == 0)
            {
                message = "No se selecciono ninguna cimentacion estructural.";
                return Result.Cancelled;
            }

            List<BarTypes.Info> barTypes;
            try { barTypes = BarTypes.Read(doc); }
            catch (Exception ex) { barTypes = new List<BarTypes.Info>(); Log.Error("BarTypes.Read", ex); }
            Log.Write("tipos de barra: " + (barTypes.Count == 0 ? "ninguno" : string.Join(", ", barTypes.Select(b => b.Display))));
            List<SectionViews.TagType> tagTypes = SectionViews.ReadTagTypes(doc);
            Log.Write("etiquetas de armadura: " + (tagTypes.Count == 0 ? "ninguna" : string.Join(", ", tagTypes.Select(t => t.Display))));
            List<GridGenerator.AngleSymbolInfo> angleSymbols = GridGenerator.ReadAngleSymbols(doc, cfg.Grids.Angles.KgPerMDefault);
            Log.Write("tipos de Structural Framing: " + (angleSymbols.Count == 0 ? "ninguno" : string.Join(", ", angleSymbols.Select(a => a.Display + " (" + a.KgPerM.ToString("0.00", CultureInfo.InvariantCulture) + " kg/m)"))));

            // --- 1. Analisis geometrico de cada elemento (solo lectura, sin transaccion) ---
            var items = new List<HostAnalysis>();
            foreach (Element h in hosts)
            {
                HostAnalysis a = HostAnalysis.Analyze(doc, h, cfg);
                a.PluginRebars = RebarGenerator.FindPluginRebars(doc, h);
                if (a.PluginRebars.Count > 0) a.Diagnostics.Add("ya tiene " + a.PluginRebars.Count + " conjunto(s) de armadura creados por el plugin");
                a.PluginGridItems = GridGenerator.FindPluginItems(doc, h);
                if (a.PluginGridItems.Count > 0) a.Diagnostics.Add("ya tiene " + a.PluginGridItems.Count + " angulo(s)/rejilla(s) colocados por el plugin");
                items.Add(a);
                Log.Block(a.Tag.Trim(), string.Join(Environment.NewLine, a.Diagnostics));
            }

            // --- 2. Lamina: el usuario revisa lo detectado y elige el armado ---
            Func<string, double> offset = r => BlockOutline.ElevationOffset(doc, r);
            RebarOptionsWindow win;
            AppConfig winCfg = cfg.Clone();
            while (true)
            {
                Autodesk.Revit.DB.Family gridFamily = GridGenerator.FindGridFamily(doc, winCfg.Grids.FamilyName);
                var gfs = new GridFamilyStatus { Loaded = gridFamily != null, Name = gridFamily?.Name ?? winCfg.Grids.FamilyName, Types = GridGenerator.GridSymbols(doc, gridFamily).Select(x => x.Name).ToList() };
                win = new RebarOptionsWindow(winCfg, barTypes, items, offset, tagTypes, angleSymbols, gfs);
                try { new WindowInteropHelper(win).Owner = commandData.Application.MainWindowHandle; } catch { }
                bool? ok;
                try { ok = win.ShowDialog(); }
                catch (Exception ex)
                {
                    Log.Error("RebarOptionsWindow", ex);
                    message = "Error en la ventana: " + ex.Message + Environment.NewLine + "Detalle en " + Log.Path;
                    return Result.Failed;
                }
                if (ok != true) return Result.Cancelled;
                if (!win.CreateGridFamilyRequested) break;

                // --- 2b. Crear y cargar la familia de rejilla; la ventana se vuelve a abrir ---
                winCfg = win.Result ?? winCfg;
                var notes = new List<string>();
                using (Transaction tx = new Transaction(doc, "Crear familia de rejilla"))
                {
                    tx.Start();
                    try
                    {
                        Autodesk.Revit.DB.Family fam = GridGenerator.CreateGridFamily(commandData.Application.Application, doc, winCfg.Grids, notes);
                        tx.Commit();
                        Log.Block("Crear familia de rejilla", string.Join(Environment.NewLine, notes));
                        TaskDialog.Show("Familia de rejilla", "Familia \"" + fam.Name + "\" creada y cargada." + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, notes));
                    }
                    catch (Exception ex)
                    {
                        tx.RollBack();
                        Log.Error("CreateGridFamily", ex);
                        TaskDialog.Show("Familia de rejilla", "No se pudo crear la familia: " + ex.Message + Environment.NewLine + string.Join(Environment.NewLine, notes) + Environment.NewLine + "log: " + Log.Path);
                    }
                }
            }

            // --- 3a. Borrar el armado / las rejillas del plugin ---
            if (win.DeleteRequested) return DeletePluginRebars(doc, items, commandData);
            if (win.DeleteGridsRequested) return DeletePluginGrids(doc, items, commandData);

            if (win.Result == null) return Result.Cancelled;
            cfg = win.Result;

            // --- 3b. Solo vistas de seccion (sin armar) ---
            if (win.ViewsOnlyRequested) return CreateViewsOnly(doc, items, cfg, win, tagTypes, commandData);
            // --- 3b'. Rejillas y angulos (sin armar) ---
            if (win.GridsRequested) return PlaceGrids(doc, items, cfg, angleSymbols, barTypes, commandData);

            // --- 3c. Elementos que ya tienen barras del plugin: borrar antes de rearmar o conservar ---
            var withExisting = items.Where(i => i.Outline != null && i.PluginRebars.Count > 0).ToList();
            bool deleteFirst = false;
            if (withExisting.Count > 0)
            {
                var td = new TaskDialog("Bloques con foso")
                {
                    MainInstruction = withExisting.Count + " elemento(s) ya tienen armadura creada por este plugin.",
                    MainContent = string.Join(Environment.NewLine, withExisting.Select(i => i.Tag.Trim() + " " + i.PluginRebars.Count + " conjunto(s)")) +
                                  Environment.NewLine + Environment.NewLine + "Para no duplicar barras, lo normal es borrarla antes de rearmar.",
                    AllowCancellation = true,
                    CommonButtons = TaskDialogCommonButtons.Cancel
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Borrar la armadura del plugin y rearmar", "Se borra dentro de la misma subtransaccion: si el nuevo armado se rechaza, la anterior se conserva.");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Conservar la existente y anadir la nueva", "Quedaran barras duplicadas en esos elementos.");
                TaskDialogResult r = td.Show();
                if (r == TaskDialogResult.CommandLink1) deleteFirst = true;
                else if (r != TaskDialogResult.CommandLink2) return Result.Cancelled;
            }

            // --- 4. Armado ---
            var log = new List<string>();
            var detail = new StringBuilder();
            int total = 0, armed = 0, rejected = 0, deletedSets = 0;

            using (Transaction tx = new Transaction(doc, "Armar bloques con foso"))
            {
                tx.Start();
                foreach (HostAnalysis item in items)
                {
                    string tag = item.Tag;
                    if (item.Outline == null)
                    {
                        rejected++;
                        log.Add(tag + "SIN ARMAR -> " + item.Error);
                        continue;
                    }

                    // Cada elemento se arma dentro de una subtransaccion. Si cualquier barra queda
                    // fuera del hormigon (red de seguridad), se deshace TODO lo creado (y lo borrado)
                    // para ese elemento: o se arma entero y bien, o no se arma.
                    using (SubTransaction sub = new SubTransaction(doc))
                    {
                        sub.Start();
                        BuildResult res = null;
                        string error = null;
                        int deleted = 0, deletedBars = 0;
                        try
                        {
                            if (deleteFirst && item.PluginRebars.Count > 0) deleted = RebarGenerator.DeletePluginRebars(doc, item.Host, out deletedBars);
                            res = RebarGenerator.Build(doc, item, cfg, barTypes);
                            if (res.Safe && res.Created.Count > 0)
                            {
                                doc.Regenerate();
                                RebarGenerator.VerifyCreated(doc, item, cfg, res);
                            }
                        }
                        catch (Exception ex)
                        {
                            error = ex.Message;
                            Log.Error("Build " + tag, ex);
                        }

                        bool keep = error == null && res != null && res.Safe && res.Created.Count > 0;
                        string desc = (item.Frame(cfg)?.Describe() ?? "") + ", " + (item.Topology(cfg)?.Describe() ?? item.Outline.Describe());
                        if (keep)
                        {
                            sub.Commit();
                            armed++;
                            SectionViews.Result views = null;
                            if (cfg.SectionViews.Enabled)
                            {
                                // las vistas van en su propia subtransaccion: si fallan no se pierde el armado
                                using (SubTransaction subV = new SubTransaction(doc))
                                {
                                    subV.Start();
                                    try
                                    {
                                        var ids = res.Created.Select(c => c.Id).ToList();
                                        if (!deleteFirst) ids.AddRange(item.PluginRebars);
                                        (double a, double b) cuts = win.CutsOf(item, cfg);
                                        views = SectionViews.Create(doc, item, cfg, cuts.a, cuts.b, ids, tagTypes);
                                        subV.Commit();
                                    }
                                    catch (Exception ex)
                                    {
                                        subV.RollBack();
                                        views = new SectionViews.Result();
                                        views.Warnings.Add("vistas de seccion: " + ex.Message);
                                        Log.Error("SectionViews " + tag, ex);
                                    }
                                }
                            }
                            deletedSets += deleted;
                            total += res.Created.Count;
                            string line = tag + desc + "  ->  " + res.Summary;
                            if (deleted > 0) line += "  (borrados antes " + deleted + " conjuntos, " + deletedBars + " barras del plugin)";
                            if (res.Failed.Count > 0) line += "  INCOMPLETO, no se pudieron crear: " + string.Join(" | ", res.Failed);
                            if (res.Warnings.Count > 0) line += "  AVISOS: " + string.Join(" | ", res.Warnings);
                            if (!res.ComparisonOk) line += "  DIFERENCIAS entre lo previsto y lo leido de Revit (ver detalle)";
                            if (views != null) line += "  VISTAS: " + views.Views.Count + " creada(s)" + (views.Tags > 0 ? ", " + views.Tags + " etiqueta(s)" : "") + (views.Warnings.Count > 0 ? " (avisos, ver detalle)" : "");
                            log.Add(line);

                            detail.AppendLine("== " + tag.Trim() + " ==");
                            detail.AppendLine(desc);
                            detail.AppendLine(res.Summary);
                            detail.AppendLine("Particion: " + item.Partition(cfg, Families.Name(Family.F1), "F1", "u") + " (F1, capa u)  |  comentario: \"" + RebarGenerator.Marker + " F1\"...");
                            detail.AppendLine();
                            detail.AppendLine("Tabla prevista (por familia, pesos por diametro):");
                            detail.AppendLine(res.Plan.QuantityTable());
                            detail.AppendLine();
                            detail.AppendLine("Comparacion con lo leido de Revit tras regenerar" + (res.ComparisonOk ? " (todo coincide):" : " (HAY DIFERENCIAS):"));
                            foreach (string c in res.Comparison) detail.AppendLine(c);
                            foreach (string w in res.Warnings) detail.AppendLine("aviso: " + w);
                            foreach (string f in res.Failed) detail.AppendLine("NO CREADO: " + f);
                            if (views != null)
                            {
                                foreach (string v in views.Lines) detail.AppendLine(v);
                                foreach (string w in views.Warnings) detail.AppendLine("aviso (vistas): " + w);
                            }
                            detail.AppendLine();
                        }
                        else
                        {
                            sub.RollBack();
                            rejected++;
                            string why;
                            if (error != null) why = "ERROR: " + error;
                            else if (res == null || res.Created.Count == 0 && res.Safe) why = "no se creo ningun conjunto" + (res != null && res.Failed.Count > 0 ? ": " + string.Join(" | ", res.Failed) : "");
                            else why = "barras fuera del hormigon o choques (" + res.Rejected.Count + "): " + string.Join(" | ", res.Rejected);
                            log.Add(tag + "SIN ARMAR -> " + desc + ": " + why + ". Se ha deshecho todo lo creado para este elemento" +
                                    (deleteFirst && item.PluginRebars.Count > 0 ? " (su armadura anterior se conserva)" : "") + ".");
                            detail.AppendLine("== " + tag.Trim() + " ==  SIN ARMAR");
                            detail.AppendLine(why);
                            detail.AppendLine();
                        }
                    }
                }
                tx.Commit();
            }

            string head = total + " conjuntos de armadura creados en " + armed + " de " + hosts.Count + " elemento(s)" +
                          (deletedSets > 0 ? ", " + deletedSets + " conjuntos anteriores del plugin borrados" : "") + "." +
                          (rejected > 0 ? Environment.NewLine + "ATENCION: " + rejected + " elemento(s) SIN ARMAR (ver detalle). No se ha creado ninguna barra en ellos." : "");
            string report = head + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, log) + Environment.NewLine + Environment.NewLine +
                            "---- DETALLE ----" + Environment.NewLine + detail + "log: " + Log.Path;
            Log.Block("Armar bloques con foso", report);
            ShowReport(commandData, "Armado de bloques con foso", report);
            return Result.Succeeded;
        }

        private static Result PlaceGrids(Document doc, List<HostAnalysis> items, AppConfig cfg, List<GridGenerator.AngleSymbolInfo> angleSymbols, List<BarTypes.Info> barTypes, ExternalCommandData commandData)
        {
            GridsCfg g = cfg.Grids;
            PlanDiameters diam = BarTypes.Diameters(barTypes, cfg, out List<string> missingTypes);
            List<GridGenerator.AngleSymbolInfo> cands = GridGenerator.Candidates(angleSymbols, g.Angles.FamilyName, g.Angles.TypeName);
            GridGenerator.AngleSymbolInfo angle = cands.Count == 1 ? cands[0] : null;
            Autodesk.Revit.DB.Family gridFamily = GridGenerator.FindGridFamily(doc, g.FamilyName);
            var targets = items.Where(i => i.Outline != null).ToList();
            var withExisting = targets.Where(i => i.PluginGridItems.Count > 0).ToList();
            bool deleteFirst = false;
            if (withExisting.Count > 0)
            {
                var td = new TaskDialog("Rejillas y angulos")
                {
                    MainInstruction = withExisting.Count + " elemento(s) ya tienen rejillas o angulos colocados por este plugin.",
                    MainContent = string.Join(Environment.NewLine, withExisting.Select(i => i.Tag.Trim() + " " + i.PluginGridItems.Count + " elemento(s)")) +
                                  Environment.NewLine + Environment.NewLine + "Para no duplicar, lo normal es borrarlos antes de recolocar.",
                    AllowCancellation = true,
                    CommonButtons = TaskDialogCommonButtons.Cancel
                };
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Borrar los del plugin y recolocar", "Dentro de la misma subtransaccion: si la colocacion falla, los anteriores se conservan.");
                td.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Conservar y anadir", "Quedaran duplicados.");
                TaskDialogResult r = td.Show();
                if (r == TaskDialogResult.CommandLink1) deleteFirst = true;
                else if (r != TaskDialogResult.CommandLink2) return Result.Cancelled;
            }

            var log = new List<string>();
            var detail = new StringBuilder();
            int placedAngles = 0, placedGrids = 0, done = 0, rejected = 0;
            using (Transaction tx = new Transaction(doc, "Colocar rejillas y angulos de foso"))
            {
                tx.Start();
                foreach (HostAnalysis item in items)
                {
                    string tag = item.Tag;
                    if (item.Outline == null) { rejected++; log.Add(tag + "SIN REJILLAS -> " + item.Error); continue; }
                    using (SubTransaction sub = new SubTransaction(doc))
                    {
                        sub.Start();
                        GridGenerator.PlaceResult res = null;
                        GridPlan plan = null;
                        GridPlan.ClashReport clash = null;
                        string error = null;
                        int deleted = 0;
                        try
                        {
                            BlockTopology t = item.Frame(cfg)?.Topology;
                            if (t == null || t.Error != null) throw new InvalidOperationException(t?.Error ?? "sin geometria legible");
                            plan = GridPlan.Build(t, g, angle?.KgPerM ?? g.Angles.KgPerMDefault, item.GridTypeOverride);
                            if (plan.Error != null) throw new InvalidOperationException(plan.Error);
                            // red de seguridad: angulo contra hormigon, contra rejilla y contra las barras previstas
                            BlockPlan rebarPlan = missingTypes.Count == 0 ? BlockPlan.Build(t, cfg, diam) : null;
                            clash = plan.CheckClashes(rebarPlan, BlockPlan.Mm(g.Angles.LegMm), BlockPlan.Mm(g.Angles.ThicknessMm));
                            if (!clash.Ok) throw new InvalidOperationException("choques de los angulos: " + string.Join(" | ", clash.Clashes));
                            if (deleteFirst && item.PluginGridItems.Count > 0) deleted = GridGenerator.DeletePluginItems(doc, item.Host, out _, out _);
                            res = GridGenerator.Place(doc, item, cfg, plan, angle, gridFamily);
                        }
                        catch (Exception ex) { error = ex.Message; Log.Error("PlaceGrids " + tag, ex); }

                        bool keep = error == null && res != null && (res.Angles.Count + res.Grids.Count > 0 || (g.Mode == "countOnly" && !g.Angles.Enabled));
                        if (keep)
                        {
                            sub.Commit();
                            done++; placedAngles += res.Angles.Count; placedGrids += res.Grids.Count;
                            log.Add(tag + res.Summary + (deleted > 0 ? " (borrados antes " + deleted + " del plugin)" : "") + (res.Failed.Count > 0 ? "  INCOMPLETO: " + res.Failed.Count + " no colocados" : "") +
                                    (res.Warnings.Count > 0 ? "  AVISOS: " + string.Join(" | ", res.Warnings) : ""));
                            detail.AppendLine("== " + tag.Trim() + " ==");
                            detail.AppendLine(plan.Describe());
                            foreach (GridStrip st in plan.Strips) detail.AppendLine(st.Describe());
                            detail.AppendLine(plan.QuantityTable());
                            if (clash != null) detail.AppendLine(clash.Describe());
                            foreach (string l in res.Lines) detail.AppendLine(l);
                            foreach (string w in plan.Warnings) detail.AppendLine("aviso (reparto): " + w);
                            foreach (string w in res.Warnings) detail.AppendLine("aviso: " + w);
                            foreach (string f in res.Failed) detail.AppendLine("NO COLOCADO: " + f);
                            detail.AppendLine();
                        }
                        else
                        {
                            sub.RollBack();
                            rejected++;
                            string why = error ?? (res != null && res.Failed.Count > 0 ? "no se pudo colocar nada: " + string.Join(" | ", res.Failed) : "nada que colocar" + (res != null && res.Warnings.Count > 0 ? " (" + string.Join(" | ", res.Warnings) + ")" : ""));
                            log.Add(tag + "SIN REJILLAS -> " + why + ". Se ha deshecho todo lo de este elemento.");
                            detail.AppendLine("== " + tag.Trim() + " ==  SIN REJILLAS");
                            detail.AppendLine(why);
                            if (plan != null && plan.Error == null) detail.AppendLine(plan.QuantityTable());
                            detail.AppendLine();
                        }
                    }
                }
                tx.Commit();
            }
            string head = placedAngles + " angulo(s) y " + placedGrids + " rejilla(s) colocados en " + done + " de " + items.Count + " elemento(s)." +
                          (rejected > 0 ? Environment.NewLine + "ATENCION: " + rejected + " elemento(s) sin rejillas (ver detalle)." : "") +
                          (angle == null && g.Angles.Enabled ? Environment.NewLine + "Sin tipo de angulo cargado o ambiguo: no se han colocado angulos (solo se cuentan en el metrado)." : "") +
                          (gridFamily == null && g.Mode == "model" ? Environment.NewLine + "Familia de rejilla no cargada: las rejillas solo se cuentan." : "");
            string report = head + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, log) + Environment.NewLine + Environment.NewLine +
                            "---- DETALLE ----" + Environment.NewLine + detail + "log: " + Log.Path;
            Log.Block("Colocar rejillas y angulos", report);
            ShowReport(commandData, "Rejillas y angulos de foso", report);
            return Result.Succeeded;
        }

        private static Result DeletePluginGrids(Document doc, List<HostAnalysis> items, ExternalCommandData commandData)
        {
            var lines = new List<string>();
            int angles = 0, grids = 0, elems = 0;
            using (Transaction tx = new Transaction(doc, "Borrar rejillas y angulos del plugin"))
            {
                tx.Start();
                foreach (HostAnalysis item in items)
                {
                    int n = GridGenerator.DeletePluginItems(doc, item.Host, out int a, out int gr);
                    if (n > 0) { elems++; angles += a; grids += gr; }
                    lines.Add(item.Tag + (n > 0 ? a + " angulo(s) y " + gr + " rejilla(s) del plugin borrados" : "sin rejillas ni angulos del plugin"));
                }
                tx.Commit();
            }
            string report = angles + " angulo(s) y " + grids + " rejilla(s) del plugin borrados en " + elems + " elemento(s). La armadura no se toca." + Environment.NewLine + string.Join(Environment.NewLine, lines);
            Log.Block("Borrar rejillas y angulos del plugin", report);
            TaskDialog.Show("Borrar rejillas y angulos", report);
            return Result.Succeeded;
        }

        private static Result CreateViewsOnly(Document doc, List<HostAnalysis> items, AppConfig cfg, RebarOptionsWindow win, List<SectionViews.TagType> tagTypes, ExternalCommandData commandData)
        {
            var lines = new List<string>();
            int views = 0, tags = 0;
            using (Transaction tx = new Transaction(doc, "Crear vistas de seccion"))
            {
                tx.Start();
                foreach (HostAnalysis item in items)
                {
                    if (item.Outline == null) { lines.Add(item.Tag + "sin geometria legible: sin vistas (" + item.Error + ")"); continue; }
                    using (SubTransaction sub = new SubTransaction(doc))
                    {
                        sub.Start();
                        try
                        {
                            (double a, double b) cuts = win.CutsOf(item, cfg);
                            SectionViews.Result r = SectionViews.Create(doc, item, cfg, cuts.a, cuts.b, item.PluginRebars, tagTypes);
                            sub.Commit();
                            views += r.Views.Count; tags += r.Tags;
                            lines.Add(item.Tag + r.Views.Count + " vista(s)" + (item.PluginRebars.Count == 0 ? " (el bloque no tiene armadura del plugin: sin etiquetas)" : ""));
                            lines.AddRange(r.Lines.Select(l => "  " + l));
                            lines.AddRange(r.Warnings.Select(w => "  aviso: " + w));
                        }
                        catch (Exception ex)
                        {
                            sub.RollBack();
                            lines.Add(item.Tag + "ERROR al crear las vistas: " + ex.Message);
                            Log.Error("CreateViewsOnly " + item.Tag, ex);
                        }
                    }
                }
                tx.Commit();
            }
            string report = views + " vista(s) de seccion creadas" + (tags > 0 ? ", " + tags + " etiqueta(s)" : "") + ". No se ha creado ni borrado ninguna barra." +
                            Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, lines) + Environment.NewLine + "log: " + Log.Path;
            Log.Block("Crear vistas de seccion", report);
            ShowReport(commandData, "Vistas de seccion", report);
            return Result.Succeeded;
        }

        private static Result DeletePluginRebars(Document doc, List<HostAnalysis> items, ExternalCommandData commandData)
        {
            var lines = new List<string>();
            int sets = 0, bars = 0, elems = 0;
            using (Transaction tx = new Transaction(doc, "Borrar armado del plugin"))
            {
                tx.Start();
                foreach (HostAnalysis item in items)
                {
                    int n = RebarGenerator.DeletePluginRebars(doc, item.Host, out int b);
                    if (n > 0) { elems++; sets += n; bars += b; }
                    lines.Add(item.Tag + (n > 0 ? n + " conjunto(s), " + b + " barra(s) del plugin borrados" : "sin armadura del plugin"));
                }
                tx.Commit();
            }
            string report = sets + " conjunto(s) (" + bars + " barras) del plugin borrados en " + elems + " elemento(s). Solo se borran los conjuntos con el comentario \"" +
                            RebarGenerator.Marker + "\"; el resto de la armadura no se toca." + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, lines);
            Log.Block("Borrar armado del plugin", report);
            var td = new TaskDialog("Borrar armado del plugin") { MainInstruction = sets + " conjunto(s) del plugin borrados en " + elems + " elemento(s).", MainContent = string.Join(Environment.NewLine, lines) };
            td.Show();
            return Result.Succeeded;
        }

        private static void ShowReport(ExternalCommandData commandData, string title, string text)
        {
            try
            {
                var win = new ReportWindow(title, text);
                try { new WindowInteropHelper(win).Owner = commandData.Application.MainWindowHandle; } catch { }
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                Log.Error("ShowReport", ex);
                TaskDialog.Show(title, text.Length > 3000 ? text.Substring(0, 3000) + "..." + Environment.NewLine + "(informe completo en " + Log.Path + ")" : text);
            }
        }

        private static IList<Element> GetHosts(UIDocument uidoc)
        {
            Document doc = uidoc.Document;
            var sel = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .Where(IsCandidate)
                .ToList();
            if (sel.Count > 0) return sel;

            IList<Reference> refs = uidoc.Selection.PickObjects(
                ObjectType.Element, new HostFilter(),
                "Selecciona los bloques de cimentacion con foso (cimentaciones estructurales) y pulsa Finalizar");
            return refs.Select(r => doc.GetElement(r)).ToList();
        }

        /// <summary>Cimentaciones estructurales: instancias de familia (bloques, zapatas), losas de cimentacion y zapatas corridas.</summary>
        private static bool IsCandidate(Element e)
        {
            if (e == null || e.Category == null) return false;
            try { return e.Category.BuiltInCategory == BuiltInCategory.OST_StructuralFoundation; }
            catch { return false; }
        }

        private class HostFilter : ISelectionFilter
        {
            public bool AllowElement(Element e) => IsCandidate(e);
            public bool AllowReference(Reference r, XYZ p) => false;
        }
    }
}
