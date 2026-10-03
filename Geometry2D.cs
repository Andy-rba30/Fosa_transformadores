using System;
using System.Collections.Generic;
using System.Linq;

namespace BlockRebar
{
    /// <summary>Punto 2D en coordenadas locales de la zapata (u, v), en pies.</summary>
    public struct Pt
    {
        public double U, V;
        public Pt(double u, double v) { U = u; V = v; }
        public double DistanceTo(Pt o) => Math.Sqrt((U - o.U) * (U - o.U) + (V - o.V) * (V - o.V));
        public override string ToString() => "(" + U + ", " + V + ")";
    }

    /// <summary>
    /// Tramo interior de una recta que cruza la zapata: de A a B (coordenada a lo largo de la
    /// recta, pies). HoleA / HoleB dicen si ese extremo lo marca el borde de un hueco (true)
    /// o el borde exterior (false): en el borde exterior la barra lleva gancho; en un hueco
    /// siempre para recta al recubrimiento.
    /// </summary>
    public struct Span
    {
        public double A, B;
        public bool HoleA, HoleB;
        public Span(double a, double b, bool holeA, bool holeB) { A = a; B = b; HoleA = holeA; HoleB = holeB; }
        public double Length => B - A;
        public override string ToString() => "[" + A + (HoleA ? "h" : "") + ", " + B + (HoleB ? "h" : "") + "]";
    }

    /// <summary>
    /// Contorno en coordenadas locales: uno o varios anillos exteriores (antihorarios) y sus
    /// huecos, clasificados por anidamiento (profundidad par = exterior, impar = hueco). Sabe
    /// recortar una recta (u = cte o v = cte) contra el contorno devolviendo los tramos
    /// interiores, que son las barras. Geometria pura, sin Revit.
    /// </summary>
    public sealed class Outline2D
    {
        /// <summary>Anillos exteriores, el mayor primero.</summary>
        public List<List<Pt>> Outers;
        public List<List<Pt>> Holes;
        /// <summary>El anillo exterior mayor.</summary>
        public List<Pt> Outer => Outers[0];
        public double UMin, UMax, VMin, VMax;
        public double Width => UMax - UMin;
        public double Depth => VMax - VMin;

        private Outline2D _transposed;

        /// <summary>Contorno de un solo anillo exterior con sus huecos.</summary>
        public Outline2D(List<Pt> outer, IEnumerable<List<Pt>> holes, double tol)
            : this(new[] { outer }.Concat(holes ?? new List<List<Pt>>()), tol) { }

        /// <summary>Contorno a partir de todos los anillos, sin orden: se clasifican en exteriores y huecos.</summary>
        public Outline2D(IEnumerable<List<Pt>> rings, double tol)
        {
            var clean = new List<List<Pt>>();
            foreach (List<Pt> r in rings)
            {
                if (r == null) continue;
                List<Pt> s = Geometry2D.Simplify(r, tol);
                if (s.Count >= 3 && Math.Abs(Geometry2D.SignedArea(s)) > tol * tol) clean.Add(s);
            }
            if (clean.Count == 0) throw new ArgumentException("contorno vacio");
            Outers = new List<List<Pt>>();
            Holes = new List<List<Pt>>();
            foreach (List<Pt> r in clean)
            {
                int depth = clean.Count(o => !ReferenceEquals(o, r) && Geometry2D.PointInRing(o, Geometry2D.InnerPoint(r)));
                if (depth % 2 == 0) { if (Geometry2D.SignedArea(r) < 0) r.Reverse(); Outers.Add(r); }
                else Holes.Add(r);
            }
            Outers = Outers.OrderByDescending(r => Math.Abs(Geometry2D.SignedArea(r))).ToList();
            IEnumerable<Pt> all = Outers.SelectMany(r => r);
            UMin = all.Min(p => p.U); UMax = all.Max(p => p.U);
            VMin = all.Min(p => p.V); VMax = all.Max(p => p.V);
        }

        /// <summary>Area neta (exteriores menos huecos).</summary>
        public double Area => Outers.Sum(r => Math.Abs(Geometry2D.SignedArea(r))) - Holes.Sum(h => Math.Abs(Geometry2D.SignedArea(h)));

        /// <summary>Todos los anillos: primero los exteriores, luego los huecos.</summary>
        public IEnumerable<List<Pt>> Rings()
        {
            foreach (List<Pt> o in Outers) yield return o;
            foreach (List<Pt> h in Holes) yield return h;
        }

