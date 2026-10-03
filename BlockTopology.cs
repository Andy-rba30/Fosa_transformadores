using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BlockRebar
{
    /// <summary>Tipo de una arista (en planta) de una region del tope, de un foso o del cuerpo.</summary>
    public enum EdgeKind
    {
        /// <summary>Da al exterior del bloque (cara vertical exterior): recubrimiento de borde (o de muro en un murete).</summary>
        Exterior,
        /// <summary>Da a un hueco pasante: recubrimiento de borde, sin patas.</summary>
        Hole,
        /// <summary>Da a un foso: es una cara de foso (vertical) que pertenece a la region.</summary>
        Recess,
        /// <summary>Frontera interna entre una plataforma y un murete (no es una cara de hormigon).</summary>
        Internal,
        /// <summary>Extremo libre dentro del hormigon (anclajes, zonas sin clasificar): sin recubrimiento ni patas.</summary>
        Free
    }

    public enum RegionKind { Platform, Wall }

    /// <summary>Arista de un anillo, con el interior de su region a la izquierda (A -> B).</summary>
    public sealed class RegionEdge
    {
        public Pt A, B;
        public EdgeKind Kind = EdgeKind.Free;
        /// <summary>Foso al que da (Kind == Recess), o foso contiguo de una arista de foso.</summary>
        public int RecessIndex = -1;
        /// <summary>Region del tope a la que pertenece la arista (o, en una arista de foso, la region al otro lado).</summary>
        public int RegionIndex = -1;
        /// <summary>0 = anillo exterior, 1... = huecos.</summary>
        public int Ring;
        public int Index;
        public RegionEdge Prev, Next;

        public double Length => A.DistanceTo(B);
        public Pt Dir => Geometry2D.Unit(Geometry2D.Sub(B, A));
        /// <summary>Normal hacia el interior de la region (izquierda de A -> B).</summary>
        public Pt Normal => Geometry2D.Left(Dir);
        public Pt Mid => new Pt(0.5 * (A.U + B.U), 0.5 * (A.V + B.V));
        public Pt At(double t) => Geometry2D.Add(A, Geometry2D.Scale(Dir, t));
        /// <summary>True si la esquina en B (con la arista siguiente) es convexa (giro a la izquierda).</summary>
        public bool ConvexEnd => Next != null && Geometry2D.Cross(Dir, Next.Dir) > 1e-9;
        /// <summary>True si la esquina en A (con la arista anterior) es convexa.</summary>
        public bool ConvexStart => Prev != null && Geometry2D.Cross(Prev.Dir, Dir) > 1e-9;
        /// <summary>Paralela a u (horizontal en planta) o a v.</summary>
        public bool AlongU => Math.Abs(Dir.U) >= Math.Abs(Dir.V);

        public string Describe() =>
            Kind + " L=" + BlockTopology.Mm(Length) + " mm de (" + BlockTopology.Mm(A.U) + ", " + BlockTopology.Mm(A.V) + ") a (" +
            BlockTopology.Mm(B.U) + ", " + BlockTopology.Mm(B.V) + ")" + (RecessIndex >= 0 ? " foso " + (RecessIndex + 1) : "");
    }

    /// <summary>Foso o canaleta abierta por arriba: su fondo (contorno en planta) y su cota.</summary>
    public sealed class Recess
    {
        public int Index;
        public Region2D Shape;
        public Outline2D Outline;
        /// <summary>Cota del fondo desde la cara inferior del bloque (pies).</summary>
        public double ZFloor;
        public double Depth;
        /// <summary>Alguna arista da al exterior (canaleta abierta por un lado).</summary>
        public bool Open;
        public List<RegionEdge> Edges = new List<RegionEdge>();

        public string Describe() =>
            "foso " + (Index + 1) + ": " + BlockTopology.M(Shape.Width) + " x " + BlockTopology.M(Shape.Depth) + " m, fondo a z=" +
            BlockTopology.Mm(ZFloor) + " mm, profundidad " + BlockTopology.Mm(Depth) + " mm" + (Open ? ", abierto por un lado" : "") +
            (Shape.Holes.Count > 0 ? ", en anillo" : "");
    }

    /// <summary>Region del tope (a zTope): plataforma (nucleo) o murete.</summary>
    public sealed class TopRegion
    {
        public int Index;
        /// <summary>Numero dentro de su tipo (plataforma 1, 2... / murete 1, 2...).</summary>
        public int KindIndex;
        public RegionKind Kind;
        public Region2D Shape;
        public Outline2D Outline;
        public double MinWidth;
        public List<RegionEdge> Edges = new List<RegionEdge>();

        public bool IsRing => Shape.Holes.Count > 0;
        public IEnumerable<RegionEdge> EdgesOf(EdgeKind k) => Edges.Where(e => e.Kind == k);
        public string KindName => Kind == RegionKind.Platform ? "plataforma" : "murete";

        public string Describe() =>
            KindName + " " + (KindIndex + 1) + ": " + BlockTopology.M(Shape.Width) + " x " + BlockTopology.M(Shape.Depth) + " m, ancho minimo " +
            BlockTopology.Mm(MinWidth) + " mm" + (IsRing ? ", en anillo" : "") + ", aristas: " +
            string.Join(", ", Edges.GroupBy(e => e.Kind).OrderBy(g => (int)g.Key).Select(g => g.Count() + " " + Name(g.Key)));

        private static string Name(EdgeKind k)
        {
            switch (k)
            {
                case EdgeKind.Exterior: return "exterior(es)";
                case EdgeKind.Hole: return "de hueco";
                case EdgeKind.Recess: return "de foso";
                case EdgeKind.Internal: return "interna(s)";
                default: return "libre(s)";
            }
        }
    }

    /// <summary>
    /// Clasificacion pura (2D, sin Revit) del bloque a partir de los anillos del contorno
    /// inferior, de las caras del tope y de los fondos de foso con su cota: fosos,
    /// plataformas y muretes (apertura morfologica de radio wallMaxWidth / 2), tipo de cada
    /// arista (exterior / hueco / foso / interna) y a que region pertenece cada cara de foso.
    /// Rechaza con motivo los bloques sin fosos, con cavidades cerradas o cuyas caras no
    /// cubren el contorno inferior (paredes de foso no verticales). Coordenadas locales
    /// (u, v) en pies, z desde la cara inferior.
    /// </summary>
    public sealed class BlockTopology
    {
        public const double MmPerFt = 304.8;
        public static double Mm(double ft) => Math.Round(ft * MmPerFt);
        public static string M(double ft) => (ft * 0.3048).ToString("0.00", CultureInfo.InvariantCulture);

        /// <summary>Cota del tope (canto del bloque) desde la cara inferior.</summary>
        public double ZTop;
        public double Tol, WallMaxWidth;
        /// <summary>Contorno inferior como regiones (exteriores con sus huecos pasantes).</summary>
        public List<Region2D> Body = new List<Region2D>();
        public Outline2D Bottom;
        /// <summary>Aristas del contorno inferior: exteriores y de hueco.</summary>
        public List<RegionEdge> BodyEdges = new List<RegionEdge>();
        public List<Recess> Recesses = new List<Recess>();
        public List<TopRegion> Regions = new List<TopRegion>();
        public string Error;
        public List<string> Warnings = new List<string>();
        /// <summary>Lineas del diagnostico (modo "Analizar sin armar").</summary>
        public List<string> Diagnostics = new List<string>();

        public int Platforms => Regions.Count(r => r.Kind == RegionKind.Platform);
        public int Walls => Regions.Count(r => r.Kind == RegionKind.Wall);
        public double UMin => Bottom.UMin;
        public double UMax => Bottom.UMax;
        public double VMin => Bottom.VMin;
        public double VMax => Bottom.VMax;
        public double Width => Bottom.Width;
        public double Depth => Bottom.Depth;
        public bool Ok => Error == null;
        /// <summary>Cota del fondo de foso mas profundo (la menor).</summary>
        public double DeepestFloor => Recesses.Count == 0 ? ZTop : Recesses.Min(r => r.ZFloor);

        // =================================================================
        // Construccion
        // =================================================================

        /// <param name="bottomRings">Anillos de las caras a la cota inferior (exteriores y huecos, sin orden).</param>
        /// <param name="topRings">Anillos de las caras a la cota superior.</param>
        /// <param name="floors">Anillos de cada cara horizontal intermedia hacia arriba, con su cota desde la cara inferior.</param>
        /// <param name="zTop">Canto (pies).</param>
        /// <param name="wallMaxWidth">Ancho maximo de un murete (pies).</param>
        public static BlockTopology Build(IEnumerable<List<Pt>> bottomRings, IEnumerable<List<Pt>> topRings,
                                          IEnumerable<(List<List<Pt>> rings, double z)> floors, double zTop, double wallMaxWidth, double tol)
        {
            var t = new BlockTopology { ZTop = zTop, Tol = tol, WallMaxWidth = wallMaxWidth };
            try
            {
                t.BuildBody(bottomRings);
                if (t.Error != null) return t;
                t.BuildRecesses(floors);
                t.BuildRegions(topRings);
                t.RefineShapes();
                t.ClassifyEdges();
                t.Validate();
            }
            catch (Exception ex)
            {
                t.Error = "no se pudo clasificar la geometria: " + ex.Message;
            }
            return t;
        }

        private void BuildBody(IEnumerable<List<Pt>> rings)
        {
            Body = Poly2D.UnionRings(rings.Select(r => (IList<Pt>)r), Tol);
            if (Body.Count == 0) { Error = "el contorno inferior esta vacio"; return; }
            Bottom = new Outline2D(Body.SelectMany(r => r.Rings()), Tol);
            foreach (Region2D r in Body)
                BodyEdges.AddRange(BuildEdges(r, -1, (ring, i, e) => ring == 0 ? EdgeKind.Exterior : EdgeKind.Hole));
            Diagnostics.Add("contorno inferior: " + Body.Count + " cuerpo(s), " + Body.Sum(b => b.Holes.Count) + " hueco(s) pasante(s), " +
                            M(Width) + " x " + M(Depth) + " m, area " + Area(Body.Sum(b => b.Area)));
        }

        private void BuildRecesses(IEnumerable<(List<List<Pt>> rings, double z)> floors)
        {
            var list = floors?.ToList() ?? new List<(List<List<Pt>>, double)>();
            // se agrupan las caras a la misma cota (una canaleta puede venir en varias caras)
            var groups = new List<(double z, List<IList<Pt>> rings)>();
            foreach ((List<List<Pt>> rings, double z) in list.OrderBy(f => f.z))
            {
                if (z <= Tol) { Diagnostics.Add("cara hacia arriba a z=" + Mm(z) + " mm: es la cara inferior, se ignora"); continue; }
                if (z >= ZTop - Tol) { Diagnostics.Add("cara hacia arriba a z=" + Mm(z) + " mm: es el tope, se ignora"); continue; }
                int g = groups.FindIndex(x => Math.Abs(x.z - z) <= Tol);
                if (g < 0) { groups.Add((z, new List<IList<Pt>>())); g = groups.Count - 1; }
                foreach (List<Pt> r in rings) groups[g].rings.Add(r);
            }
            foreach ((double z, List<IList<Pt>> rings) in groups)
                foreach (Region2D comp in Poly2D.UnionRings(rings, Tol))
                {
                    var rec = new Recess { Index = Recesses.Count, Shape = comp, Outline = comp.ToOutline(Tol), ZFloor = z, Depth = ZTop - z };
                    Recesses.Add(rec);
                }
            Recesses = Recesses.OrderByDescending(r => r.Shape.Area).ToList();
            for (int i = 0; i < Recesses.Count; i++) Recesses[i].Index = i;
        }

        private void BuildRegions(IEnumerable<List<Pt>> topRings)
        {
            List<Region2D> tops = Poly2D.UnionRings(topRings.Select(r => (IList<Pt>)r), Tol);
            double r = 0.5 * WallMaxWidth;
            var platforms = new List<Region2D>();
            var walls = new List<Region2D>();
            foreach (Region2D comp in tops)
            {
                double sliver = Tol * Math.Max(comp.Width, comp.Depth);
                List<Region2D> open = Poly2D.Opening(new[] { comp }, r, Tol).Where(p => p.Area > sliver).ToList();
                platforms.AddRange(open);
                List<Region2D> rest = open.Count == 0 ? new List<Region2D> { comp } : Poly2D.Difference(new[] { comp }, open, Tol);
                foreach (Region2D w in rest)
                    if (w.Area > sliver && Poly2D.MinWidth(w, Tol) > 2 * Tol) walls.Add(w);
            }
            int np = 0, nw = 0;
            foreach (Region2D p in platforms.OrderByDescending(p => p.Area))
                Regions.Add(new TopRegion { Index = Regions.Count, KindIndex = np++, Kind = RegionKind.Platform, Shape = p, Outline = p.ToOutline(Tol), MinWidth = Poly2D.MinWidth(p, Tol) });
            foreach (Region2D w in walls.OrderByDescending(w => w.Area))
                Regions.Add(new TopRegion { Index = Regions.Count, KindIndex = nw++, Kind = RegionKind.Wall, Shape = w, Outline = w.ToOutline(Tol), MinWidth = Poly2D.MinWidth(w, Tol) });
        }

        /// <summary>Aristas de todos los anillos de una region, enlazadas (Prev / Next), con el tipo que diga el clasificador.</summary>
        public List<RegionEdge> BuildEdges(Region2D shape, int regionIndex, Func<int, int, RegionEdge, EdgeKind> classify)
        {
            var all = new List<RegionEdge>();
            int ring = 0;
            foreach (List<Pt> pts in shape.Rings())
            {
                var edges = new List<RegionEdge>();
                for (int i = 0; i < pts.Count; i++)
                {
                    var e = new RegionEdge { A = pts[i], B = pts[(i + 1) % pts.Count], Ring = ring, Index = i, RegionIndex = regionIndex };
                    if (e.Length <= Tol) continue;
                    edges.Add(e);
                }
                for (int i = 0; i < edges.Count; i++)
                {
                    edges[i].Prev = edges[(i + edges.Count - 1) % edges.Count];
                    edges[i].Next = edges[(i + 1) % edges.Count];
                }
                foreach (RegionEdge e in edges) e.Kind = classify(ring, e.Index, e);
                all.AddRange(edges);
                ring++;
            }
            return all;
        }

        /// <summary>
        /// Parte las aristas de cada region y de cada foso por los vertices de las demas
        /// regiones, fosos y del cuerpo que caen sobre ellas: una arista que toca a la vez un
        /// foso y un murete queda en dos aristas, cada una con su tipo.
        /// </summary>
        private void RefineShapes()
        {
            var pts = new List<Pt>();
            foreach (TopRegion r in Regions) pts.AddRange(r.Shape.Rings().SelectMany(x => x));
            foreach (Recess r in Recesses) pts.AddRange(r.Shape.Rings().SelectMany(x => x));
            foreach (Region2D b in Body) pts.AddRange(b.Rings().SelectMany(x => x));
            foreach (TopRegion r in Regions) r.Shape = Refine(r.Shape, pts);
            foreach (Recess r in Recesses) r.Shape = Refine(r.Shape, pts);
        }

        private Region2D Refine(Region2D shape, List<Pt> pts)
        {
            var res = new Region2D { Outer = RefineRing(shape.Outer, pts) };
            foreach (List<Pt> h in shape.Holes) res.Holes.Add(RefineRing(h, pts));
            return res;
        }

        private List<Pt> RefineRing(List<Pt> ring, List<Pt> pts)
        {
            var result = new List<Pt>();
            for (int i = 0; i < ring.Count; i++)
            {
                Pt a = ring[i], b = ring[(i + 1) % ring.Count];
                result.Add(a);
                var on = new List<(double t, Pt p)>();
                foreach (Pt p in pts)
                {
                    if (p.DistanceTo(a) <= Tol || p.DistanceTo(b) <= Tol) continue;
                    if (Geometry2D.DistanceToSegment(p, a, b, out double t) > Tol || t <= 1e-6 || t >= 1 - 1e-6) continue;
                    if (on.Any(o => o.p.DistanceTo(p) <= Tol)) continue;
                    on.Add((t, p));
                }
                foreach ((double t, Pt p) in on.OrderBy(o => o.t)) result.Add(p);
            }
            return result;
        }

        /// <summary>
        /// Tipo de cada arista de cada region y de cada foso: se sondea un punto justo fuera
        /// de la arista (3 tolerancias): si cae en un foso es cara de foso; si cae en otra
        /// region del tope es un limite interno; si cae fuera del cuerpo (o en un hueco) es
        /// exterior; si cae dentro del cuerpo sin region ni foso, queda libre (la validacion
        /// de cobertura dira que falta).
        /// </summary>
        private void ClassifyEdges()
        {
            double eps = 3 * Tol;
            foreach (TopRegion reg in Regions)
                reg.Edges = BuildEdges(reg.Shape, reg.Index, (ring, i, e) =>
                {
                    Pt probe = Geometry2D.Sub(e.Mid, Geometry2D.Scale(e.Normal, eps));
                    Recess rc = RecessAt(probe);
                    if (rc != null) { e.RecessIndex = rc.Index; return EdgeKind.Recess; }
                    TopRegion other = RegionAt(probe);
                    if (other != null && other != reg) return EdgeKind.Internal;
                    if (!InBody(probe)) return InHole(probe) ? EdgeKind.Hole : EdgeKind.Exterior;
                    return EdgeKind.Free;
                });
            foreach (Recess rc in Recesses)
            {
                rc.Edges = BuildEdges(rc.Shape, -1, (ring, i, e) =>
                {
                    Pt probe = Geometry2D.Sub(e.Mid, Geometry2D.Scale(e.Normal, eps));
                    TopRegion reg = RegionAt(probe);
                    if (reg != null) { e.RegionIndex = reg.Index; return EdgeKind.Internal; }
                    Recess other = RecessAt(probe);
                    if (other != null && other != rc) { e.RecessIndex = other.Index; return EdgeKind.Recess; }
                    if (!InBody(probe)) return InHole(probe) ? EdgeKind.Hole : EdgeKind.Exterior;
                    return EdgeKind.Free;
                });
                rc.Open = rc.Edges.Any(e => e.Kind == EdgeKind.Exterior);
            }
        }

        private void Validate()
        {
            double bodyArea = Body.Sum(b => b.Area);
            double topArea = Regions.Sum(r => r.Shape.Area);
            double recArea = Recesses.Sum(r => r.Shape.Area);
            Diagnostics.Add("tope: " + Regions.Count + " region(es) (" + Platforms + " plataforma(s), " + Walls + " murete(s)), area " + Area(topArea));
            Diagnostics.Add("fondos de foso: " + Recesses.Count + ", area " + Area(recArea));
            foreach (Recess r in Recesses) Diagnostics.Add("  " + r.Describe());
            foreach (TopRegion r in Regions) Diagnostics.Add("  " + r.Describe());

            if (Recesses.Count == 0)
            {
                Error = "el bloque no tiene ningun foso (no hay caras horizontales entre la cara inferior y el tope" +
                        (Body.Sum(b => b.Holes.Count) > 0 ? "; los huecos pasantes son huecos, no fosos" : "") +
                        "): usa el add-in de Zapatas para armarlo";
                return;
            }
            if (Regions.Count == 0)
            {
                Error = "no hay ninguna cara horizontal en el tope del bloque";
                return;
            }
            // cavidades cerradas: un fondo de foso bajo una region del tope
            double overlap = Poly2D.Area(Poly2D.Intersection(Regions.Select(r => r.Shape), Recesses.Select(r => r.Shape), Tol));
            if (overlap > 0.005 * recArea && overlap > Tol * Math.Max(Width, Depth))
            {
                Error = "cavidad cerrada: hay " + Area(overlap) + " de fondo de foso debajo de una cara del tope (el foso no esta abierto por arriba)";
                return;
            }
            // fondos superpuestos (dos cotas en el mismo sitio)
            for (int i = 0; i < Recesses.Count; i++)
                for (int j = i + 1; j < Recesses.Count; j++)
                {
                    double o = Poly2D.Area(Poly2D.Intersection(new[] { Recesses[i].Shape }, new[] { Recesses[j].Shape }, Tol));
                    if (o > Tol * Math.Max(Width, Depth))
                    {
                        Error = "los fondos de los fosos " + (i + 1) + " y " + (j + 1) + " se superponen en planta (" + Area(o) + "): cavidad cerrada o escalon dentro del foso";
                        return;
                    }
                }
            // cobertura: tope + fondos = contorno inferior (paredes verticales)
            double covered = topArea + recArea - overlap;
            double missing = bodyArea - covered;
            if (Math.Abs(missing) > 0.01 * bodyArea && Math.Abs(missing) > 4 * Tol * Math.Max(Width, Depth))
            {
                Error = missing > 0
                    ? "las caras del tope (" + Area(topArea) + ") y los fondos de foso (" + Area(recArea) + ") no cubren el contorno inferior (" +
                      Area(bodyArea) + "): faltan " + Area(missing) + ", paredes de foso no verticales, fondos inclinados o caras inclinadas"
                    : "las caras del tope y los fondos de foso suman mas que el contorno inferior (" + Area(-missing) + " de mas): el tope sobresale del contorno inferior (voladizo) o hay caras repetidas";
                return;
            }
            // cada foso debe tener alguna cara de region (si no, es un escalon exterior)
            foreach (Recess r in Recesses)
                if (!r.Edges.Any(e => e.Kind == EdgeKind.Internal))
                {
                    Error = "la cara horizontal a z=" + Mm(r.ZFloor) + " mm no tiene ninguna pared alrededor: es un escalon exterior, no un foso";
                    return;
                }
            foreach (TopRegion reg in Regions)
                if (reg.Edges.Any(e => e.Kind == EdgeKind.Free))
                    Warnings.Add(reg.KindName + " " + (reg.KindIndex + 1) + ": " + reg.Edges.Count(e => e.Kind == EdgeKind.Free) + " arista(s) sin clasificar (caras inclinadas?)");
        }

        // =================================================================
        // Consultas
        // =================================================================

        public bool InBody(Pt p)
        {
            foreach (Region2D b in Body) if (b.Contains(p)) return true;
            return false;
        }

        /// <summary>Dentro de un hueco pasante del contorno inferior.</summary>
        public bool InHole(Pt p)
        {
            foreach (Region2D b in Body)
                if (Geometry2D.PointInRing(b.Outer, p))
                    foreach (List<Pt> h in b.Holes) if (Geometry2D.PointInRing(h, p)) return true;
            return false;
        }

        public TopRegion RegionAt(Pt p)
        {
            foreach (TopRegion r in Regions) if (r.Shape.Contains(p)) return r;
            return null;
        }

        public Recess RecessAt(Pt p)
        {
            foreach (Recess r in Recesses) if (r.Shape.Contains(p)) return r;
            return null;
        }

        /// <summary>Cota del hormigon en la vertical del punto (tope, fondo de foso) o null si ahi no hay hormigon.</summary>
        public double? TopAt(Pt p)
        {
            if (RegionAt(p) != null) return ZTop;
            Recess r = RecessAt(p);
            if (r != null) return r.ZFloor;
            return null;
        }

        /// <summary>Tipo de hormigon en planta: "nucleo", "murete", "foso" o "hueco".</summary>
        public string KindAt(Pt p)
        {
            TopRegion r = RegionAt(p);
            if (r != null) return r.Kind == RegionKind.Platform ? "nucleo" : "murete";
            if (RecessAt(p) != null) return "foso";
            return "hueco";
        }

        // =================================================================
        // Descripcion
        // =================================================================

        private static string Area(double ft2) => (ft2 * 0.09290304).ToString("0.00", CultureInfo.InvariantCulture) + " m2";

        /// <summary>Resumen corto: "canto 1300 mm, 1 foso (800 mm), 1 plataforma, 1 murete en anillo".</summary>
        public string Describe()
        {
            if (Error != null) return "RECHAZADO: " + Error;
            var parts = new List<string> { "canto " + Mm(ZTop) + " mm" };
            parts.Add(Recesses.Count + (Recesses.Count == 1 ? " foso" : " fosos") + " (" +
                      string.Join(", ", Recesses.Select(r => Mm(r.Depth) + " mm" + (r.Open ? " abierto" : ""))) + ")");
            int p = Platforms, w = Walls;
            parts.Add(p + (p == 1 ? " plataforma" : " plataformas"));
            parts.Add(w + (w == 1 ? " murete" : " muretes") + (Regions.Any(r => r.Kind == RegionKind.Wall && r.IsRing) ? " en anillo" : ""));
            int holes = Body.Sum(b => b.Holes.Count);
            if (holes > 0) parts.Add(holes + (holes == 1 ? " hueco pasante" : " huecos pasantes"));
            if (Warnings.Count > 0) parts.Add("avisos: " + string.Join("; ", Warnings));
            return string.Join(", ", parts);
        }

        /// <summary>Informe del modo diagnostico (parte 2D).</summary>
        public string DescribeDetailed()
        {
            var lines = new List<string>(Diagnostics);
            if (Error != null) lines.Add("MOTIVO DE RECHAZO: " + Error);
            else lines.Add("clasificacion: " + Describe());
            return string.Join(Environment.NewLine, lines);
        }
    }
}
