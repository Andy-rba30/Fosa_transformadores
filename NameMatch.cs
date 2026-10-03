using System;
using System.Collections.Generic;
using System.Linq;

namespace BlockRebar
{
    /// <summary>
    /// Regla de coincidencia de nombres de tipo (de barra, de etiqueta...), pura: primero el
    /// nombre exacto (sin distinguir mayusculas); si no, todos los que contienen el fragmento.
    /// Un fragmento que coincide con varios tipos es AMBIGUO y no se elige en silencio: la
    /// ventana lo marca y pide elegir uno (que se guarda con su nombre exacto).
    /// </summary>
    public static class NameMatch
    {
        /// <summary>Candidatos: el exacto si existe (uno solo); si no, los que contienen el fragmento, en el orden dado.</summary>
        public static List<string> Candidates(IEnumerable<string> names, string name)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(name) || names == null) return list;
            string wanted = name.Trim();
            var all = names.Where(n => n != null).ToList();
            string exact = all.FirstOrDefault(n => string.Equals(n.Trim(), wanted, StringComparison.OrdinalIgnoreCase));
            if (exact != null) { list.Add(exact); return list; }
            list.AddRange(all.Where(n => n.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0));
            return list;
        }

        /// <summary>El unico candidato, o null si no hay ninguno o hay varios (ambiguo).</summary>
        public static string Unique(IEnumerable<string> names, string name)
        {
            List<string> c = Candidates(names, name);
            return c.Count == 1 ? c[0] : null;
        }

        public static bool IsAmbiguous(IEnumerable<string> names, string name) => Candidates(names, name).Count > 1;
    }
}
