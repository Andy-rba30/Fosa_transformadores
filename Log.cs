using System;
using System.IO;
using System.Text;

namespace BlockRebar
{
    /// <summary>
    /// Registro de texto en %Temp%\BlockRebar.log: diagnostico de cada elemento, avisos y
    /// excepciones. Es lo que se pide pegar cuando algo falla en Revit. Nunca lanza.
    /// </summary>
    public static class Log
    {
        public static string Path => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BlockRebar.log");

        private static readonly object Gate = new object();

        public static void Write(string text)
        {
            try
            {
                lock (Gate)
                {
                    // se recorta cuando pasa de 2 MB para que no crezca sin limite
                    var fi = new FileInfo(Path);
                    if (fi.Exists && fi.Length > 2 * 1024 * 1024) fi.Delete();
                    File.AppendAllText(Path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + text + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch { }
        }

        public static void Block(string title, string body)
        {
            var sb = new StringBuilder();
            sb.AppendLine("==== " + title + " ====");
            sb.Append(body);
            Write(sb.ToString());
        }

        public static void Error(string where, Exception ex) => Write("ERROR en " + where + ": " + ex);
    }
}