        /// <summary>Anillos con su marca de hueco.</summary>
        public IEnumerable<(List<Pt> ring, bool hole)> RingsFlagged()
        {
            foreach (List<Pt> o in Outers) yield return (o, false);
            foreach (List<Pt> h in Holes) yield return (h, true);
        }

        /// <summary>Copia desplazada (du, dv).</summary>
        public Outline2D Translated(double du, double dv, double tol) =>
            new Outline2D(Rings().Select(r => r.Select(p => new Pt(p.U + du, p.V + dv)).ToList()), tol);

        /// <summary>El mismo contorno con u y v intercambiados (para cortar con rectas u = cte).</summary>
        public Outline2D Transposed(double tol)
        {
            if (_transposed == null)
                _transposed = new Outline2D(Rings().Select(r => r.Select(p => new Pt(p.V, p.U)).ToList()), tol);
            return _transposed;
        }

        /// <summary>True si el punto esta dentro (fuera de los huecos), regla par-impar.</summary>
        public bool Contains(Pt p)
        {
            bool inside = false;
            foreach (List<Pt> ring in Rings())
                if (Geometry2D.PointInRing(ring, p)) inside = !inside;
            return inside;
        }

        /// <summary>
        /// Tramos interiores de la recta v = coord (alongU) o u = coord (!alongU) sin franja:
        /// para contornos ya retranqueados, donde la recta puede ir justo por el borde (la
        /// recta se mete 1e-6 hacia dentro del contorno para no cortar en el filo).
        /// </summary>
        public List<Span> CutExact(bool alongU, double coord, double tol)
        {
            Outline2D o = alongU ? this : Transposed(tol);
            double lo = alongU ? VMin : UMin, hi = alongU ? VMax : UMax;
            double c = Math.Min(Math.Max(coord, lo + 1e-6), hi - 1e-6);
            // si la recta pasa justo por un vertice (tangente a un hueco o a una esquina), se
            // desplaza una micra hacia el lado que deja mas barra (no partirla por una tangencia)
            bool onVertex = false;
            foreach (List<Pt> ring in o.Rings())
                foreach (Pt p in ring)
                    if (Math.Abs(p.V - c) < 1e-9) { onVertex = true; break; }
            if (!onVertex) return Geometry2D.RawCut(o, c);
            List<Span> up = Geometry2D.RawCut(o, c + 1e-6), down = Geometry2D.RawCut(o, c - 1e-6);
            return up.Sum(s => s.Length) >= down.Sum(s => s.Length) ? up : down;
        }

        /// <summary>
        /// Tramos interiores de la recta v = coord (alongU) o u = coord (!alongU), tales que
        /// toda la franja de semiancho "halfStrip" alrededor de la recta queda dentro del
        /// hormigon: asi la barra guarda ese recubrimiento con los bordes paralelos a ella.
        /// Los extremos de cada tramo llevan la marca de hueco o exterior.
        /// </summary>
        public List<Span> Cut(bool alongU, double coord, double halfStrip, double tol)
        {
            if (halfStrip <= 0) return CutExact(alongU, coord, tol);
            Outline2D o = alongU ? this : Transposed(tol);
            double eps = Math.Max(0.5 * tol, 1e-7);
            // la franja se encoge "eps" para que una barra colocada exactamente al recubrimiento
            // (su franja toca el borde) no quede fuera por el filo del borde
            double strip = halfStrip - eps;
            var offsets = new List<double> { -eps, eps };
            if (strip > eps) { offsets.Insert(0, -strip); offsets.Add(strip); }
            List<Span> result = null;
            foreach (double d in offsets)
            {
                List<Span> raw = Geometry2D.RawCut(o, coord + d);
                result = result == null ? raw : Geometry2D.Intersect(result, raw, tol);
                if (result.Count == 0) break;
            }
            return result ?? new List<Span>();
        }
    }

    /// <summary>Geometria pura (sin Revit) de poligonos y repartos. Se puede probar fuera de Revit.</summary>
    public static class Geometry2D
    {
        /// <summary>Area con signo (positiva si el poligono va en sentido antihorario).</summary>
        public static double SignedArea(IList<Pt> poly)
        {
            double a = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                Pt p = poly[i], q = poly[(i + 1) % poly.Count];
                a += p.U * q.V - q.U * p.V;
            }
            return 0.5 * a;
        }

