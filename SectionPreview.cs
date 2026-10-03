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
    /// <summary>
    /// Una seccion de la lamina (A-A o B-B) dibujada a partir de un SectionCut: titulo con la
    /// escala y la posicion del corte, terreno, perfil del hormigon (murete / foso / nucleo),
    /// barras cortadas como circulos a su diametro, barras contenidas en el plano como
    /// polilineas con sus patas, una etiqueta por familia y lado con la notacion del plano,
    /// cotas (tramos, ancho total, canto, profundidad de foso, espesor de base), niveles a la
    /// derecha con su elevacion y, a trazos, los recubrimientos. Rueda: zoom; arrastrar:
    /// mover; doble clic: encajar. Pasar el raton por una barra la resalta en las tres vistas.
    /// </summary>
    public sealed class SectionPreview : Canvas
    {
        private readonly PreviewState _state;
        private SectionCut _cut;
        private BlockPlan _plan;
        private string _levelRef = "";
        private string _message = "Sin elemento armable";

        private double _zoom = 1;
        private Vector _pan;
        private double _x0, _y0;
        private Point _dragStart;
        private Vector _panStart;
        private bool _dragging;
        private readonly List<(PlannedBar bar, Point a, Point b, double r)> _hits = new List<(PlannedBar, Point, Point, double)>();

        private const double FtToMm = 304.8;
        private const double MarginTop = 44, MarginBottom = 74;
        private static readonly int[] Scales = { 5, 10, 15, 20, 25, 30, 40, 50, 75, 100, 125, 150, 200, 250, 300, 500 };

        public SectionPreview(PreviewState state)
        {
            _state = state;
            Background = RevitTheme.Paper;
            ClipToBounds = true;
            SizeChanged += (s, e) => Redraw();
            MouseWheel += OnWheel;
            MouseLeftButtonDown += OnDown;
            MouseMove += OnMove;
            MouseLeftButtonUp += OnUp;
            MouseLeave += (s, e) => { _dragging = false; ReleaseMouseCapture(); _state.Hover = null; };
            Cursor = Cursors.Hand;
        }

        /// <param name="newElement">True al cambiar de bloque: se vuelve a encajar la vista.</param>
        public void Show(SectionCut cut, BlockPlan plan, string levelReferenceName, bool newElement)
        {
            _cut = cut; _plan = plan; _levelRef = levelReferenceName ?? "";
            if (newElement) ResetView(); else Redraw();
        }

        public void Clear(string message)
        {
            _cut = null; _plan = null; _message = message;
            Redraw();
        }

        public void ResetView()
        {
            _zoom = 1; _pan = new Vector(0, 0);
            Redraw();
        }

        public void Refresh() => Redraw();

        // --- raton ---
        private void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (_cut == null) return;
            double factor = e.Delta > 0 ? 1.25 : 1 / 1.25;
            double newZoom = Math.Max(0.2, Math.Min(60, _zoom * factor));
            factor = newZoom / _zoom;
            Point m = e.GetPosition(this);
            _pan = new Vector(m.X - _x0 - (m.X - _pan.X - _x0) * factor, m.Y - _y0 - (m.Y - _pan.Y - _y0) * factor);
            _zoom = newZoom;
            Redraw();
            e.Handled = true;
        }

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            if (_cut == null) return;
            if (e.ClickCount == 2) { ResetView(); e.Handled = true; return; }
            _dragging = true; _dragStart = e.GetPosition(this); _panStart = _pan;
            CaptureMouse();
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (_cut == null) return;
            Point m = e.GetPosition(this);
            if (_dragging)
            {
                _pan = _panStart + (m - _dragStart);
                Redraw();
                return;
            }
            PlannedBar best = null; double bestD = 5;
            foreach ((PlannedBar bar, Point a, Point b, double r) in _hits)
            {
                double d = PlanPreview.DistToSegment(m, a, b) - r;
                if (d < bestD) { bestD = d; best = bar; }
            }
            _state.Hover = best;
        }

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            _dragging = false;
            ReleaseMouseCapture();
        }

        // --- dibujo ---
        private static string Mm(double ft) => Math.Round(ft * FtToMm).ToString(CultureInfo.InvariantCulture);
        private static string M(double ft) => (ft * 0.3048).ToString("0.00", CultureInfo.InvariantCulture) + " m";
        private static string Elev(double ft) => (ft * 0.3048).ToString("0.000", CultureInfo.InvariantCulture);

        private void Redraw()
        {
            Children.Clear();
            _hits.Clear();
            double W = ActualWidth, H = ActualHeight;
            if (W < 10 || H < 10) return;
            if (_cut == null)
            {
                Text(_message, 10, 10, Brushes.Gray, 12);
                return;
            }
            SectionCut cut = _cut;
            double w = Math.Max(cut.SMax - cut.SMin, 1e-6), hT = Math.Max(cut.ZTop, 1e-6);
            // margenes laterales segun el ancho real de los textos que van a cada lado (etiquetas a la izquierda; niveles y etiquetas "der" a la derecha),
            // asi el encaje (y el doble clic) incluye etiquetas, niveles y cotas, no solo el hormigon
            double leftText = 0, rightText = 0;
            if (_state.ShowLabels && _plan != null && _plan.Error == null)
                foreach (SectionLabel lb in cut.Labels.Where(l => _state.Visible(l.Family)))
                {
                    double wt = TextWidth(Families.Code(lb.Family) + " " + lb.Text, 10, false);
                    if (lb.Side == "der") rightText = Math.Max(rightText, wt); else leftText = Math.Max(leftText, wt);
                }
            foreach (SectionLevel lv in cut.Levels) rightText = Math.Max(rightText, TextWidth("▽ " + Elev(lv.Elevation) + "  " + lv.Name, 10, true));
            double MarginLeft = Math.Max(70, leftText + 44), MarginRight = Math.Max(60, rightText + 22);
            double aw = Math.Max(W - MarginLeft - MarginRight, 40), ah = Math.Max(H - MarginTop - MarginBottom, 40);
            double k = Math.Min(aw / w, ah / hT) * _zoom;
            // origen sin zoom: la seccion centrada en el area de dibujo, cara inferior abajo
            _x0 = MarginLeft + 0.5 * (aw - w * k / _zoom);
            _y0 = MarginTop + 0.5 * (ah - hT * k / _zoom) + hT * k / _zoom;
            double x0 = _x0 + _pan.X, y0 = _y0 + _pan.Y;
            Func<double, double> X = s => x0 + (s - cut.SMin) * k;
            Func<double, double> Y = z => y0 - z * k;

            // titulo y escala (1152 px por pie a escala 1:1 con 96 ppp)
            double n = 1152.0 / Math.Max(k, 1e-9);
            int scale = Scales.OrderBy(s => Math.Abs(Math.Log(s / n))).First();
            string pos = cut.Line.AlongU ? "v = " : "u = ";
            Text(cut.Line.Title + "   esc. " + (Math.Abs(scale - n) / n > 0.08 ? "≈" : "") + "1:" + scale + "   (" + pos + M(cut.Line.Coord) + ")", 8, 5, Brushes.Black, 13, true);
            Text("niveles: " + _levelRef, 8, 25, PlanColors.Dim, 9);

            // terreno bajo la cara inferior
            Children.Add(new Line { X1 = 0, Y1 = Y(0), X2 = W, Y2 = Y(0), Stroke = PlanColors.Soil, StrokeThickness = 2, IsHitTestVisible = false });
            for (double x = 0; x < W; x += 14)
                Children.Add(new Line { X1 = x, Y1 = Y(0) + 2, X2 = x - 6, Y2 = Y(0) + 8, Stroke = PlanColors.Soil, StrokeThickness = 1, IsHitTestVisible = false });

            // perfil del hormigon
            Children.Add(new Path { Data = ProfileGeometry(cut, X, Y), Fill = PlanColors.Concrete, Stroke = PlanColors.Edge, StrokeThickness = 1.3, StrokeLineJoin = PenLineJoin.Miter });
            foreach (SectionInterval iv in cut.Profile.Where(i => i.Kind == "foso" && i.ZTop != null))
            {
                // fondo de foso marcado
                Children.Add(new Line { X1 = X(iv.S0), Y1 = Y(iv.ZTop.Value), X2 = X(iv.S1), Y2 = Y(iv.ZTop.Value), Stroke = PlanColors.RecessEdge, StrokeThickness = 1.3, IsHitTestVisible = false });
            }

            // recubrimientos a trazos
            if (_state.ShowCovers && _plan != null) Covers(cut, X, Y);

            // rejillas (al ras del tope del foso) y angulos de borde
            if (_state.ShowGrids)
                foreach (SectionGrid sg in cut.Grids)
                {
                    double x1 = X(sg.S0), x2 = X(sg.S1), y1 = Y(sg.Z1), y2 = Y(sg.Z0);
                    var rect = new Rectangle { Width = Math.Max(1, x2 - x1), Height = Math.Max(1.5, y2 - y1), Fill = PlanColors.GridFill, Stroke = PlanColors.GridEdge, StrokeThickness = 0.9,
                                               ToolTip = "rejilla " + sg.Group + (sg.Lengthwise ? " (vista a lo largo)" : " (cortada)") + ": " + Mm(sg.Piece.Length) + " x " + Mm(sg.Piece.Width) + " mm, alto " + Mm(sg.Z1 - sg.Z0) };
                    SetLeft(rect, x1); SetTop(rect, y1);
                    Children.Add(rect);
                    if (!sg.Lengthwise)
                    {
                        double step = BlockPlan.Mm(30) * k;
                        if (step >= 3) for (double x = x1 + step; x < x2; x += step) Children.Add(new Line { X1 = x, Y1 = y1, X2 = x, Y2 = y2, Stroke = PlanColors.GridEdge, StrokeThickness = 0.5, IsHitTestVisible = false });
                    }
                    if (_state.ShowLabels) Text(sg.Group, 0.5 * (x1 + x2) - 7, y1 - 13, PlanColors.GridEdge, 9, true);
                }
            if (_state.ShowAngles)
                foreach (SectionAngle sa in cut.Angles)
                {
                    double th = Math.Max(1.5, BlockPlan.Mm(6.4) * k);
                    if (sa.Crossing)
                    {
                        // talon en la cara del foso a la cota de apoyo: ala vertical hacia abajo contra la pared, ala horizontal hacia el foso
                        double tHalf = 0.5 * th;
                        double x = X(sa.S) + sa.Toward * tHalf, yH = Y(sa.ZHeel) + tHalf, yB = Y(sa.ZHeel - sa.Leg), xL = X(sa.S + sa.Toward * sa.Leg);
                        var pl = new Polyline { Stroke = PlanColors.Angle, StrokeThickness = th, StrokeLineJoin = PenLineJoin.Miter, ToolTip = sa.Angle.Describe() + "; talon a " + Mm(sa.ZTop - sa.ZHeel) + " mm bajo el tope" };
                        pl.Points.Add(new Point(x, yB)); pl.Points.Add(new Point(x, yH)); pl.Points.Add(new Point(xL, yH));
                        Children.Add(pl);
                    }
                    else
                    {
                        var rect = new Rectangle { Width = Math.Max(1, X(sa.S1) - X(sa.S0)), Height = Math.Max(1.5, sa.Leg * k), Fill = Brushes.Transparent, Stroke = PlanColors.Angle, StrokeThickness = 1, ToolTip = sa.Angle.Describe() + " (visto a lo largo)" };
                        SetLeft(rect, X(sa.S0)); SetTop(rect, Y(sa.ZHeel));
                        Children.Add(rect);
                    }
                }

            // barras: primero las contenidas en el plano, encima las cortadas
            if (_plan != null && _plan.Error == null)
            {
                foreach (SectionPolyline p in cut.Polylines.Where(p => _state.Visible(p.Family)))
                {
                    bool hl = _state.IsHighlighted(p.Bar, _plan);
                    double th = hl ? Math.Max(3, p.D * k) : Math.Max(1.2, p.D * k);
                    var pl = new Polyline
                    {
                        Stroke = hl ? PlanColors.Highlight : PlanColors.Of(p.Family), StrokeThickness = th,
                        StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Flat, StrokeEndLineCap = PenLineCap.Flat
                    };
                    for (int i = 0; i < p.Points.Count; i++)
                    {
                        var pt = new Point(X(p.Points[i].U), Y(p.Points[i].V));
                        pl.Points.Add(pt);
                        if (i > 0) _hits.Add((p.Bar, new Point(X(p.Points[i - 1].U), Y(p.Points[i - 1].V)), pt, 0.5 * th));
                    }
                    Children.Add(pl);
                }
                foreach (SectionCircle c in cut.Circles.Where(c => _state.Visible(c.Family)))
                {
                    bool hl = _state.IsHighlighted(c.Bar, _plan);
                    double r = Math.Max(2.2, 0.5 * c.D * k);
                    var e = new Ellipse
                    {
                        Width = 2 * r, Height = 2 * r,
                        Fill = hl ? PlanColors.Highlight : PlanColors.Of(c.Family), Stroke = Brushes.Black, StrokeThickness = hl ? 1.2 : 0.5
                    };
                    var ctr = new Point(X(c.S), Y(c.Z));
                    SetLeft(e, ctr.X - r); SetTop(e, ctr.Y - r);
                    Children.Add(e);
                    _hits.Add((c.Bar, ctr, ctr, r));
                }
            }

            // etiquetas (una por familia y lado) y niveles, en dos columnas con sus lineas de referencia
            var left = new List<(double y, string text, Brush brush, Point anchor, bool bold)>();
            var right = new List<(double y, string text, Brush brush, Point anchor, bool bold)>();
            if (_state.ShowLabels && _plan != null && _plan.Error == null)
                foreach (SectionLabel lb in cut.Labels.Where(l => _state.Visible(l.Family)))
                {
                    var anchor = new Point(X(lb.Anchor.U), Y(lb.Anchor.V));
                    string text = Families.Code(lb.Family) + " " + lb.Text;
                    if (lb.Side == "der") right.Add((anchor.Y, text, PlanColors.Of(lb.Family), anchor, false));
                    else left.Add((anchor.Y, text, PlanColors.Of(lb.Family), anchor, false));
                }
            foreach (SectionLevel lv in cut.Levels)
                right.Add((Y(lv.Z), "▽ " + Elev(lv.Elevation) + "  " + lv.Name, PlanColors.Level, new Point(X(cut.SMax), Y(lv.Z)), true));
            Column(left, X(cut.SMin) - 34, false);
            Column(right, X(cut.SMax) + 14, true);

            // cotas
            if (_state.ShowDims) Dims(cut, X, Y, k);

            // avisos y estado
            double yWarn = H - 16;
            foreach (string wtext in cut.Warnings.Take(2)) { Text(wtext, 8, yWarn, Brushes.Firebrick, 9); yWarn -= 13; }
            if (_plan != null && _plan.Error != null) Text(_plan.Error, 8, MarginTop, Brushes.Firebrick, 11);
            if (_state.Hover != null) Text(PlanPreview.HoverText(_state.Hover, _plan), 8, H - 30 - (cut.Warnings.Count > 0 ? 13 * Math.Min(2, cut.Warnings.Count) : 0), Brushes.Black, 10, true);
        }

        /// <summary>Columna de textos (etiquetas o niveles) sin solapes, cada uno con su linea de referencia al anclaje.</summary>
        private void Column(List<(double y, string text, Brush brush, Point anchor, bool bold)> items, double x, bool leftAligned)
        {
            if (items.Count == 0) return;
            const double step = 13;
            var sorted = items.OrderBy(i => i.y).ToList();
            var ys = new double[sorted.Count];
            for (int i = 0; i < sorted.Count; i++) ys[i] = i == 0 ? sorted[i].y : Math.Max(sorted[i].y, ys[i - 1] + step);
            // si la pila se ha ido hacia abajo mas de lo necesario, se sube en bloque para centrarla sobre los anclajes
            double shift = 0.5 * ((ys[sorted.Count - 1] - sorted[sorted.Count - 1].y) - 0);
            for (int i = 0; i < sorted.Count; i++)
            {
                double y = ys[i] - shift;
                TextBlock tb = Text(sorted[i].text, x, y - 7, sorted[i].brush, 10, sorted[i].bold);
                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double wText = tb.DesiredSize.Width;
                double xEdge = leftAligned ? x - 2 : x + 2;
                if (!leftAligned) SetLeft(tb, x - wText);
                Children.Add(new Line
                {
                    X1 = xEdge, Y1 = y, X2 = sorted[i].anchor.X, Y2 = sorted[i].anchor.Y,
                    Stroke = sorted[i].brush, StrokeThickness = 0.7, Opacity = 0.8, IsHitTestVisible = false
                });
            }
        }

        private void Covers(SectionCut cut, Func<double, double> X, Func<double, double> Y)
        {
            var dash = new DoubleCollection { 4, 3 };
            BlockPlan p = _plan;
            void Dashed(double x1, double y1, double x2, double y2) =>
                Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = PlanColors.Cover, StrokeThickness = 0.8, StrokeDashArray = dash, IsHitTestVisible = false });
            var prof = cut.Profile;
            for (int i = 0; i < prof.Count; i++)
            {
                SectionInterval iv = prof[i];
                if (iv.ZTop == null) continue;
                double zt = iv.ZTop.Value;
                Dashed(X(iv.S0), Y(p.Cb), X(iv.S1), Y(p.Cb));                       // recubrimiento inferior
                if (iv.Kind != "foso") Dashed(X(iv.S0), Y(zt - p.Ct), X(iv.S1), Y(zt - p.Ct));   // superior
                // caras verticales: exteriores (borde o muro) y de foso
                SectionInterval prev = i > 0 ? prof[i - 1] : null, next = i + 1 < prof.Count ? prof[i + 1] : null;
                if (iv.Kind != "foso")
                {
                    bool extStart = prev == null || prev.ZTop == null, extEnd = next == null || next.ZTop == null;
                    double cs = iv.Kind == "murete" ? p.Cw : p.Ce;
                    if (extStart) Dashed(X(iv.S0 + cs), Y(0), X(iv.S0 + cs), Y(zt));
                    if (extEnd) Dashed(X(iv.S1 - cs), Y(0), X(iv.S1 - cs), Y(zt));
                    if (prev != null && prev.Kind == "foso" && prev.ZTop != null) Dashed(X(iv.S0 + p.Cw), Y(prev.ZTop.Value), X(iv.S0 + p.Cw), Y(zt));
                    if (next != null && next.Kind == "foso" && next.ZTop != null) Dashed(X(iv.S1 - p.Cw), Y(next.ZTop.Value), X(iv.S1 - p.Cw), Y(zt));
                }
            }
        }

        private void Dims(SectionCut cut, Func<double, double> X, Func<double, double> Y, double k)
        {
            Brush b = PlanColors.Dim;
            double yChain = Y(0) + 22, yTotal = Y(0) + 42;
            // cadena de tramos (murete / foso / nucleo / hueco) bajo la cara inferior
            foreach (SectionInterval iv in cut.Profile)
            {
                double x1 = X(iv.S0), x2 = X(iv.S1);
                HDim(x1, x2, yChain, Mm(iv.Length), b, 9, iv.Kind);
            }
            // ancho total
            HDim(X(cut.SMin), X(cut.SMax), yTotal, Mm(cut.SMax - cut.SMin), b, 10, null);
            // canto total (a la izquierda, texto girado)
            VDim(X(cut.SMin) - 16, Y(0), Y(cut.ZTop), Mm(cut.ZTop), b);
            // profundidad del foso y espesor de la base, dentro del foso mas profundo
            SectionInterval deepest = cut.Profile.Where(i => i.Kind == "foso" && i.ZTop != null).OrderBy(i => i.ZTop.Value).FirstOrDefault();
            if (deepest != null)
            {
                double xm = X(0.5 * (deepest.S0 + deepest.S1));
                double zf = deepest.ZTop.Value;
                VDim(xm, Y(zf), Y(cut.ZTop), Mm(cut.ZTop - zf), b);
                VDim(xm, Y(0), Y(zf), Mm(zf), b);
            }
        }

        private void HDim(double x1, double x2, double y, string text, Brush b, double size, string kind)
        {
            if (x2 - x1 < 2) return;
            Children.Add(new Line { X1 = x1, Y1 = y, X2 = x2, Y2 = y, Stroke = b, StrokeThickness = 0.8, IsHitTestVisible = false });
            foreach (double x in new[] { x1, x2 })
            {
                Children.Add(new Line { X1 = x, Y1 = y - 4, X2 = x, Y2 = y + 4, Stroke = b, StrokeThickness = 0.8, IsHitTestVisible = false });
                Children.Add(new Line { X1 = x - 3, Y1 = y + 3, X2 = x + 3, Y2 = y - 3, Stroke = b, StrokeThickness = 0.8, IsHitTestVisible = false });
            }
            if (x2 - x1 < 26) return;
            string t = kind == "hueco" ? text + " (hueco)" : text;
            TextBlock tb = Text(t, 0, y - size - 3, b, size);
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            SetLeft(tb, 0.5 * (x1 + x2) - 0.5 * tb.DesiredSize.Width);
        }

        private void VDim(double x, double y1, double y2, string text, Brush b)
        {
            double top = Math.Min(y1, y2), bot = Math.Max(y1, y2);
            if (bot - top < 2) return;
            Children.Add(new Line { X1 = x, Y1 = top, X2 = x, Y2 = bot, Stroke = b, StrokeThickness = 0.8, IsHitTestVisible = false });
            foreach (double y in new[] { top, bot })
            {
                Children.Add(new Line { X1 = x - 4, Y1 = y, X2 = x + 4, Y2 = y, Stroke = b, StrokeThickness = 0.8, IsHitTestVisible = false });
                Children.Add(new Line { X1 = x - 3, Y1 = y + 3, X2 = x + 3, Y2 = y - 3, Stroke = b, StrokeThickness = 0.8, IsHitTestVisible = false });
            }
            if (bot - top < 18) return;
            var tb = new TextBlock { Text = text, Foreground = b, FontSize = 9, IsHitTestVisible = false, LayoutTransform = new RotateTransform(-90) };
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            SetLeft(tb, x - tb.DesiredSize.Width - 2);
            SetTop(tb, 0.5 * (top + bot) - 0.5 * tb.DesiredSize.Height);
            Children.Add(tb);
        }

        /// <summary>Poligono del perfil: cara inferior plana y cota superior por tramos; los huecos bajan a cero.</summary>
        private static Geometry ProfileGeometry(SectionCut cut, Func<double, double> X, Func<double, double> Y)
        {
            var geo = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(X(cut.SMin), Y(0)), IsClosed = true, IsFilled = true };
            double prev = 0;
            foreach (SectionInterval iv in cut.Profile)
            {
                double z = iv.ZTop ?? 0;
                if (Math.Abs(z - prev) > 1e-9) fig.Segments.Add(new LineSegment(new Point(X(iv.S0), Y(prev)), true));
                fig.Segments.Add(new LineSegment(new Point(X(iv.S0), Y(z)), true));
                fig.Segments.Add(new LineSegment(new Point(X(iv.S1), Y(z)), true));
                prev = z;
            }
            fig.Segments.Add(new LineSegment(new Point(X(cut.SMax), Y(0)), true));
            geo.Figures.Add(fig);
            geo.Freeze();
            return geo;
        }

        private TextBlock Text(string s, double x, double y, Brush brush, double size, bool bold = false)
        {
            var t = new TextBlock { Text = s, Foreground = brush, FontSize = size, TextWrapping = TextWrapping.NoWrap, IsHitTestVisible = false };
            if (bold) t.FontWeight = FontWeights.SemiBold;
            SetLeft(t, x); SetTop(t, y);
            Children.Add(t);
            return t;
        }

        private double TextWidth(string s, double size, bool bold)
        {
            var t = new TextBlock { Text = s, FontSize = size };
            if (bold) t.FontWeight = FontWeights.SemiBold;
            t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return t.DesiredSize.Width;
        }
    }
}
