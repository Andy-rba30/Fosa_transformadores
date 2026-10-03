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

            // --- 1. Analisis geometrico de cada elemento (solo lectura, sin transaccion) ---
            var items = new List<HostAnalysis>();
            foreach (Element h in hosts)
            {
                HostAnalysis a = HostAnalysis.Analyze(doc, h, cfg);
                a.PluginRebars = RebarGenerator.FindPluginRebars(doc, h);
                if (a.PluginRebars.Count > 0) a.Diagnostics.Add("ya tiene " + a.PluginRebars.Count + " conjunto(s) de armadura creados por el plugin");
                items.Add(a);
                Log.Block(a.Tag.Trim(), string.Join(Environment.NewLine, a.Diagnostics));
            }

            // --- 2. Lamina: el usuario revisa lo detectado y elige el armado ---
            Func<string, double> offset = r => BlockOutline.ElevationOffset(doc, r);
            var win = new RebarOptionsWindow(cfg.Clone(), barTypes, items, offset);
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

            // --- 3a. Borrar el armado del plugin ---
            if (win.DeleteRequested) return DeletePluginRebars(doc, items, commandData);

            if (win.Result == null) return Result.Cancelled;
            cfg = win.Result;

            // --- 3b. Elementos que ya tienen barras del plugin: borrar antes de rearmar o conservar ---
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
                            deletedSets += deleted;
                            total += res.Created.Count;
                            string line = tag + desc + "  ->  " + res.Summary;
                            if (deleted > 0) line += "  (borrados antes " + deleted + " conjuntos, " + deletedBars + " barras del plugin)";
                            if (res.Failed.Count > 0) line += "  INCOMPLETO, no se pudieron crear: " + string.Join(" | ", res.Failed);
                            if (res.Warnings.Count > 0) line += "  AVISOS: " + string.Join(" | ", res.Warnings);
                            if (!res.ComparisonOk) line += "  DIFERENCIAS entre lo previsto y lo leido de Revit (ver detalle)";
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
