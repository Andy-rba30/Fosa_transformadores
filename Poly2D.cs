using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;

namespace BlockRebar
{
    /// <summary>
    /// Region plana: un anillo exterior (antihorario) con sus huecos (horarios), de modo que
    /// el interior queda siempre a la izquierda de cada arista. Pura (sin Revit).
    /// </summary>
    public sealed class Region2D
    {
        public List<Pt> Outer;
        public List<List<Pt>> Holes = new List<List<Pt>>();

        public Region2D() { }
        public Region2D(List<Pt> outer, IEnumerable<List<Pt>> holes = null)
        {
            Outer = Geometry2D.SignedArea(outer) < 0 ? Enumerable.Reverse(outer).ToList() : new List<Pt>(outer);
            if (holes != null)
                foreach (List<Pt> h in holes)
                    Holes.Add(Geometry2D.SignedArea(h) > 0 ? Enumerable.Reverse(h).ToList() : new List<Pt>(h));
        }

        public double Area => Math.Abs(Geometry2D.SignedArea(Outer)) - Holes.Sum(h => Math.Abs(Geometry2D.SignedArea(h)));
        public double UMin => Outer.Min(p => p.U);
        public double UMax => Outer.Max(p => p.U);
        public double VMin => Outer.Min(p => p.V);
        public double VMax => Outer.Max(p => p.V);
        public double Width => UMax - UMin;
        public double Depth => VMax - VMin;

        public IEnumerable<List<Pt>> Rings()
        {
            yield return Outer;
            foreach (List<Pt> h in Holes) yield return h;
        }

        /// <summary>True si el punto esta dentro (fuera de los huecos).</summary>
        public bool Contains(Pt p)
        {
            if (!Geometry2D.PointInRing(Outer, p)) return false;
            foreach (List<Pt> h in Holes) if (Geometry2D.PointInRing(h, p)) return false;
            return true;
        }

        public Outline2D ToOutline(double tol) => new Outline2D(Rings(), tol);

        public Region2D Translated(double du, double dv) =>
            new Region2D(Outer.Select(p => new Pt(p.U + du, p.V + dv)).ToList(), Holes.Select(h => h.Select(p => new Pt(p.U + du, p.V + dv)).ToList()));
    }

    /// <summary>
    /// Booleanas y offsets 2D sobre Clipper2: union, diferencia, interseccion, offset,
    /// apertura morfologica (clasificacion plataforma / murete), ancho minimo, componentes
    /// conexas e inset por arista con recubrimiento distinto por tipo de borde. Las
    /// coordenadas van en pies; Clipper trabaja en enteros con 6 decimales (0.3 micras).
    /// </summary>
    public static class Poly2D
    {
        private const int Precision = 6;

        // -----------------------------------------------------------------
        // Conversiones
        // -----------------------------------------------------------------
        private static PathD ToPath(IList<Pt> ring)
        {
            var p = new PathD(ring.Count);
            foreach (Pt q in ring) p.Add(new PointD(q.U, q.V));
            return p;
        }

        private static PathsD ToPaths(IEnumerable<IList<Pt>> rings)
        {
            var ps = new PathsD();
            foreach (IList<Pt> r in rings) if (r != null && r.Count >= 3) ps.Add(ToPath(r));
            return ps;
        }

        private static PathsD ToPaths(IEnumerable<Region2D> regions) => ToPaths(regions.SelectMany(r => r.Rings()));

        private static List<Pt> ToRing(PathD path) => path.Select(p => new Pt(p.x, p.y)).ToList();

        /// <summary>
        /// Anillos de Clipper clasificados en regiones: cada anillo exterior con los huecos que
        /// contiene directamente (por anidamiento, sin fiarse de la orientacion). Se descartan
        /// los anillos degenerados.
        /// </summary>
        public static List<Region2D> ToRegions(PathsD paths, double tol)
        {
            var rings = new List<List<Pt>>();
            foreach (PathD p in paths)
            {
                List<Pt> r = Geometry2D.Simplify(ToRing(p), tol);
                if (r.Count >= 3 && Math.Abs(Geometry2D.SignedArea(r)) > tol * tol) rings.Add(r);
            }
            var depth = new int[rings.Count];
            for (int i = 0; i < rings.Count; i++)
            {
                Pt inner = Geometry2D.InnerPoint(rings[i]);
                for (int j = 0; j < rings.Count; j++)
                    if (i != j && Geometry2D.PointInRing(rings[j], inner)) depth[i]++;
            }
            var result = new List<Region2D>();
            var outerIndex = new Dictionary<int, Region2D>();
            for (int i = 0; i < rings.Count; i++)
                if (depth[i] % 2 == 0)
                {
                    var reg = new Region2D(rings[i]);
                    outerIndex[i] = reg;
                    result.Add(reg);
                }
            for (int i = 0; i < rings.Count; i++)
                if (depth[i] % 2 == 1)
                {
                    // padre: el exterior mas pequeno que lo contiene
                    Pt inner = Geometry2D.InnerPoint(rings[i]);
                    Region2D parent = null; double best = double.MaxValue;
                    foreach (var kv in outerIndex)
                    {
                        if (!Geometry2D.PointInRing(rings[kv.Key], inner)) continue;
                        double a = Math.Abs(Geometry2D.SignedArea(rings[kv.Key]));
                        if (a < best) { best = a; parent = kv.Value; }
                    }
                    if (parent != null) parent.Holes.Add(Geometry2D.SignedArea(rings[i]) > 0 ? Enumerable.Reverse(rings[i]).ToList() : rings[i]);
                }
            return result.OrderByDescending(r => r.Area).ToList();
        }