        /// <summary>Centroide del poligono (formula del area); el centro de la caja si el area es nula.</summary>
        public static Pt Centroid(IList<Pt> poly)
        {
            double a = SignedArea(poly);
            if (Math.Abs(a) < 1e-12)
                return new Pt(0.5 * (poly.Min(p => p.U) + poly.Max(p => p.U)), 0.5 * (poly.Min(p => p.V) + poly.Max(p => p.V)));
            double cu = 0, cv = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                Pt p = poly[i], q = poly[(i + 1) % poly.Count];
                double w = p.U * q.V - q.U * p.V;
                cu += (p.U + q.U) * w;
                cv += (p.V + q.V) * w;
            }
            return new Pt(cu / (6 * a), cv / (6 * a));
        }

        /// <summary>Quita vertices repetidos y colineales (tolerancia en pies).</summary>
        public static List<Pt> Simplify(IList<Pt> poly, double tol)
        {
            var pts = new List<Pt>();
            foreach (Pt p in poly)
                if (pts.Count == 0 || pts[pts.Count - 1].DistanceTo(p) > tol) pts.Add(p);
            while (pts.Count > 1 && pts[0].DistanceTo(pts[pts.Count - 1]) <= tol) pts.RemoveAt(pts.Count - 1);
            bool changed = true;
            while (changed && pts.Count > 3)
            {
                changed = false;
                for (int i = 0; i < pts.Count; i++)
                {
                    Pt a = pts[(i + pts.Count - 1) % pts.Count], b = pts[i], c = pts[(i + 1) % pts.Count];
                    double la = a.DistanceTo(b), lc = b.DistanceTo(c);
                    if (la < 1e-12 || lc < 1e-12) { pts.RemoveAt(i); changed = true; break; }
                    // distancia de b a la recta ac
                    double du = c.U - a.U, dv = c.V - a.V, len = Math.Sqrt(du * du + dv * dv);
                    if (len < 1e-12) continue;
                    double dist = Math.Abs(du * (b.V - a.V) - dv * (b.U - a.U)) / len;
                    // colineal y entre a y c (no un pico que vuelve)
                    double t = ((b.U - a.U) * du + (b.V - a.V) * dv) / (len * len);
                    if (dist <= tol && t > -1e-9 && t < 1 + 1e-9) { pts.RemoveAt(i); changed = true; break; }
                }
            }
            return pts;
        }

        /// <summary>Direccion unitaria del borde mas largo del anillo (du, dv), con du >= 0 (o dv > 0 si du = 0).</summary>
        public static Pt LongestEdgeDirection(IList<Pt> ring)
        {
            double best = -1; Pt dir = new Pt(1, 0);
            for (int i = 0; i < ring.Count; i++)
            {
                Pt a = ring[i], b = ring[(i + 1) % ring.Count];
                double du = b.U - a.U, dv = b.V - a.V, len = Math.Sqrt(du * du + dv * dv);
                if (len > best && len > 1e-12)
                {
                    best = len;
                    dir = new Pt(du / len, dv / len);
                }
            }
            if (dir.U < -1e-12 || (Math.Abs(dir.U) <= 1e-12 && dir.V < 0)) dir = new Pt(-dir.U, -dir.V);
            return dir;
        }

        /// <summary>Direccion del borde mas largo de entre varios anillos.</summary>
        public static Pt LongestEdgeDirection(IEnumerable<List<Pt>> rings)
        {
            double best = -1; Pt dir = new Pt(1, 0);
            foreach (List<Pt> ring in rings)
                for (int i = 0; i < ring.Count; i++)
                {
                    Pt a = ring[i], b = ring[(i + 1) % ring.Count];
                    double len = a.DistanceTo(b);
                    if (len > best) { best = len; dir = LongestEdgeDirection(new List<Pt> { a, b }); }
                }
            return dir;
        }

        /// <summary>
        /// Un punto estrictamente interior al anillo: el punto medio de su primera arista
        /// desplazado un poco hacia dentro (hacia la izquierda si el anillo es antihorario).
        /// </summary>
        public static Pt InnerPoint(IList<Pt> ring)
        {
            Pt a = ring[0], b = ring[1 % ring.Count];
            double du = b.U - a.U, dv = b.V - a.V, len = Math.Sqrt(du * du + dv * dv);
            if (len < 1e-12) return a;
            double sign = SignedArea(ring) >= 0 ? 1 : -1;
            double eps = 1e-4 * Math.Max(len, 1e-3);
            Pt m = new Pt(0.5 * (a.U + b.U), 0.5 * (a.V + b.V));
            Pt p = new Pt(m.U - sign * dv / len * eps, m.V + sign * du / len * eps);
            return PointInRing(ring, p) ? p : new Pt(m.U + sign * dv / len * eps, m.V - sign * du / len * eps);
        }

        /// <summary>Punto dentro de un anillo simple (regla par-impar, sin tolerancia).</summary>
        public static bool PointInRing(IList<Pt> ring, Pt p)
        {
            bool inside = false;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                Pt a = ring[i], b = ring[j];
                if ((a.V > p.V) != (b.V > p.V))
                {
                    double u = a.U + (p.V - a.V) / (b.V - a.V) * (b.U - a.U);
                    if (p.U < u) inside = !inside;
                }
            }
            return inside;
        }

        /// <summary>
        /// Cruces de la recta v = coord con todos los anillos del contorno, emparejados en
        /// tramos interiores (regla par-impar: exterior y huecos a la vez). Sin franja.
        /// </summary>
        public static List<Span> RawCut(Outline2D o, double coord)
        {
            var xs = new List<(double u, bool hole)>();
            foreach ((List<Pt> ring, bool holeRing) in o.RingsFlagged())
            {
                for (int i = 0; i < ring.Count; i++)
                {
                    Pt a = ring[i], b = ring[(i + 1) % ring.Count];
                    // regla semiabierta: cada vertice cuenta en un unico lado
                    if ((a.V <= coord) != (b.V <= coord))
                    {
                        double t = (coord - a.V) / (b.V - a.V);
                        xs.Add((a.U + t * (b.U - a.U), holeRing));
                    }
                }
            }
            xs.Sort((p, q) => p.u.CompareTo(q.u));
            var spans = new List<Span>();
            for (int i = 0; i + 1 < xs.Count; i += 2)
                spans.Add(new Span(xs[i].u, xs[i + 1].u, xs[i].hole, xs[i + 1].hole));
            return spans;
        }

        /// <summary>Interseccion de dos listas de tramos (cada una ordenada y sin solapes).</summary>
        public static List<Span> Intersect(List<Span> a, List<Span> b, double tol)
        {
            var result = new List<Span>();
            foreach (Span x in a)
                foreach (Span y in b)
                {
                    double s = Math.Max(x.A, y.A), e = Math.Min(x.B, y.B);
                    if (e - s <= tol) continue;
                    bool hs = x.A >= y.A - 1e-12 ? x.HoleA : y.HoleA;
                    bool he = x.B <= y.B + 1e-12 ? x.HoleB : y.HoleB;
                    // si los dos extremos coinciden, basta que uno sea de hueco para tratarlo como hueco
                    if (Math.Abs(x.A - y.A) <= tol) hs = x.HoleA || y.HoleA;
                    if (Math.Abs(x.B - y.B) <= tol) he = x.HoleB || y.HoleB;
                    result.Add(new Span(s, e, hs, he));
                }
            return result.OrderBy(sp => sp.A).ToList();
        }

        /// <summary>
        /// Posiciones equiespaciadas entre "from" y "to" (ambas incluidas) con separacion no
        /// mayor que "maxSpacing": n = ceil(L / s) huecos iguales. Una sola en el centro si no
        /// cabe mas de una.
        /// </summary>
        public static List<double> Positions(double from, double to, double maxSpacing, double tol)
        {
            var list = new List<double>();
            double len = to - from;
            if (len <= tol) { list.Add(0.5 * (from + to)); return list; }
            if (maxSpacing <= tol) maxSpacing = len;
            int n = Math.Max(1, (int)Math.Ceiling(len / maxSpacing - 1e-9));
            double step = len / n;
            for (int k = 0; k <= n; k++) list.Add(from + k * step);
            return list;
        }

        // -----------------------------------------------------------------
        // Rectas generales (no solo u = cte / v = cte): para barras a lo largo de caras con
        // cualquier orientacion. Los tramos se devuelven como parametro t a lo largo de "dir".
        // -----------------------------------------------------------------

        public static double Dot(Pt a, Pt b) => a.U * b.U + a.V * b.V;
        public static double Cross(Pt a, Pt b) => a.U * b.V - a.V * b.U;
        public static Pt Add(Pt a, Pt b) => new Pt(a.U + b.U, a.V + b.V);
        public static Pt Sub(Pt a, Pt b) => new Pt(a.U - b.U, a.V - b.V);
        public static Pt Scale(Pt a, double k) => new Pt(a.U * k, a.V * k);
        public static double Length(Pt a) => Math.Sqrt(a.U * a.U + a.V * a.V);
        public static Pt Unit(Pt a) { double l = Length(a); return l < 1e-15 ? new Pt(1, 0) : new Pt(a.U / l, a.V / l); }
        /// <summary>Normal a la izquierda de la direccion (dentro de un anillo antihorario).</summary>
        public static Pt Left(Pt d) => new Pt(-d.V, d.U);

        /// <summary>
        /// Cruces de la recta origin + t * dir con todos los anillos del contorno, emparejados
        /// en tramos interiores (regla par-impar). Sin franja. Los tramos van en t.
        /// </summary>
        public static List<Span> RawLineCut(Outline2D o, Pt origin, Pt dir)
        {
            var xs = new List<(double t, bool hole)>();
            foreach ((List<Pt> ring, bool holeRing) in o.RingsFlagged())
                for (int i = 0; i < ring.Count; i++)
                {
                    Pt a = ring[i], b = ring[(i + 1) % ring.Count];
                    // lado de cada vertice respecto a la recta (regla semiabierta)
                    double sa = Cross(dir, Sub(a, origin)), sb = Cross(dir, Sub(b, origin));
                    if ((sa <= 0) != (sb <= 0))
                    {
                        double k = sa / (sa - sb);
                        Pt p = new Pt(a.U + k * (b.U - a.U), a.V + k * (b.V - a.V));
                        xs.Add((Dot(Sub(p, origin), dir), holeRing));
                    }
                }
            xs.Sort((p, q) => p.t.CompareTo(q.t));
            var spans = new List<Span>();
            for (int i = 0; i + 1 < xs.Count; i += 2)
                spans.Add(new Span(xs[i].t, xs[i + 1].t, xs[i].hole, xs[i + 1].hole));
            return spans;
        }

        /// <summary>
        /// Tramos interiores de la recta origin + t * dir (dir unitaria) tales que toda la
        /// franja de semiancho "halfStrip" alrededor de la recta queda dentro del contorno.
        /// Equivale a Outline2D.Cut para una recta cualquiera.
        /// </summary>
        public static List<Span> LineCut(Outline2D o, Pt origin, Pt dir, double halfStrip, double tol)
        {
            dir = Unit(dir);
            Pt n = Left(dir);
            double eps = Math.Max(0.5 * tol, 1e-7);
            double strip = halfStrip - eps;
            var offsets = new List<double> { -eps, eps };
            if (strip > eps) { offsets.Insert(0, -strip); offsets.Add(strip); }
            List<Span> result = null;
            foreach (double d in offsets)
            {
                List<Span> raw = RawLineCut(o, Add(origin, Scale(n, d)), dir);
                result = result == null ? raw : Intersect(result, raw, tol);
                if (result.Count == 0) break;
            }
            return result ?? new List<Span>();
        }

        /// <summary>
        /// Parametro t de la recta p + t * d en su cruce con la recta q + s * e; NaN si son
        /// paralelas.
        /// </summary>
        public static double LineParam(Pt p, Pt d, Pt q, Pt e)
        {
            double den = Cross(d, e);
            if (Math.Abs(den) < 1e-12) return double.NaN;
            return Cross(Sub(q, p), e) / den;
        }

        /// <summary>Distancia de un punto al segmento ab y parametro (0..1) del pie de la perpendicular.</summary>
        public static double DistanceToSegment(Pt p, Pt a, Pt b, out double t)
        {
            Pt ab = Sub(b, a);
            double l2 = Dot(ab, ab);
            t = l2 < 1e-18 ? 0 : Dot(Sub(p, a), ab) / l2;
            double tc = Math.Max(0, Math.Min(1, t));
            Pt f = new Pt(a.U + tc * ab.U, a.V + tc * ab.V);
            return p.DistanceTo(f);
        }

        /// <summary>Distancia (con signo, positiva a la izquierda) de un punto a la recta ab.</summary>
        public static double SignedDistanceToLine(Pt p, Pt a, Pt b)
        {
            Pt d = Unit(Sub(b, a));
            return Cross(d, Sub(p, a));
        }

        /// <summary>Une tramos [a, b] que se solapan o se tocan (hueco menor o igual que "gap").</summary>
        public static List<(double a, double b)> Merge(IEnumerable<(double a, double b)> parts, double gap)
        {
            var list = parts.Where(p => p.b > p.a).OrderBy(p => p.a).ToList();
            var result = new List<(double a, double b)>();
            foreach ((double a, double b) p in list)
            {
                if (result.Count > 0 && p.a <= result[result.Count - 1].b + gap)
                {
                    (double a, double b) last = result[result.Count - 1];
                    result[result.Count - 1] = (last.a, Math.Max(last.b, p.b));
                }
                else result.Add(p);
            }
            return result;
        }
    }
}
