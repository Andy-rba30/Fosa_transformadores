using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BlockRebar
{
    /// <summary>Linea de corte en planta: A-A a lo largo de u (plano u-z, en v = Coord) o B-B a lo largo de v (plano v-z, en u = Coord).</summary>
    public sealed class SectionLine
    {
        public bool AlongU;
        public double Coord;
        public SectionLine() { }
        public SectionLine(bool alongU, double coord) { AlongU = alongU; Coord = coord; }
        public string Letter => AlongU ? "A" : "B";
        public string Title => "SECCIÓN " + Letter + "-" + Letter;
        /// <summary>Coordenada de un punto a lo largo del corte (s) y perpendicular a el (c).</summary>
        public double S(P3 p) => AlongU ? p.U : p.V;
        public double C(P3 p) => AlongU ? p.V : p.U;
        public Pt Plan(double s) => AlongU ? new Pt(s, Coord) : new Pt(Coord, s);
        public P3 Normal => AlongU ? P3.AxisV : P3.AxisU;
    }

    /// <summary>Barra cortada por el plano: un circulo a su diametro.</summary>
    public sealed class SectionCircle
    {
        public PlannedBar Bar;
        public BarGroup Group;
        public double S, Z, D;
        public Family Family => Bar.Family;
    }

    /// <summary>Barra contenida en el plano: su polilinea (s, z) con patas y doblados.</summary>
    public sealed class SectionPolyline
    {
        public PlannedBar Bar;
        public BarGroup Group;
        public List<Pt> Points = new List<Pt>();
        public double D;
        public Family Family => Bar.Family;
        /// <summary>Distancia de la barra al plano del corte (pies).</summary>
        public double Offset;
    }

    /// <summary>Tramo del perfil del hormigon a lo largo del corte.</summary>
    public sealed class SectionInterval
    {
        public double S0, S1;
        /// <summary>Cota del hormigon (desde la cara inferior); null = no hay hormigon (hueco).</summary>
        public double? ZTop;
        /// <summary>"nucleo", "murete", "foso" o "hueco".</summary>
        public string Kind;
        public double Length => S1 - S0;
    }

    public sealed class SectionLevel
    {
        public string Name;
        public double Z;
        public double Elevation;
    }

    /// <summary>Una etiqueta por familia y lado, con la notacion del plano.</summary>
    public sealed class SectionLabel
    {
        public Family Family;
        /// <summary>"izq", "der" o "centro".</summary>
        public string Side;
        public string Text;
        public Pt Anchor;
        public double D;
    }

    /// <summary>Pieza de rejilla cortada o vista en la seccion: rectangulo (s0..s1, z0..z1) y su grupo (P1...).</summary>
    public sealed class SectionGrid
    {
        public double S0, S1, Z0, Z1;
        public string Group = "";
        /// <summary>True si el corte va a lo largo de la pieza (se ve su largo); false si la cruza (se ve su ancho).</summary>
        public bool Lengthwise;
        public GridPiece Piece;
    }

    /// <summary>Angulo de borde en la seccion: cortado (una L en s, abriendo hacia el foso) o visto a lo largo (banda s0..s1 bajo el tope).</summary>
    public sealed class SectionAngle
    {
        public bool Crossing;
        public double S, ZTop, Leg;
        /// <summary>Cota del talon (cara superior del ala horizontal = apoyo de la rejilla).</summary>
        public double ZHeel;
        /// <summary>+1 si el foso (y el ala horizontal) queda hacia +s, -1 hacia -s.</summary>
        public int Toward;
        public double S0, S1;
        public AngleBar Angle;
    }

    /// <summary>Resultado de cortar el bloque armado por una linea de corte. Pura; la ventana solo la dibuja.</summary>
    public sealed class SectionCut
    {
        public List<SectionGrid> Grids = new List<SectionGrid>();
        public List<SectionAngle> Angles = new List<SectionAngle>();
        public SectionLine Line;
        public double SMin, SMax, ZTop;
        public double ZBaseElevation;
        public List<SectionInterval> Profile = new List<SectionInterval>();
        public List<SectionCircle> Circles = new List<SectionCircle>();
        public List<SectionPolyline> Polylines = new List<SectionPolyline>();
        public List<SectionLevel> Levels = new List<SectionLevel>();
        public List<SectionLabel> Labels = new List<SectionLabel>();
        public List<string> Warnings = new List<string>();

        public int CirclesOf(Family f) => Circles.Count(c => c.Family == f);
        public int PolylinesOf(Family f) => Polylines.Count(p => p.Family == f);
        public IEnumerable<Family> Families => Circles.Select(c => c.Family).Concat(Polylines.Select(p => p.Family)).Distinct().OrderBy(f => (int)f);
        public double SMid => 0.5 * (SMin + SMax);
        /// <summary>Profundidad del foso mas profundo que cruza el corte (0 si no cruza ninguno).</summary>
        public double RecessDepth => Profile.Where(i => i.Kind == "foso" && i.ZTop != null).Select(i => ZTop - i.ZTop.Value).DefaultIfEmpty(0).Max();
        /// <summary>Espesor de la base bajo el foso mas profundo del corte.</summary>
        public double BaseThickness => Profile.Where(i => i.Kind == "foso" && i.ZTop != null).Select(i => i.ZTop.Value).DefaultIfEmpty(ZTop).Min();

        public string ProfileText() => string.Join(" / ", Profile.Select(i => i.Kind + " " + BlockPlan.ToMm(i.Length)));

        public string Describe()
        {
            string pos = Line.AlongU ? "v=" : "u=";
            return Line.Letter + "-" + Line.Letter + " a " + pos + BlockTopology.M(Line.Coord) + " m: perfil " + ProfileText() +
                   "; circulos " + string.Join(", ", BlockRebar.Families.All.Where(f => CirclesOf(f) > 0).Select(f => f + " " + CirclesOf(f))) +
                   "; lineas " + string.Join(", ", BlockRebar.Families.All.Where(f => PolylinesOf(f) > 0).Select(f => f + " " + PolylinesOf(f)));
        }
    }

    /// <summary>
    /// Seccion pura del bloque armado: perfil del hormigon en el corte (topologico, o
    /// muestreado del solido si se pasa), barras cortadas como circulos, barras contenidas en
    /// el plano (la mas cercana al corte de cada conjunto, a menos de media separacion) como
    /// polilineas, niveles con su elevacion y una etiqueta por familia y lado.
    /// </summary>
    public static class BlockSection
    {
        /// <param name="tol">Tolerancia (pies) para considerar una barra suelta dentro del plano.</param>
        /// <param name="zBaseElevation">Elevacion de la cara inferior en la referencia elegida (pies), para los niveles.</param>
        /// <param name="sampledTop">Opcional: cota del hormigon muestreada del solido real en s (desde la cara inferior; null = no hay hormigon).</param>
        public static SectionCut Cut(BlockPlan plan, BlockTopology topo, SectionLine line, double tol, double zBaseElevation = 0, Func<double, double?> sampledTop = null)
        {
            var cut = new SectionCut { Line = line, ZTop = topo.ZTop, ZBaseElevation = zBaseElevation };
            cut.SMin = line.AlongU ? topo.UMin : topo.VMin;
            cut.SMax = line.AlongU ? topo.UMax : topo.VMax;
            Profile(cut, topo, line, tol, sampledTop);
            Levels(cut, topo);
            if (plan != null && plan.Error == null) Bars(cut, plan, line, tol);
            Labels(cut, plan);
            return cut;
        }

        // -----------------------------------------------------------------
        // Perfil del hormigon
        // -----------------------------------------------------------------
        private static void Profile(SectionCut cut, BlockTopology topo, SectionLine line, double tol, Func<double, double?> sampled)
        {
            var breaks = new List<double> { cut.SMin, cut.SMax };
            IEnumerable<List<Pt>> rings = topo.Body.SelectMany(b => b.Rings())
                .Concat(topo.Regions.SelectMany(r => r.Shape.Rings()))
                .Concat(topo.Recesses.SelectMany(r => r.Shape.Rings()));
            foreach (List<Pt> ring in rings)
                for (int i = 0; i < ring.Count; i++)
                {
                    Pt a = ring[i], b = ring[(i + 1) % ring.Count];
                    double ca = line.AlongU ? a.V : a.U, cb = line.AlongU ? b.V : b.U;
                    if ((ca <= line.Coord) != (cb <= line.Coord))
                    {
                        double t = (line.Coord - ca) / (cb - ca);
                        breaks.Add(line.AlongU ? a.U + t * (b.U - a.U) : a.V + t * (b.V - a.V));
                    }
                }
            breaks.Sort();
            var s = new List<double>();
            foreach (double v in breaks) if (s.Count == 0 || v - s[s.Count - 1] > tol) s.Add(v);
            for (int i = 0; i + 1 < s.Count; i++)
            {
                double mid = 0.5 * (s[i] + s[i + 1]);
                Pt p = line.Plan(mid);
                double? z = topo.TopAt(p);
                string kind = topo.KindAt(p);
                if (sampled != null)
                {
                    double? zs = null;
                    try { zs = sampled(mid); } catch { }
                    bool differs = (zs == null) != (z == null) || (zs != null && z != null && Math.Abs(zs.Value - z.Value) > 3 * tol);
                    if (differs)
                    {
                        cut.Warnings.Add("el perfil muestreado del solido difiere del topologico en s=" + BlockPlan.ToMm(mid) + " mm (" +
                                         (zs == null ? "sin hormigon" : BlockPlan.ToMm(zs.Value) + " mm") + " frente a " + (z == null ? "sin hormigon" : BlockPlan.ToMm(z.Value) + " mm") + ")");
                        z = zs;
                        if (z == null) kind = "hueco";
                    }
                }
                SectionInterval last = cut.Profile.Count > 0 ? cut.Profile[cut.Profile.Count - 1] : null;
                if (last != null && last.Kind == kind && ((last.ZTop == null && z == null) || (last.ZTop != null && z != null && Math.Abs(last.ZTop.Value - z.Value) <= tol)))
                    last.S1 = s[i + 1];
                else
                    cut.Profile.Add(new SectionInterval { S0 = s[i], S1 = s[i + 1], ZTop = z, Kind = kind });
            }
        }

        private static void Levels(SectionCut cut, BlockTopology topo)
        {
            cut.Levels.Add(new SectionLevel { Name = "tope", Z = topo.ZTop, Elevation = topo.ZTop + cut.ZBaseElevation });
            foreach (double z in cut.Profile.Where(i => i.Kind == "foso" && i.ZTop != null).Select(i => i.ZTop.Value).Distinct().OrderByDescending(z => z))
            {
                int idx = topo.Recesses.FindIndex(r => Math.Abs(r.ZFloor - z) <= topo.Tol);
                cut.Levels.Add(new SectionLevel { Name = "fondo de foso" + (topo.Recesses.Count > 1 && idx >= 0 ? " " + (idx + 1) : ""), Z = z, Elevation = z + cut.ZBaseElevation });
            }
            cut.Levels.Add(new SectionLevel { Name = "cara inferior", Z = 0, Elevation = cut.ZBaseElevation });
        }

        // -----------------------------------------------------------------
        // Barras
        // -----------------------------------------------------------------
        private static void Bars(SectionCut cut, BlockPlan plan, SectionLine line, double tol)
        {
            IEnumerable<BarGroup> groups = plan.Groups.Count > 0
                ? plan.Groups
                : plan.Bars.Select(b => new BarGroup { Bars = new List<PlannedBar> { b } });
            P3 n = line.Normal;
            foreach (BarGroup g in groups)
            {
                // barras en planos paralelos al corte, repartidas perpendicularmente a el: se dibuja la mas cercana
                bool parallelArray = Math.Abs(g.Normal.Dot(n)) > 0.99 && g.Count > 1;
                double tolIn = parallelArray ? 0.5 * g.Spacing + tol : tol;
                PlannedBar nearest = null; double nearestDist = double.MaxValue;
                foreach (PlannedBar b in g.Bars)
                {
                    double dist = b.Points.Max(p => Math.Abs(line.C(p) - line.Coord));
                    if (dist < nearestDist) { nearestDist = dist; nearest = b; }
                }
                if (nearest != null && nearestDist <= tolIn)
                    cut.Polylines.Add(new SectionPolyline
                    {
                        Bar = nearest, Group = g, D = nearest.D, Offset = nearestDist,
                        Points = nearest.Points.Select(p => new Pt(line.S(p), p.Z)).ToList()
                    });
                foreach (PlannedBar b in g.Bars)
                {
                    if (ReferenceEquals(b, nearest) && nearestDist <= tolIn) continue;
                    for (int i = 0; i + 1 < b.Points.Count; i++)
                    {
                        P3 p = b.Points[i], q = b.Points[i + 1];
                        double a = line.C(p) - line.Coord, c = line.C(q) - line.Coord;
                        if (Math.Abs(a) <= tol && Math.Abs(c) <= tol) continue;   // tramo en el plano (no es un corte)
                        if ((a <= 0) == (c <= 0)) continue;
                        double k = a / (a - c);
                        P3 x = p + (q - p) * k;
                        cut.Circles.Add(new SectionCircle { Bar = b, Group = g, S = line.S(x), Z = x.Z, D = b.D });
                    }
                }
            }
        }

        // -----------------------------------------------------------------
        // Etiquetas: una por familia y lado
        // -----------------------------------------------------------------
        private static void Labels(SectionCut cut, BlockPlan plan)
        {
            if (plan == null) return;
            double mid = cut.SMid;
            foreach (Family f in cut.Families)
            {
                bool mesh = f == Family.F1 || f == Family.F2 || f == Family.F3;
                var items = new List<(string side, Pt anchor, BarGroup g, double d, double s)>();
                foreach (SectionPolyline p in cut.Polylines.Where(p => p.Family == f))
                {
                    Pt top = p.Points.OrderByDescending(q => q.V).First();
                    items.Add((mesh ? "centro" : (top.U < mid ? "izq" : "der"), top, p.Group, p.D, top.U));
                }
                foreach (SectionCircle c in cut.Circles.Where(c => c.Family == f))
                    items.Add((mesh ? "centro" : (c.S < mid ? "izq" : "der"), new Pt(c.S, c.Z), c.Group, c.D, c.S));
                foreach (var side in items.GroupBy(i => i.side))
                {
                    var first = side.OrderBy(i => Math.Abs(i.s - (side.Key == "centro" ? mid : (side.Key == "izq" ? cut.SMin : cut.SMax)))).First();
                    cut.Labels.Add(new SectionLabel { Family = f, Side = side.Key, Anchor = first.anchor, D = first.d, Text = LabelText(plan, f, first.g, first.d) });
                }
            }
        }

        /// <summary>
        /// Anade al corte las rejillas y angulos del plan de rejillas: piezas cuyo rectangulo
        /// cruza la linea de corte y angulos cortados por el plano (perpendiculares) o vistos a
        /// lo largo (paralelos a menos de un ala del plano).
        /// </summary>
        public static void AddGrids(SectionCut cut, GridPlan g, double legFt)
        {
            cut.Grids.Clear(); cut.Angles.Clear();
            if (g == null || g.Error != null) return;
            SectionLine line = cut.Line;
            double h = BlockPlan.Mm(g.Type?.HeightMm ?? 38);
            foreach (GridPiece p in g.Pieces)
            {
                bool crosses = line.AlongU ? (p.VMin <= line.Coord && line.Coord <= p.VMax) : (p.UMin <= line.Coord && line.Coord <= p.UMax);
                if (!crosses) continue;
                cut.Grids.Add(new SectionGrid
                {
                    S0 = line.AlongU ? p.UMin : p.VMin, S1 = line.AlongU ? p.UMax : p.VMax, Z0 = p.ZTop - h, Z1 = p.ZTop,
                    Group = p.Group, Lengthwise = p.AlongU == line.AlongU, Piece = p
                });
            }
            foreach (AngleBar a in g.Angles)
            {
                bool alongCut = line.AlongU ? Math.Abs(a.B.V - a.A.V) < 1e-9 : Math.Abs(a.B.U - a.A.U) < 1e-9;
                double ca = line.AlongU ? a.A.V : a.A.U, cb = line.AlongU ? a.B.V : a.B.U;
                if (!alongCut)
                {
                    if ((ca <= line.Coord) == (cb <= line.Coord) && Math.Abs(ca - line.Coord) > 1e-9 && Math.Abs(cb - line.Coord) > 1e-9) continue;
                    double sInward = line.AlongU ? a.Inward.U : a.Inward.V;
                    cut.Angles.Add(new SectionAngle { Crossing = true, S = line.AlongU ? a.A.U : a.A.V, ZTop = a.ZTop, ZHeel = a.ZHeel, Leg = legFt, Toward = sInward >= 0 ? 1 : -1, Angle = a });
                }
                else if (Math.Abs(ca - line.Coord) <= legFt)
                {
                    double s0 = line.AlongU ? a.A.U : a.A.V, s1 = line.AlongU ? a.B.U : a.B.V;
                    cut.Angles.Add(new SectionAngle { Crossing = false, S0 = Math.Min(s0, s1), S1 = Math.Max(s0, s1), ZTop = a.ZTop, ZHeel = a.ZHeel, Leg = legFt, Angle = a });
                }
            }
        }

        /// <summary>Notacion del plano: "ø5/8"@125" (conjunto) o "ø5/8" L=1370" (barra suelta).</summary>
        public static string LabelText(BlockPlan plan, Family f, BarGroup g, double d)
        {
            string layer = g != null ? g.Layer : "";
            string type = plan.Diam.Label(f, layer);
            if (string.IsNullOrEmpty(type)) type = BlockPlan.Dia(d) + " mm";
            string head = "ø" + type;
            if (g != null && g.Count > 1 && g.Spacing > 0)
            {
                double sp = g.First.NominalSpacing > 0 ? g.First.NominalSpacing : g.Spacing;
                return head + "@" + BlockPlan.ToMm(sp).ToString(CultureInfo.InvariantCulture);
            }
            if (g != null) return head + " L=" + BlockPlan.ToMm(g.First.Length).ToString(CultureInfo.InvariantCulture);
            return head;
        }
    }
}