        // -----------------------------------------------------------------
        // Booleanas
        // -----------------------------------------------------------------

        /// <summary>
        /// Union de anillos sueltos (caras de Revit: exteriores y huecos sin orden, que no se
        /// solapan entre si): regla par-impar, asi un anillo dentro de otro es un hueco.
        /// </summary>
        public static List<Region2D> UnionRings(IEnumerable<IList<Pt>> rings, double tol) =>
            ToRegions(Clipper.BooleanOp(ClipType.Union, ToPaths(rings), null, FillRule.EvenOdd, Precision), tol);

        /// <summary>Union de regiones (pueden solaparse).</summary>
        public static List<Region2D> Union(IEnumerable<Region2D> regions, double tol)
        {
            PathsD acc = null;
            foreach (Region2D r in regions)
            {
                PathsD p = ToPaths(new[] { r });
                acc = acc == null ? Clipper.BooleanOp(ClipType.Union, p, null, FillRule.EvenOdd, Precision) : Clipper.Union(acc, p, FillRule.EvenOdd, Precision);
            }
            return acc == null ? new List<Region2D>() : ToRegions(acc, tol);
        }

        public static List<Region2D> Difference(IEnumerable<Region2D> subject, IEnumerable<Region2D> clip, double tol)
        {
            PathsD s = UnionPaths(subject), c = UnionPaths(clip);
            if (s.Count == 0) return new List<Region2D>();
            if (c.Count == 0) return ToRegions(s, tol);
            return ToRegions(Clipper.Difference(s, c, FillRule.EvenOdd, Precision), tol);
        }

        public static List<Region2D> Intersection(IEnumerable<Region2D> subject, IEnumerable<Region2D> clip, double tol)
        {
            PathsD s = UnionPaths(subject), c = UnionPaths(clip);
            if (s.Count == 0 || c.Count == 0) return new List<Region2D>();
            return ToRegions(Clipper.Intersect(s, c, FillRule.EvenOdd, Precision), tol);
        }

        private static PathsD UnionPaths(IEnumerable<Region2D> regions)
        {
            PathsD acc = null;
            foreach (Region2D r in regions)
            {
                PathsD p = ToPaths(new[] { r });
                acc = acc == null ? Clipper.BooleanOp(ClipType.Union, p, null, FillRule.EvenOdd, Precision) : Clipper.Union(acc, p, FillRule.EvenOdd, Precision);
            }
            return acc ?? new PathsD();
        }

        public static double Area(IEnumerable<Region2D> regions) => regions.Sum(r => r.Area);

        // -----------------------------------------------------------------
        // Offsets
        // -----------------------------------------------------------------

        /// <summary>Offset uniforme (positivo hacia fuera, negativo hacia dentro) con esquinas a inglete (las de 90 grados se conservan).</summary>
        public static List<Region2D> Offset(IEnumerable<Region2D> regions, double delta, double tol)
        {
            PathsD src = UnionPaths(regions);
            if (src.Count == 0) return new List<Region2D>();
            PathsD res = Clipper.InflatePaths(src, delta, Clipper2Lib.JoinType.Miter, Clipper2Lib.EndType.Polygon, 2.0, Precision);
            return ToRegions(res, tol);
        }

        /// <summary>Offset de un anillo cerrado solo (sin huecos).</summary>
        public static List<Region2D> OffsetRing(IList<Pt> ring, double delta, double tol) =>
            Offset(new[] { new Region2D(new List<Pt>(ring)) }, delta, tol);

        /// <summary>
        /// Apertura morfologica de radio r: erosion y luego dilatacion. Lo que sobrevive tiene
        /// ancho mayor que 2 r en todas partes (plataforma); lo que desaparece es mas estrecho
        /// (murete). Parte una region mixta en sus partes anchas.
        /// </summary>
        public static List<Region2D> Opening(IEnumerable<Region2D> regions, double r, double tol)
        {
            List<Region2D> eroded = Offset(regions, -r, tol);
            if (eroded.Count == 0) return eroded;
            return Offset(eroded, r, tol);
        }

