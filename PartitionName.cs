using System;
using System.Text.RegularExpressions;

namespace BlockRebar
{
    /// <summary>
    /// Expande la plantilla del parametro Particion: {marca}, {id}, {tipo}, {familia},
    /// {conjunto} y {capa}. Los comodines vacios se eliminan con el separador que
    /// los precede o sigue ("BLQ-{marca}-{familia}" con marca vacia da "BLQ-F1" y no "BLQ--F1").
    /// </summary>
    public static class PartitionName
    {
        public sealed class Source
        {
            public string Mark = "", Id = "", TypeName = "", FamilyName = "", SetName = "", Layer = "";
            /// <summary>Codigo de la familia de armado (F1...F8).</summary>
            public string Family = "";
        }

        public static string Expand(string template, Source s)
        {
            if (string.IsNullOrWhiteSpace(template)) return "";
            string mark = string.IsNullOrWhiteSpace(s.Mark) ? s.Id : s.Mark;
            string result = Regex.Replace(template, @"\{(\w+)\}", m =>
            {
                string key = m.Groups[1].Value.ToLowerInvariant();
                switch (key)
                {
                    case "marca": return mark ?? "";
                    case "id": return s.Id ?? "";
                    case "tipo": return s.TypeName ?? "";
                    case "familia": return s.Family ?? "";
                    case "familiarevit": return s.FamilyName ?? "";
                    case "conjunto": return s.SetName ?? "";
                    case "capa": return s.Layer ?? "";
                    default: return m.Value;
                }
            });
            // separadores huerfanos: dobles, al principio o al final
            result = Regex.Replace(result, @"([-_ /.]){2,}", "$1");
            result = Regex.Replace(result, @"^[-_ /.]+|[-_ /.]+$", "");
            return result.Trim();
        }

        /// <summary>Lista de comodines para la ayuda de la interfaz.</summary>
        public const string Help = "{marca} (Marca del elemento; si esta vacia, el Id), {id}, {tipo} (nombre del tipo), " +
                                   "{familia} (codigo F1...F8 del armado), {familiarevit} (familia de Revit), " +
                                   "{conjunto} (nombre del juego de barras) y {capa} (u, v, cara...).";
    }
}
