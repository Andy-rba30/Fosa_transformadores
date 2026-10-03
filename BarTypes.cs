using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace BlockRebar
{
    /// <summary>
    /// Tipos de barra del proyecto (RebarBarType) con sus medidas reales. La busqueda por
    /// nombre sigue la regla de NameMatch: exacto primero; un fragmento que coincide con
    /// varios tipos ("5/8" con "5/8\"" y "Ø 5/8\"") es ambiguo y no se elige en silencio.
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

        /// <summary>Familia (y capa) cuyo nombre de tipo en la configuracion coincide con varios tipos.</summary>
        public sealed class Ambiguity
        {
            public string Key;
            public string Name;
            public List<string> Candidates = new List<string>();
            public string Describe() => Key + " (\"" + Name + "\" coincide con " + string.Join(", ", Candidates.Select(c => "\"" + c + "\"")) + ")";
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

        /// <summary>Clave de una familia y capa en la configuracion: "F1:u", "F4".</summary>
        public static string Key(Family f, string layer) => Families.Code(f) + (string.IsNullOrEmpty(layer) ? "" : ":" + layer);

        public static List<Info> Candidates(IList<Info> all, string name)
        {
            List<string> names = NameMatch.Candidates(all.Select(i => i.Name), name);
            return names.Select(n => all.First(i => i.Name == n)).ToList();
        }

        /// <summary>El tipo que corresponde al nombre, o null si no existe o es ambiguo.</summary>
        public static Info Find(IList<Info> all, string name)
        {
            List<Info> c = Candidates(all, name);
            return c.Count == 1 ? c[0] : null;
        }

        /// <summary>Tipo de barra de Revit por nombre exacto o fragmento unico; lanza si no existe o es ambiguo.</summary>
        public static RebarBarType FindBarType(Document doc, string name, string use)
        {
            var all = AllBarTypes(doc);
            if (all.Count == 0)
                throw new InvalidOperationException("El proyecto no tiene ningun tipo de barra (RebarBarType). Carga una familia de armadura primero.");
            List<string> c = NameMatch.Candidates(all.Select(b => b.Name), name);
            if (c.Count == 0)
                throw new InvalidOperationException("el tipo de barra de " + use + " \"" + name + "\" no existe en este proyecto; elige uno de los cargados en la ventana");
            if (c.Count > 1)
                throw new InvalidOperationException("el tipo de barra de " + use + " \"" + name + "\" es ambiguo (" + string.Join(", ", c) + "); elige uno en la ventana");
            return all.First(b => b.Name == c[0]);
        }

        public static PlanDiameters Diameters(IList<Info> all, AppConfig cfg, out List<string> missing) =>
            Diameters(all, cfg, out missing, out _);

        /// <summary>
        /// Diametros de cada familia activa segun la configuracion. Las familias sin tipo (no
        /// elegido, inexistente o ambiguo) quedan sin diametro: BlockPlan lo dice y Armar no se
        /// activa. Las ambiguas se devuelven aparte con sus candidatos.
        /// </summary>
        public static PlanDiameters Diameters(IList<Info> all, AppConfig cfg, out List<string> missing, out List<Ambiguity> ambiguous)
        {
            missing = new List<string>();
            ambiguous = new List<Ambiguity>();
            var d = new PlanDiameters();
            foreach ((Family f, string layer, string name) in cfg.BarTypesNeeded())
            {
                string key = Key(f, layer);
                List<Info> c = Candidates(all, name);
                if (c.Count == 1) { d.Set(f, layer, c[0].DiameterFt, c[0].Name, c[0].BendInsideFt, c[0].TieBendInsideFt); continue; }
                missing.Add(key + (c.Count > 1 ? " (ambiguo)" : (string.IsNullOrWhiteSpace(name) ? "" : " (\"" + name + "\" no existe)")));
                if (c.Count > 1) ambiguous.Add(new Ambiguity { Key = key, Name = name, Candidates = c.Select(i => i.Name).ToList() });
            }
            return d;
        }
    }
}
