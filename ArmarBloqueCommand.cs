using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace BlockRebar
{
    /// <summary>
    /// Comando "Bloques con foso": selecciona cimentaciones estructurales, lee su geometria
    /// (sin transaccion), abre la lamina y, cuando el armado este disponible (entrega 2b),
    /// crea las barras con una subtransaccion por elemento. En la entrega 2a solo lee y
    /// diagnostica: no se modifica el modelo.
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
            if (ok != true || win.Result == null) return Result.Cancelled;

            // --- 3. Armado: entrega 2b (RebarGenerator). En 2a no se crea nada. ---
            TaskDialog.Show("Bloques con foso", "La creacion de barras en Revit llega en la entrega 2b. No se ha modificado el modelo.");
            return Result.Succeeded;
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
