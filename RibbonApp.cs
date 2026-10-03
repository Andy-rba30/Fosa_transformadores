using System;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using Arba.Comun;

namespace BlockRebar
{
    /// <summary>
    /// Entrada de la aplicacion de cinta para BlockRebar en Revit. Anade el boton "Bloques con foso"
    /// (nombre interno ARBA_Acero_Bloques) al desplegable "Acero" del panel "Acero" de la pestana "ARBA".
    /// La pestana, los paneles y el desplegable los gestiona la clase comun Arba.Comun.ArbaRibbon
    /// (external/ARBA-comun), la misma que llevan los demas add-ins ARBA.
    /// </summary>
    public class RibbonApp : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication app)
        {
            try
            {
                ArbaRibbon.Ensure(app);

                string assembly = Assembly.GetExecutingAssembly().Location;
                var data = new PushButtonData("ARBA_Acero_Bloques", "Bloques con foso", assembly, typeof(ArmarBloqueCommand).FullName)
                {
                    ToolTip = "Arma bloques macizos de cimentacion con fosos o canaletas abiertas por arriba (fundaciones de transformador, bloques de equipo, fosos de bombas)",
                    LongDescription = "Selecciona una o varias cimentaciones estructurales cuyo solido ya lleve los fosos recortados y pulsa el boton. " +
                                      "Se abre la lamina con la planta (fosos, plataformas, muretes y lineas de corte A-A y B-B) y las dos secciones con el " +
                                      "armado previsto (F1 malla inferior, F2 malla bajo foso, F3 malla superior, F4 L y F5 horizontales en caras de foso, " +
                                      "F6 verticales, F7 horquillas y F8 horizontales de murete). \"Analizar sin armar\" muestra las caras leidas y el motivo de rechazo.",
                    LargeImage = IconBloques(32),
                    Image = IconBloques(16)
                };

                ArbaRibbon.AddAcero(app, data);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("ARBA", "No se pudo anadir el boton Bloques con foso a la cinta: " + ex.Message +
                                "\nEl comando sigue disponible en Complementos > Herramientas externas.");
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;

        /// <summary>
        /// Icono del boton Bloques con foso: seccion de un bloque de cimentacion con el foso
        /// perimetral (dos canales), el nucleo en el centro, los muretes a los lados, la malla
        /// inferior con patas y la barra en L de la cara del foso.
        /// </summary>
        public static BitmapSource IconBloques(int size)
        {
            double s = size / 32.0;
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                var concrete = new SolidColorBrush(Color.FromRgb(0xD9, 0xD9, 0xD9));
                var recess = new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF4));
                var edge = new Pen(new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)), 1.2 * s);
                var bar = new Pen(new SolidColorBrush(Color.FromRgb(0x8B, 0x2E, 0x2E)), 1.6 * s) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                var barL = new Pen(new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)), 1.6 * s) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                var dot = new SolidColorBrush(Color.FromRgb(0x7A, 0x3E, 0x9D));
                var soil = new Pen(new SolidColorBrush(Color.FromRgb(0xA8, 0x8A, 0x5A)), 1.0 * s);

                // perfil: murete - foso - nucleo - foso - murete sobre una base
                var outline = new StreamGeometry();
                using (StreamGeometryContext g = outline.Open())
                {
                    g.BeginFigure(new Point(1.5 * s, 28 * s), true, true);
                    g.LineTo(new Point(1.5 * s, 4 * s), true, false);
                    g.LineTo(new Point(5 * s, 4 * s), true, false);
                    g.LineTo(new Point(5 * s, 16 * s), true, false);
                    g.LineTo(new Point(11 * s, 16 * s), true, false);
                    g.LineTo(new Point(11 * s, 4 * s), true, false);
                    g.LineTo(new Point(21 * s, 4 * s), true, false);
                    g.LineTo(new Point(21 * s, 16 * s), true, false);
                    g.LineTo(new Point(27 * s, 16 * s), true, false);
                    g.LineTo(new Point(27 * s, 4 * s), true, false);
                    g.LineTo(new Point(30.5 * s, 4 * s), true, false);
                    g.LineTo(new Point(30.5 * s, 28 * s), true, false);
                }
                dc.DrawGeometry(concrete, edge, outline);
                dc.DrawRectangle(recess, null, new System.Windows.Rect(5.6 * s, 4.6 * s, 4.8 * s, 11 * s));
                dc.DrawRectangle(recess, null, new System.Windows.Rect(21.6 * s, 4.6 * s, 4.8 * s, 11 * s));
                // malla inferior con patas hacia arriba
                dc.DrawLine(bar, new Point(4 * s, 24.5 * s), new Point(28 * s, 24.5 * s));
                dc.DrawLine(bar, new Point(4 * s, 24.5 * s), new Point(4 * s, 19 * s));
                dc.DrawLine(bar, new Point(28 * s, 24.5 * s), new Point(28 * s, 19 * s));
                for (double x = 8; x <= 25; x += 4.2)
                    dc.DrawEllipse(dot, null, new Point(x * s, 22.3 * s), 1.0 * s, 1.0 * s);
                // barra en L en la cara del foso del nucleo
                dc.DrawLine(barL, new Point(13 * s, 6 * s), new Point(13 * s, 19 * s));
                dc.DrawLine(barL, new Point(13 * s, 19 * s), new Point(8.5 * s, 19 * s));
                dc.DrawLine(barL, new Point(19 * s, 6 * s), new Point(19 * s, 19 * s));
                dc.DrawLine(barL, new Point(19 * s, 19 * s), new Point(23.5 * s, 19 * s));
                // terreno
                for (double x = 2; x < 31; x += 5)
                    dc.DrawLine(soil, new Point(x * s, 31 * s), new Point((x + 3) * s, 29 * s));
            }
            var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(visual);
            bmp.Freeze();
            return bmp;
        }
    }
}
