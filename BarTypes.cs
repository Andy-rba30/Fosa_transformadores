using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace BlockRebar
{
    /// <summary>
    /// Tipos de barra del proyecto (RebarBarType): busqueda por nombre exacto o fragmento
    /// ("5/8") y diametros reales para el plan y la lamina. Lo comparten la ventana y, en la
    /// entrega 2b, el generador.
    /// </summary>
    public static class BarTypes
    {
        /// <summary>Un tipo de barra con sus medidas en pies.</summary>
        public sealed class Info
        {
            public string Name;
            public double DiameterFt;
            /// <summary>Diametro interior de doblado estandar (pies, 0 si no se pudo leer).</summary>
            public double BendInsideFt;
            /// <summary>Diametro interior de doblado de estribo / horquilla (pies, 0 si no se pudo leer).</summary>
            public double TieBendInsideFt;
            public double DiameterMm => DiameterFt * BlockPlan.MmPerFt;
            public string Display => Name + " (" + DiameterMm.ToString("0.#", CultureInfo.InvariantCulture) + " mm)";
        }

        public static List<RebarBarType> AllBarTypes(Document doc) =>
            new FilteredElementCollector(doc).OfClass(typeof(RebarBarType)).Cast<RebarBarType>()
                .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>Todos los tipos con sus medidas, ordenados por diametro y nombre.</summary>
        public static List<Info> Read(Document doc)
        {
            var list = new List<Info>();
            foreach (RebarBarType bt in AllBarTypes(doc))
            {
                var i = new Info { Name = bt.Name };
                try { i.DiameterFt = bt.BarNominalDiameter; } catch { }
                if (i.DiameterFt <= 0) { try { i.DiameterFt = bt.BarModelDiameter; } catch { } }
                try { i.BendInsideFt = bt.StandardBendDiameter; } catch { }
                try { i.TieBendInsideFt = bt.StirrupTieBendDiameter; } catch { }
                if (i.DiameterFt > 0) list.Add(i);
            }
            return list.OrderBy(i => i.DiameterFt).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>Nombre exacto (sin distinguir mayusculas) o, si no, el primero que contiene el fragmento; null si no hay.</summary>
        public static string MatchName(IEnumerable<string> names, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var list = names.ToList();
            string exact = list.FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            return list.FirstOrDefault(n => n.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static Info Find(IList<Info> all, string name)
        {
            string match = MatchName(all.Select(i => i.Name), name);
            return match == null ? null : all.First(i => i.Name == match);
        }

        public static RebarBarType FindBarType(Document doc, string name, string use)
        {
            var all = AllBarTypes(doc);
            if (all.Count == 0)
                throw new InvalidOperationException("El proyecto no tiene ningun tipo de barra (RebarBarType). Carga una familia de armadura primero.");
            string match = MatchName(all.Select(b => b.Name), name);
            if (match == null)
                throw new InvalidOperationException("el tipo de barra de " + use + " \"" + name + "\" no existe en este proyecto; elige uno de los cargados en la ventana");
            return all.First(b => b.Name == match);
        }

        /// <summary>
        /// Diametros de cada familia activa segun la configuracion. Las familias sin tipo
        /// elegido (o cuyo tipo no existe) quedan sin diametro y BlockPlan lo dice.
        /// </summary>
        public static PlanDiameters Diameters(IList<Info> all, AppConfig cfg, out List<string> missing)
        {
            missing = new List<string>();
            var d = new PlanDiameters();
            foreach ((Family f, string layer, string name) in cfg.BarTypesNeeded())
            {
                Info i = Find(all, name);
                if (i == null) { missing.Add(Families.Code(f) + (layer != "" ? " (" + layer + ")" : "")); continue; }
                d.Set(f, layer, i.DiameterFt, i.Name, i.BendInsideFt, i.TieBendInsideFt);
            }
            return d;
        }
    }
}