        /// <summary>
        /// Ancho minimo de una region: el doble del mayor radio de erosion que no la vacia
        /// (biseccion). Para un rectangulo es su lado corto; para un anillo, su espesor.
        /// </summary>
        public static double MinWidth(Region2D region, double tol)
        {
            double lo = 0, hi = 0.5 * Math.Max(region.Width, region.Depth);
            PathsD src = ToPaths(new[] { region });
            for (int i = 0; i < 18 && hi - lo > 0.1 * tol; i++)
            {
                double mid = 0.5 * (lo + hi);
                // area directa de Clipper (sin simplificar los anillos, para no perder las franjas finas)
                double a = Clipper.Area(Clipper.InflatePaths(src, -mid, Clipper2Lib.JoinType.Miter, Clipper2Lib.EndType.Polygon, 2.0, Precision));
                if (Math.Abs(a) > 1e-9) lo = mid; else hi = mid;
            }
            return 2 * lo;
        }

        /// <summary>
        /// Region retranqueada arista a arista: cada arista se desplaza hacia el interior su
        /// propio valor (recubrimiento + medio diametro segun el tipo de borde) y los nuevos
        /// vertices son los cruces de las rectas desplazadas contiguas. El resultado se limpia
        /// con Clipper (regla positiva: las partes que se invierten desaparecen). Devuelve las
        /// componentes resultantes (puede ser ninguna si la region es mas estrecha que los
        /// retranqueos).
        /// </summary>
        public static List<Region2D> InsetByEdge(Region2D region, Func<int, int, double> insetOf, double tol)
        {
            var paths = new PathsD();
            int ringIndex = 0;
            foreach (List<Pt> ring in region.Rings())
            {
                List<Pt> shifted = InsetRing(ring, i => insetOf(ringIndex, i));
                if (shifted != null && shifted.Count >= 3) paths.Add(ToPath(shifted));
                ringIndex++;
            }
            if (paths.Count == 0) return new List<Region2D>();
            PathsD clean = Clipper.BooleanOp(ClipType.Union, paths, null, FillRule.Positive, Precision);
            return ToRegions(clean, tol);
        }

        /// <summary>Anillo con cada arista i desplazada "inset(i)" hacia la izquierda (interior) y vertices en los cruces.</summary>
        public static List<Pt> InsetRing(List<Pt> ring, Func<int, double> inset)
        {
            int n = ring.Count;
            if (n < 3) return null;
            var dirs = new Pt[n];
            var offs = new Pt[n];   // un punto de cada recta desplazada
            for (int i = 0; i < n; i++)
            {
                Pt a = ring[i], b = ring[(i + 1) % n];
                dirs[i] = Geometry2D.Unit(Geometry2D.Sub(b, a));
                Pt nrm = Geometry2D.Left(dirs[i]);
                offs[i] = Geometry2D.Add(a, Geometry2D.Scale(nrm, inset(i)));
            }
            var result = new List<Pt>(n + 4);
            for (int i = 0; i < n; i++)
            {
                int h = (i + n - 1) % n;   // arista que llega al vertice i
                double t = Geometry2D.LineParam(offs[h], dirs[h], offs[i], dirs[i]);
                if (double.IsNaN(t))
                {
                    // aristas colineales con retranqueo distinto (p. ej. cara de foso entre dos limites
                    // internos): las rectas desplazadas son paralelas y el contorno da un escalon
                    // perpendicular en el vertice: fin de la recta anterior y principio de la siguiente
                    Pt endPrev = Geometry2D.Add(ring[i], Geometry2D.Scale(Geometry2D.Left(dirs[h]), inset(h)));
                    Pt startNext = Geometry2D.Add(ring[i], Geometry2D.Scale(Geometry2D.Left(dirs[i]), inset(i)));
                    result.Add(endPrev);
                    if (endPrev.DistanceTo(startNext) > 1e-9) result.Add(startNext);
                }
                else result.Add(Geometry2D.Add(offs[h], Geometry2D.Scale(dirs[h], t)));
            }
            return result;
        }

        /// <summary>Caja envolvente de un conjunto de regiones.</summary>
        public static (double umin, double vmin, double umax, double vmax) Bounds(IEnumerable<Region2D> regions)
        {
            double umin = double.MaxValue, vmin = double.MaxValue, umax = double.MinValue, vmax = double.MinValue;
            foreach (Region2D r in regions)
                foreach (Pt p in r.Outer)
                {
                    umin = Math.Min(umin, p.U); umax = Math.Max(umax, p.U);
                    vmin = Math.Min(vmin, p.V); vmax = Math.Max(vmax, p.V);
                }
            return (umin, vmin, umax, vmax);
        }
    }
}
