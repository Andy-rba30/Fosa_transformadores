using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BlockRebar
{
    /// <summary>Un choque (o contacto) entre dos barras: tramo contra tramo, en 3D.</summary>
    public sealed class Clash
    {
        public PlannedBar A, B;
        public int SegA, SegB;
        /// <summary>Punto medio entre los puntos mas cercanos de los dos ejes (coordenadas locales, pies).</summary>
        public P3 Point;
        /// <summary>Distancia entre ejes (pies).</summary>
        public double Distance;
        /// <summary>Suma de radios (pies).</summary>
        public double Required;
        public bool IsContact;

        public string Pair => Families.Code(A.Family) + "-" + Families.Code(B.Family);

        public string Describe() =>
            Pair + " en (u=" + BlockPlan.ToMm(Point.U) + ", v=" + BlockPlan.ToMm(Point.V) + ", z=" + BlockPlan.ToMm(Point.Z) + ") mm: " +
            "distancia entre ejes " + (Distance * BlockPlan.MmPerFt).ToString("0.0", CultureInfo.InvariantCulture) + " mm, suma de radios " +
            (Required * BlockPlan.MmPerFt).ToString("0.0", CultureInfo.InvariantCulture) + " mm [" + A.Face + " | " + B.Face + "]";
    }

    /// <summary>Resultado de la comprobacion de choques de un plan.</summary>
    public sealed class ClashReport
    {
        /// <summary>Distancia entre ejes menor que la suma de radios menos la tolerancia: inadmisible.</summary>
        public List<Clash> Clashes = new List<Clash>();
        /// <summary>Contactos (distancia igual a la suma de radios, +- tolerancia) entre familias en las que no estaban previstos.</summary>
        public List<Clash> UnexpectedContacts = new List<Clash>();
        /// <summary>Contactos previstos (apoyo de capas, F6 con la pata exterior de F7...), solo contados.</summary>
        public int ExpectedContacts;
        public int PairsChecked;

        public bool Ok => Clashes.Count == 0;

        public Dictionary<string, int> ByPair(IEnumerable<Clash> list)
        {
            var d = new Dictionary<string, int>();
            foreach (Clash c in list) d[c.Pair] = (d.TryGetValue(c.Pair, out int n) ? n : 0) + 1;
            return d;
        }

        /// <summary>Informe: choques por par de familias con coordenadas, y contactos no previstos.</summary>
        public string Describe(int maxLines = 40)
        {
            var lines = new List<string>();
            lines.Add("choques: " + Clashes.Count + " (pares de barras comprobados: " + PairsChecked + ", contactos previstos: " + ExpectedContacts +
                      ", contactos no previstos: " + UnexpectedContacts.Count + ")");
            foreach (var kv in ByPair(Clashes).OrderBy(k => k.Key))
                lines.Add("  " + kv.Key + ": " + kv.Value + " choque(s)");
            int shown = 0;
            foreach (Clash c in Clashes.OrderBy(c => c.Pair).ThenBy(c => c.Point.Z))
            {
                if (shown++ >= maxLines) { lines.Add("  ... (" + (Clashes.Count - maxLines) + " mas)"); break; }
                lines.Add("    " + c.Describe());
            }
            foreach (var kv in ByPair(UnexpectedContacts).OrderBy(k => k.Key))
                lines.Add("  contacto no previsto " + kv.Key + ": " + kv.Value + " (" + UnexpectedContacts.First(c => c.Pair == kv.Key).Describe() + ")");
            return string.Join(Environment.NewLine, lines);
        }
    }

    /// <summary>
    /// Comprobacion generica de choques entre barras de conjuntos distintos: para cada par de
    /// tramos (en 3D) la distancia entre ejes debe ser al menos la suma de radios menos la
    /// tolerancia (1 mm). Los contactos (distancia igual a la suma de radios) solo se admiten
    /// donde estan previstos: las dos capas de una malla apoyadas una en otra, F6 con la pata
    /// exterior de F7, F8 con esa misma pata y con la vertical de esquina de F6 de la cara
    /// contigua (mismo plano), las patas de F3 contra F4 y contra F5, F5 bajo F3, y los cruces
    /// de esquina de F5 / F8 / pies de F4 desplazados un diametro. Pura (sin Revit).
    /// </summary>
    public static class ClashCheck
    {
        /// <summary>Pares de familias en los que el contacto es de diseno (a &lt;= b).</summary>
        private static readonly HashSet<(Family, Family)> ExpectedContactPairs = new HashSet<(Family, Family)>
        {
            (Family.F1, Family.F1), (Family.F2, Family.F2), (Family.F3, Family.F3),
            (Family.F5, Family.F5), (Family.F8, Family.F8),
            (Family.F6, Family.F7), (Family.F7, Family.F8), (Family.F6, Family.F8),
            (Family.F3, Family.F4), (Family.F3, Family.F5), (Family.F4, Family.F4)
        };

        public static bool ContactExpected(Family a, Family b) =>
            ExpectedContactPairs.Contains((int)a <= (int)b ? (a, b) : (b, a));

        /// <param name="tolerance">Tolerancia (pies): un choque es distancia &lt; suma de radios - tolerancia.</param>
        public static ClashReport Check(BlockPlan plan, double tolerance)
        {
            var rep = new ClashReport();
            if (plan == null || plan.Error != null) return rep;
            List<PlannedBar> bars = plan.Bars;
            int n = bars.Count;
            // conjunto de cada barra (las barras de un mismo conjunto no se comparan)
            var groupOf = new Dictionary<PlannedBar, int>();
            for (int g = 0; g < plan.Groups.Count; g++) foreach (PlannedBar b in plan.Groups[g].Bars) groupOf[b] = g;
            // cajas envolventes
            var box = new (P3 min, P3 max)[n];
            for (int i = 0; i < n; i++)
            {
                var pts = bars[i].Points;
                box[i] = (new P3(pts.Min(p => p.U), pts.Min(p => p.V), pts.Min(p => p.Z)), new P3(pts.Max(p => p.U), pts.Max(p => p.V), pts.Max(p => p.Z)));
            }
            for (int i = 0; i < n; i++)
            {
                PlannedBar a = bars[i];
                int ga = groupOf.TryGetValue(a, out int gi) ? gi : -1 - i;
                for (int j = i + 1; j < n; j++)
                {
                    PlannedBar b = bars[j];
                    int gb = groupOf.TryGetValue(b, out int gj) ? gj : -1 - j;
                    if (ga == gb) continue;
                    double req = 0.5 * (a.D + b.D);
                    double reach = req + tolerance;
                    if (box[i].min.U > box[j].max.U + reach || box[j].min.U > box[i].max.U + reach ||
                        box[i].min.V > box[j].max.V + reach || box[j].min.V > box[i].max.V + reach ||
                        box[i].min.Z > box[j].max.Z + reach || box[j].min.Z > box[i].max.Z + reach) continue;
                    rep.PairsChecked++;
                    for (int sa = 0; sa + 1 < a.Points.Count; sa++)
                        for (int sb = 0; sb + 1 < b.Points.Count; sb++)
                        {
                            double dist = SegmentDistance(a.Points[sa], a.Points[sa + 1], b.Points[sb], b.Points[sb + 1], out P3 pa, out P3 pb);
                            if (dist >= req + tolerance) continue;
                            var c = new Clash { A = a, B = b, SegA = sa, SegB = sb, Point = (pa + pb) * 0.5, Distance = dist, Required = req };
                            if (dist < req - tolerance) rep.Clashes.Add(c);
                            else
                            {
                                c.IsContact = true;
                                if (ContactExpected(a.Family, b.Family)) rep.ExpectedContacts++;
                                else rep.UnexpectedContacts.Add(c);
                            }
                        }
                }
            }
            return rep;
        }

        /// <summary>Distancia minima entre los segmentos p1q1 y p2q2 (Ericson, Real-Time Collision Detection 5.1.9) y los puntos mas cercanos.</summary>
        public static double SegmentDistance(P3 p1, P3 q1, P3 p2, P3 q2, out P3 c1, out P3 c2)
        {
            const double eps = 1e-12;
            P3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            double a = d1.Dot(d1), e = d2.Dot(d2), f = d2.Dot(r);
            double s, t;
            if (a <= eps && e <= eps) { c1 = p1; c2 = p2; return c1.DistanceTo(c2); }
            if (a <= eps) { s = 0; t = Clamp(f / e); }
            else
            {
                double c = d1.Dot(r);
                if (e <= eps) { t = 0; s = Clamp(-c / a); }
                else
                {
                    double b = d1.Dot(d2);
                    double denom = a * e - b * b;
                    s = denom != 0 ? Clamp((b * f - c * e) / denom) : 0;
                    t = (b * s + f) / e;
                    if (t < 0) { t = 0; s = Clamp(-c / a); }
                    else if (t > 1) { t = 1; s = Clamp((b - c) / a); }
                }
            }
            c1 = p1 + d1 * s;
            c2 = p2 + d2 * t;
            return c1.DistanceTo(c2);
        }

        private static double Clamp(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);
    }
}
