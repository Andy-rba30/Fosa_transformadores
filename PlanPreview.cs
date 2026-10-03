using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BlockRebar
{
    /// <summary>Colores de la lamina, compartidos por la planta, las secciones y la leyenda: uno por familia F1...F8.</summary>
    public static class PlanColors
    {
        public static readonly Brush Concrete = Freeze(Rgb(0xE6, 0xE6, 0xE6));     // cuerpo (contorno inferior)
        public static readonly Brush Platform = Freeze(Rgb(0xD6, 0xD6, 0xD6));     // plataforma (nucleo) a zTope
        public static readonly Brush Wall = Freeze(Rgb(0xC3, 0xC9, 0xD2));         // murete a zTope
        public static readonly Brush Recess = Freeze(Rgb(0xF6, 0xEF, 0xDA));       // fondo de foso
        public static readonly Brush RecessEdge = Freeze(Rgb(0x6E, 0x6E, 0x6E));
        public static readonly Brush Edge = Freeze(Rgb(0x50, 0x50, 0x50));
        public static readonly Brush Soil = Freeze(Rgb(0xC9, 0xB9, 0x9A));         // terreno (seccion)
        public static readonly Brush Cut = Freeze(Rgb(0x1E, 0x4F, 0xA0));          // lineas de corte A-A / B-B
        public static readonly Brush Highlight = Freeze(Rgb(0xFF, 0xC2, 0x00));    // barra resaltada
        public static readonly Brush Dim = Freeze(Rgb(0x55, 0x55, 0x55));          // cotas
        public static readonly Brush Cover = Freeze(Rgb(0x9A, 0x9A, 0x9A));        // lineas de recubrimiento
        public static readonly Brush Level = Freeze(Rgb(0x30, 0x30, 0x30));
        public static readonly Brush Angle = Freeze(Rgb(0x3A, 0x3F, 0x47));          // angulos de borde (acero)
        public static readonly Brush GridFill = Freeze(Rgb(0xC6, 0xCE, 0xD6));       // rejillas
        public static readonly Brush GridEdge = Freeze(Rgb(0x5A, 0x64, 0x72));

        private static readonly Brush[] Fam =
        {
            Freeze(Rgb(0x8B, 0x2E, 0x2E)),   // F1 malla inferior: rojo oscuro
            Freeze(Rgb(0x7A, 0x3E, 0x9D)),   // F2 malla bajo foso: morado
            Freeze(Rgb(0xD9, 0x6C, 0x2A)),   // F3 malla superior: naranja
            Freeze(Rgb(0x2F, 0x6D, 0xB5)),   // F4 L en cara de foso: azul
            Freeze(Rgb(0x1E, 0x8C, 0x8C)),   // F5 horizontales de cara de foso: verde azulado
            Freeze(Rgb(0x3E, 0x8E, 0x3E)),   // F6 verticales de murete: verde
            Freeze(Rgb(0xB5, 0x3A, 0x90)),   // F7 horquillas: magenta
            Freeze(Rgb(0x8A, 0x6A, 0x2A))    // F8 horizontales de murete: marron
        };

        public static Brush Of(Family f) => Fam[(int)f];

        private static SolidColorBrush Rgb(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));
        private static Brush Freeze(Brush b) { b.Freeze(); return b; }
    }

    /// <summary>
    /// Planta de la lamina: contorno inferior con huecos, fosos rayados, plataformas y
    /// muretes con tintas distintas, las barras de la capa elegida (F1, F2 o F3) a su grosor y
    /// la traza en planta de las familias de cara (F4...F8), ejes u/v, y las dos lineas de
    /// corte A-A (a lo largo de u) y B-B (a lo largo de v), que se arrastran para mover las
    /// secciones. Rueda: zoom; arrastrar el fondo: mover; doble clic: encajar. Pasar el raton
    /// por una barra la resalta (con su conjunto) en las tres vistas.
    /// </summary>
    public sealed class PlanPreview : Canvas
    {
        private readonly PreviewState _state;
        private BlockFrame _f;
        private BlockPlan _plan;
        private GridPlan _grid;
        private string _message = "Sin elemento armable";

        private double _zoom = 1;
        private Vector _pan;
        private double _x0, _y0, _k;
        private Point _dragStart;
        private Vector _panStart;
        private bool _dragging;
        private char _dragCut;
        private readonly List<(PlannedBar bar, Point a, Point b)> _hits = new List<(PlannedBar, Point, Point)>();

        private const double FtToMm = 304.8;

        public PlanPreview(PreviewState state)
        {
            _state = state;
            Background = RevitTheme.Paper;
            ClipToBounds = true;
            SizeChanged += (s, e) => Redraw();
            MouseWheel += OnWheel;
            MouseLeftButtonDown += OnDown;
            MouseMove += OnMove;
            MouseLeftButtonUp += OnUp;
            MouseLeave += (s, e) => { EndDrag(); _state.Hover = null; };
            Cursor = Cursors.Hand;
        }

        public void Show(BlockFrame f, BlockPlan plan, GridPlan grid = null)
        {
            bool changed = !ReferenceEquals(_f, f);
            _f = f; _plan = plan; _grid = grid;
            if (changed) ResetView(); else Redraw();
        }

        public void Clear(string message)
        {
            _f = null; _plan = null; _grid = null; _message = message;
            Redraw();
        }

        public void ResetView()
        {
            _zoom = 1; _pan = new Vector(0, 0);
            Redraw();
        }

        /// <summary>Redibuja con el estado compartido actual (cortes, resaltado, familia aislada, capa).</summary>
        public void Refresh() => Redraw();

        // --- raton ---
        private void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (_f == null) return;
            double factor = e.Delta > 0 ? 1.25 : 1 / 1.25;
            double newZoom = Math.Max(1, Math.Min(60, _zoom * factor));
            factor = newZoom / _zoom;
            Point m = e.GetPosition(this);
            _pan = new Vector(m.X - _x0 - (m.X - _pan.X - _x0) * factor, m.Y - _y0 - (m.Y - _pan.Y - _y0) * factor);
            _zoom = newZoom;
            if (_zoom <= 1.0001) _pan = new Vector(0, 0);
            Redraw();
            e.Handled = true;
        }

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            if (_f == null) return;
            if (e.ClickCount == 2) { ResetView(); e.Handled = true; return; }
            Point m = e.GetPosition(this);
            char c = CutAt(m);
            if (c != '\0')
            {
                _dragCut = c;
                CaptureMouse();
                e.Handled = true;
                return;
            }
            _dragging = true; _dragStart = m; _panStart = _pan;
            CaptureMouse();
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (_f == null) return;
            Point m = e.GetPosition(this);
            BlockTopology t = _f.Topology;
            if (_dragCut != '\0')
            {
                if (_dragCut == 'A') _state.CutA = Snap(V(m.Y), t.VMin, t.VMax);
                else _state.CutB = Snap(U(m.X), t.UMin, t.UMax);
                return;
            }
            if (_dragging)
            {
                _pan = _panStart + (m - _dragStart);
                Redraw();
                return;
            }
            char c = CutAt(m);
            Cursor = c == 'A' ? Cursors.SizeNS : (c == 'B' ? Cursors.SizeWE : Cursors.Hand);
            _state.Hover = c == '\0' ? HitBar(m) : null;
        }

        private void OnUp(object sender, MouseButtonEventArgs e) => EndDrag();

        private void EndDrag()
        {
            _dragging = false; _dragCut = '\0';
            ReleaseMouseCapture();
        }

        /// <summary>Coordenada del corte dentro del bloque, con iman al centro.</summary>
        private static double Snap(double x, double min, double max)
        {
            double c = 0.5 * (min + max), L = max - min;
            if (Math.Abs(x - c) < 0.015 * L) return c;
            double tol = Math.Min(0.01 * L, BlockPlan.Mm(10));
            return Math.Max(min + tol, Math.Min(max - tol, x));
        }

        private double U(double x) => _f.Topology.UMin + (x - (_x0 + _pan.X)) / _k;
        private double V(double y) => _f.Topology.VMin + ((_y0 + _pan.Y) - y) / _k;

        /// <summary>'A' o 'B' si el punto esta sobre una linea de corte (6 px), '\0' si no.</summary>
        private char CutAt(Point m)
        {
            if (_f == null || _k <= 0) return '\0';
            BlockTopology t = _f.Topology;
            double x0 = _x0 + _pan.X, y0 = _y0 + _pan.Y;
            double yA = y0 - (_state.CutA - t.VMin) * _k, xB = x0 + (_state.CutB - t.UMin) * _k;
            double xa = x0 - 32, xb = x0 + t.Width * _k + 32, ya = y0 - t.Depth * _k - 32, yb = y0 + 32;
            bool nearA = Math.Abs(m.Y - yA) <= 6 && m.X >= xa && m.X <= xb;
            bool nearB = Math.Abs(m.X - xB) <= 6 && m.Y >= ya && m.Y <= yb;
            if (nearA && nearB) return Math.Abs(m.Y - yA) <= Math.Abs(m.X - xB) ? 'A' : 'B';
            return nearA ? 'A' : (nearB ? 'B' : '\0');
        }

        private PlannedBar HitBar(Point m)
        {
            PlannedBar best = null; double bestD = 6;
            foreach ((PlannedBar bar, Point a, Point b) in _hits)
            {
                double d = DistToSegment(m, a, b);
                if (d < bestD) { bestD = d; best = bar; }
            }
            return best;
        }

        internal static double DistToSegment(Point p, Point a, Point b)
        {
            Vector ab = b - a, ap = p - a;
            double len2 = ab.LengthSquared;
            double t = len2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, (ap.X * ab.X + ap.Y * ab.Y) / len2));
            Point q = a + ab * t;
            return (p - q).Length;
        }

        // --- dibujo ---
        private static string Mm(double ft) => Math.Round(ft * FtToMm).ToString(CultureInfo.InvariantCulture);
        private static string M(double ft) => (ft * 0.3048).ToString("0.00", CultureInfo.InvariantCulture) + " m";

        private void Redraw()
        {
            Children.Clear();
            _hits.Clear();
            double W = ActualWidth, H = ActualHeight;
            if (W < 10 || H < 10) return;
            if (_f == null)
            {
                Text(_message, 10, 10, Brushes.Gray, 12);
                return;
            }

            BlockTopology t = _f.Topology;
            // bandas reservadas: izquierda (letra A, ejes), derecha (letra A, rotulo A-A, cota v), arriba (letra B, rotulo B-B, texto de hover), abajo (cota u, letra B, rotulo B-B, estado)
            const double left = 66, right = 150, top = 58, bottom = 92;
            double aw = Math.Max(W - left - right, 40), ah = Math.Max(H - top - bottom, 40);
            double k = Math.Min(aw / Math.Max(t.Width, 1e-6), ah / Math.Max(t.Depth, 1e-6)) * _zoom;
            _k = k;
            // origen sin zoom (esquina inferior izquierda del bloque, centrada en el area util); el zoom crece desde ahi y el desplazamiento se suma
            _x0 = left + 0.5 * (aw - t.Width * k / _zoom);
            _y0 = top + 0.5 * (ah - t.Depth * k / _zoom) + t.Depth * k / _zoom;
            double x0 = _x0 + _pan.X, y0 = _y0 + _pan.Y;
            Func<double, double> X = u => x0 + (u - t.UMin) * k;
            Func<double, double> Y = v => y0 - (v - t.VMin) * k;

            // cuerpo (contorno inferior con huecos pasantes)
            foreach (Region2D b in t.Body)
                Children.Add(new Path { Data = RingsGeometry(b.Rings(), X, Y), Fill = PlanColors.Concrete, Stroke = PlanColors.Edge, StrokeThickness = 1.3 });

            // regiones del tope: plataformas y muretes
            foreach (TopRegion r in t.Regions)
                Children.Add(new Path
                {
                    Data = RingsGeometry(r.Shape.Rings(), X, Y),
                    Fill = r.Kind == RegionKind.Platform ? PlanColors.Platform : PlanColors.Wall,
                    Stroke = PlanColors.Edge, StrokeThickness = 0.7,
                    ToolTip = r.Describe()
                });

            // fosos: fondo claro con rayado y rotulo
            foreach (Recess rc in t.Recesses)
            {
                Geometry g = RingsGeometry(rc.Shape.Rings(), X, Y);
                Children.Add(new Path { Data = g, Fill = PlanColors.Recess, Stroke = PlanColors.RecessEdge, StrokeThickness = 1.0, ToolTip = rc.Describe() });
                var hatch = new StreamGeometry();
                using (StreamGeometryContext c = hatch.Open())
                {
                    Rect bb = g.Bounds;
                    for (double d = bb.Left - bb.Height; d < bb.Right; d += 9)
                    {
                        c.BeginFigure(new Point(d, bb.Bottom), false, false);
                        c.LineTo(new Point(d + bb.Height, bb.Top), true, false);
                    }
                }
                hatch.Freeze();
                Children.Add(new Path { Data = hatch, Stroke = PlanColors.RecessEdge, StrokeThickness = 0.5, Clip = g, IsHitTestVisible = false, Opacity = 0.6 });
                Pt c0 = Geometry2D.InnerPoint(rc.Shape.Outer);
                Text("foso " + (rc.Index + 1) + "  -" + Mm(rc.Depth), X(c0.U) - 24, Y(c0.V) - 8, PlanColors.RecessEdge, 10, true);
            }

            // rejillas: piezas rayadas con su grupo
            if (_grid != null && _grid.Error == null && _state.ShowGrids && _grid.Cfg.Mode != "off")
            {
                var labeled = new HashSet<string>();
                foreach (GridPiece p in _grid.Pieces)
                {
                    double x1 = X(p.UMin), x2 = X(p.UMax), y1 = Y(p.VMax), y2 = Y(p.VMin);
                    var rect = new Rectangle { Width = Math.Max(1, x2 - x1), Height = Math.Max(1, y2 - y1), Fill = PlanColors.GridFill, Stroke = PlanColors.GridEdge, StrokeThickness = 0.8, Opacity = 0.9,
                                               ToolTip = "rejilla " + p.Group + ": " + Mm(p.Length) + " x " + Mm(p.Width) + " mm, foso " + (p.Recess + 1) };
                    SetLeft(rect, x1); SetTop(rect, y1);
                    Children.Add(rect);
                    // barras portantes (a lo ancho de la pieza) cada 30 mm, solo si se ven
                    double step = BlockPlan.Mm(30) * k;
                    if (step >= 4)
                    {
                        var hatch = new StreamGeometry();
                        using (StreamGeometryContext c = hatch.Open())
                        {
                            if (p.AlongU) for (double x = x1 + step; x < x2; x += step) { c.BeginFigure(new Point(x, y1), false, false); c.LineTo(new Point(x, y2), true, false); }
                            else for (double y = y1 + step; y < y2; y += step) { c.BeginFigure(new Point(x1, y), false, false); c.LineTo(new Point(x2, y), true, false); }
                        }
                        hatch.Freeze();
                        Children.Add(new Path { Data = hatch, Stroke = PlanColors.GridEdge, StrokeThickness = 0.4, IsHitTestVisible = false, Opacity = 0.7 });
                    }
                    if (labeled.Add(p.Group + ":" + p.Recess + ":" + p.AlongU)) Text(p.Group, 0.5 * (x1 + x2) - 7, 0.5 * (y1 + y2) - 7, PlanColors.GridEdge, 9, true);
                }
            }

            if (_plan != null && _plan.Error == null)
            {
                Family layer = _state.PlanLayer;
                // barras de la capa elegida (malla F1, F2 o F3) a su grosor
                foreach (PlannedBar b in _plan.Bars.Where(b => b.Family == layer && _state.Visible(b.Family)).OrderBy(b => b.ZMin))
                    DrawBar(b, X, Y, k, Math.Max(1.1, b.D * k), false);
                // traza de las familias de cara (y de las otras mallas, muy finas y claras, para situarlas)
                foreach (PlannedBar b in _plan.Bars.Where(b => b.Family != layer && _state.Visible(b.Family)))
                {
                    bool face = (int)b.Family >= (int)Family.F4;
                    if (!face && _state.Isolated != b.Family) continue;   // las otras mallas solo si se aislan
                    DrawBar(b, X, Y, k, face ? Math.Max(1.0, 0.6 * b.D * k) : Math.Max(1.1, b.D * k), true);
                }
            }

            // angulos de borde: linea gruesa a media ala del borde, hacia el foso
            if (_grid != null && _grid.Error == null && _state.ShowAngles && _grid.Cfg.Angles.Enabled)
            {
                double leg = BlockPlan.Mm(_grid.Cfg.Angles.LegMm);
                foreach (AngleBar a in _grid.Angles)
                {
                    Pt oa = new Pt(a.A.U + a.Inward.U * 0.5 * leg, a.A.V + a.Inward.V * 0.5 * leg), ob = new Pt(a.B.U + a.Inward.U * 0.5 * leg, a.B.V + a.Inward.V * 0.5 * leg);
                    Children.Add(new Line
                    {
                        X1 = X(oa.U), Y1 = Y(oa.V), X2 = X(ob.U), Y2 = Y(ob.V), Stroke = PlanColors.Angle, StrokeThickness = Math.Max(2.5, leg * k),
                        StrokeStartLineCap = PenLineCap.Flat, StrokeEndLineCap = PenLineCap.Flat, Opacity = 0.85,
                        ToolTip = a.Describe() + ", " + Mm(a.Length) + " mm"
                    });
                }
            }

            // ejes locales, fuera del bloque (esquina inferior izquierda)
            double ax = X(t.UMin) - 44, ay = Y(t.VMin) + 10;
            Arrow(ax, ay, ax + 30, ay, PlanColors.Dim);
            Arrow(ax, ay, ax, ay - 30, PlanColors.Dim);
            Text("u", ax + 20, ay + 1, PlanColors.Dim, 10, true);
            Text("v", ax - 11, ay - 34, PlanColors.Dim, 10, true);

            // cotas generales: u bajo el bloque (en la mitad mas ancha que deja la linea B-B), v a la derecha (en la mitad mas alta que deja A-A)
            if (_state.ShowDims)
            {
                double yU = Y(t.VMin) + 14;
                HDim(X(t.UMin), X(t.UMax), yU, PlanColors.Dim);
                double uText = _state.CutB - t.UMin > t.UMax - _state.CutB ? 0.5 * (t.UMin + _state.CutB) : 0.5 * (_state.CutB + t.UMax);
                Text(M(t.Width) + " (u)", X(uText) - 30, yU + 3, PlanColors.Dim, 10);
                double xV = X(t.UMax) + 14;
                VDim(xV, Y(t.VMin), Y(t.VMax), PlanColors.Dim);
                double vText = _state.CutA - t.VMin > t.VMax - _state.CutA ? 0.5 * (t.VMin + _state.CutA) : 0.5 * (_state.CutA + t.VMax);
                var tv = new TextBlock { Text = M(t.Depth) + " (v)", Foreground = PlanColors.Dim, FontSize = 10, IsHitTestVisible = false, LayoutTransform = new RotateTransform(-90) };
                tv.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                SetLeft(tv, xV + 4); SetTop(tv, Y(vText) - 0.5 * tv.DesiredSize.Height);
                Children.Add(tv);
            }

            // lineas de corte
            CutLine(true, X, Y, t);
            CutLine(false, X, Y, t);

            // resumen y estado
            if (_plan == null) { }
            else if (_plan.Error != null) Text(_plan.Error, 10, 10, Brushes.Firebrick, 12);
            else
            {
                string s = "planta: " + Families.Code(_state.PlanLayer) + " " + Families.Name(_state.PlanLayer) + " a su grosor; F4...F8 por su traza. " + _plan.Describe();
                Text(s, 8, H - 17, PlanColors.Dim, 9);
                if (_plan.Warnings.Count > 0) Text(string.Join(" | ", _plan.Warnings), 8, H - 31, Brushes.Firebrick, 9);
            }
            if (_state.Hover != null) Text(HoverText(_state.Hover, _plan), 8, 6, Brushes.Black, 11, true);
        }

        /// <summary>Descripcion de la barra bajo el raton: familia, diametro, separacion del conjunto y longitud.</summary>
        public static string HoverText(PlannedBar b, BlockPlan plan)
        {
            BarGroup g = plan?.GroupOf(b);
            string s = Families.Code(b.Family) + (b.Layer != "" ? "(" + b.Layer + ")" : "") + " " + Families.Name(b.Family) + ": ø" +
                       (plan != null && plan.Diam.Label(b.Family, b.Layer) != "" ? plan.Diam.Label(b.Family, b.Layer) : BlockPlan.Dia(b.D) + " mm") +
                       ", L=" + Mm(b.Length) + " mm" + (b.HasLegs ? " (" + (b.Points.Count - 1) + " tramos)" : "");
            if (g != null && g.Count > 1) s += ", conjunto de " + g.Count + " @" + Mm(g.First.NominalSpacing > 0 ? g.First.NominalSpacing : g.Spacing);
            if (b.Face != "") s += " [" + b.Face + "]";
            return s;
        }

        private void DrawBar(PlannedBar b, Func<double, double> X, Func<double, double> Y, double k, double th, bool trace)
        {
            bool hl = _state.IsHighlighted(b, _plan);
            Brush brush = hl ? PlanColors.Highlight : PlanColors.Of(b.Family);
            if (hl) th = Math.Max(th, 3);
            double tol = BlockPlan.Mm(1);
            bool anyPlan = false;
            for (int i = 0; i + 1 < b.Points.Count; i++)
            {
                P3 p = b.Points[i], q = b.Points[i + 1];
                var a = new Point(X(p.U), Y(p.V)); var c = new Point(X(q.U), Y(q.V));
                if (p.Plan.DistanceTo(q.Plan) > tol)
                {
                    anyPlan = true;
                    Children.Add(new Line
                    {
                        X1 = a.X, Y1 = a.Y, X2 = c.X, Y2 = c.Y, Stroke = brush, StrokeThickness = th,
                        StrokeStartLineCap = PenLineCap.Flat, StrokeEndLineCap = PenLineCap.Flat, Opacity = trace && !hl ? 0.85 : 1
                    });
                    _hits.Add((b, a, c));
                }
                else
                {
                    // tramo vertical (pata): marca perpendicular a la barra en ese punto
                    P3 dir = i + 2 < b.Points.Count ? b.Points[i + 2] - q : (i > 0 ? p - b.Points[i - 1] : new P3(1, 0, 0));
                    double L = Math.Sqrt(dir.U * dir.U + dir.V * dir.V);
                    double nu = L > 1e-9 ? -dir.V / L : 0, nv = L > 1e-9 ? dir.U / L : 1;
                    double tick = Math.Max(4, 1.5 * th);
                    Children.Add(new Line { X1 = a.X - nu * tick, Y1 = a.Y + nv * tick, X2 = a.X + nu * tick, Y2 = a.Y - nv * tick, Stroke = brush, StrokeThickness = Math.Max(1, 0.8 * th) });
                    _hits.Add((b, a, a));
                }
            }
            if (!anyPlan)
            {
                // barra vertical (F6): un punto a su diametro
                var a = new Point(X(b.First.U), Y(b.First.V));
                double r = Math.Max(1.6, 0.5 * b.D * k);
                var e = new Ellipse { Width = 2 * r, Height = 2 * r, Fill = brush };
                SetLeft(e, a.X - r); SetTop(e, a.Y - r);
                Children.Add(e);
                _hits.Add((b, a, a));
            }
        }

        private void CutLine(bool alongU, Func<double, double> X, Func<double, double> Y, BlockTopology t)
        {
            string letter = alongU ? "A" : "B";
            Brush brush = PlanColors.Cut;
            var dash = new DoubleCollection { 10, 3, 2, 3 };
            if (alongU)
            {
                double y = Y(_state.CutA), x1 = X(t.UMin) - 30, x2 = X(t.UMax) + 30;
                Children.Add(new Line { X1 = x1, Y1 = y, X2 = x2, Y2 = y, Stroke = brush, StrokeThickness = 1.8, StrokeDashArray = dash, IsHitTestVisible = false });
                // flechas de mirada hacia +v (en la seccion A-A u crece hacia la derecha)
                Arrow(x1 + 6, y, x1 + 6, y - 16, brush);
                Arrow(x2 - 6, y, x2 - 6, y - 16, brush);
                Text(letter, x1 - 13, y - 9, brush, 13, true);
                Text(letter, x2 + 3, y - 9, brush, 13, true);
                // rotulo en la banda derecha, junto a la letra (la cota v queda entre el bloque y la linea)
                Text(letter + "-" + letter + "  v = " + M(_state.CutA), x2 + 16, y - 7, brush, 9);
            }
            else
            {
                double x = X(_state.CutB), y1 = Y(t.VMax) - 30, y2 = Y(t.VMin) + 30;
                Children.Add(new Line { X1 = x, Y1 = y1, X2 = x, Y2 = y2, Stroke = brush, StrokeThickness = 1.8, StrokeDashArray = dash, IsHitTestVisible = false });
                // flechas de mirada hacia -u (en la seccion B-B v crece hacia la derecha)
                Arrow(x, y1 + 6, x - 16, y1 + 6, brush);
                Arrow(x, y2 - 6, x - 16, y2 - 6, brush);
                Text(letter, x - 4, y1 - 19, brush, 13, true);
                Text(letter, x - 4, y2 + 3, brush, 13, true);
                // rotulo en la banda inferior, a la derecha de la letra (la cota u queda entre el bloque y la letra)
                Text(letter + "-" + letter + "  u = " + M(_state.CutB), x + 10, y2 + 5, brush, 9);
            }
        }

        private void HDim(double x1, double x2, double y, Brush b)
        {
            Children.Add(new Line { X1 = x1, Y1 = y, X2 = x2, Y2 = y, Stroke = b, StrokeThickness = 0.8, IsHitTestVisible = false });
            foreach (double x in new[] { x1, x2 })
            {
                Children.Add(new Line { X1 = x, Y1 = y - 4, X2 = x, Y2 = y + 4, Stroke = b, StrokeThickness = 0.8, IsHitTestVisible = false });
                Children.Add(new Line { X1 = x - 3, Y1 = y + 3, X2 = x + 3, Y2 = y - 3, Stroke = b, StrokeThickness = 0.8, IsHitTestVisible = false });
            }
        }

        private void VDim(double x, double y1, double y2, Brush b)
        {
            Children.Add(new Line { X1 = x, Y1 = y1, X2 = x, Y2 = y2, Stroke = b, StrokeThickness = 0.8, IsHitTestVisible = false });
            foreach (double y in new[] { y1, y2 })
            {
                Children.Add(new Line { X1 = x - 4, Y1 = y, X2 = x + 4, Y2 = y, Stroke = b, StrokeThickness = 0.8, IsHitTestVisible = false });
                Children.Add(new Line { X1 = x - 3, Y1 = y + 3, X2 = x + 3, Y2 = y - 3, Stroke = b, StrokeThickness = 0.8, IsHitTestVisible = false });
            }
        }

        private static Geometry RingsGeometry(IEnumerable<List<Pt>> rings, Func<double, double> X, Func<double, double> Y)
        {
            var geo = new PathGeometry { FillRule = FillRule.EvenOdd };
            foreach (List<Pt> ring in rings)
            {
                if (ring == null || ring.Count < 3) continue;
                var fig = new PathFigure { StartPoint = new Point(X(ring[0].U), Y(ring[0].V)), IsClosed = true, IsFilled = true };
                for (int i = 1; i < ring.Count; i++) fig.Segments.Add(new LineSegment(new Point(X(ring[i].U), Y(ring[i].V)), true));
                geo.Figures.Add(fig);
            }
            geo.Freeze();
            return geo;
        }

        private void Arrow(double x1, double y1, double x2, double y2, Brush brush)
        {
            Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = brush, StrokeThickness = 1.2, IsHitTestVisible = false });
            Vector d = new Point(x2, y2) - new Point(x1, y1);
            if (d.Length < 1e-6) return;
            d.Normalize();
            var n = new Vector(-d.Y, d.X);
            Point tip = new Point(x2, y2), b1 = tip - d * 7 + n * 3.5, b2 = tip - d * 7 - n * 3.5;
            Children.Add(new Polygon { Points = new PointCollection { tip, b1, b2 }, Fill = brush, IsHitTestVisible = false });
        }

        private TextBlock Text(string s, double x, double y, Brush brush, double size, bool bold = false)
        {
            var t = new TextBlock { Text = s, Foreground = brush, FontSize = size, TextWrapping = TextWrapping.NoWrap, IsHitTestVisible = false };
            if (bold) t.FontWeight = FontWeights.SemiBold;
            SetLeft(t, x); SetTop(t, y);
            Children.Add(t);
            return t;
        }
    }
}
