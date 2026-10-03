using System;
using System.Collections.Generic;
using System.Linq;

namespace BlockRebar.Tests
{
    /// <summary>
    /// Pruebas de consola de las clases puras (sin Revit). Imprime OK/FALLO por comprobacion,
    /// la clasificacion, las tablas de cantidades y el conteo de circulos por familia en las
    /// secciones A-A y B-B de cada caso, y termina con codigo 1 si algo falla.
    /// </summary>
    internal static class Program
    {
        private static int _fail, _ok;
        private const double Ft = 304.8;
        private static double Mm(double mm) => mm / Ft;
        private static double ToMm(double ft) => Math.Round(ft * Ft, 1);

        private static void Check(bool cond, string what)
        {
            if (cond) { _ok++; Console.WriteLine("  OK    " + what); }
            else { _fail++; Console.WriteLine("  FALLO " + what); }
        }

        private static void Near(double a, double bMm, string what, double tolMm = 0.5) =>
            Check(Math.Abs(ToMm(a) - bMm) <= tolMm, what + " = " + ToMm(a) + " mm (esperado " + bMm + ")");

        private static List<Pt> Box(double u1, double v1, double u2, double v2) =>
            new List<Pt> { new Pt(Mm(u1), Mm(v1)), new Pt(Mm(u2), Mm(v1)), new Pt(Mm(u2), Mm(v2)), new Pt(Mm(u1), Mm(v2)) };

        /// <summary>Diametros reales de los tipos del plano: 5/8" = 15.875 mm, 3/8" = 9.525 mm; doblado estandar 6 d, estribo 4 d.</summary>
        private static PlanDiameters Diam(AppConfig c)
        {
            var d = new PlanDiameters();
            foreach ((Family f, string layer, string name) in c.BarTypesNeeded())
            {
                double mm = name.Contains("5/8") ? 15.875 : name.Contains("3/8") ? 9.525 : name.Contains("1/2") ? 12.7 : 12.7;
                d.Set(f, layer, Mm(mm), name, Mm(6 * mm), Mm(4 * mm));
            }
            return d;
        }

        private static AppConfig Cfg()
        {
            var c = new AppConfig();
            c.Normalize();
            return c;
        }

        private static int Main()
        {
            Console.WriteLine("== Geometry2D (rectas generales) ==");
            Geometry();
            Console.WriteLine("== Poly2D (Clipper2) ==");
            Poly();
            Console.WriteLine("== Caso del plano: bloque 4800 x 3800 x 1300, murete 150, foso perimetral 600 x 800, nucleo 3300 x 2300 ==");
            PlanCase();
            Console.WriteLine("== Caso (a): canaleta solo en un lado ==");
            CaseA();
            Console.WriteLine("== Caso (b): foso rectangular central sin murete ==");
            CaseB();
            Console.WriteLine("== Casos extra ==");
            Extra();
            Console.WriteLine("== Config y particion ==");
            ConfigAndPartition();
            Console.WriteLine();
            Console.WriteLine(_ok + " comprobaciones correctas, " + _fail + " fallos");
            return _fail == 0 ? 0 : 1;
        }

        // =================================================================
        // Geometria
        // =================================================================
        private static void Geometry()
        {
            var r = new Outline2D(Box(0, 0, 2000, 1000), null, Mm(2));
            List<Span> sp = Geometry2D.LineCut(r, new Pt(0, Mm(500)), new Pt(1, 0), 0, Mm(2));
            Check(sp.Count == 1, "LineCut horizontal por el centro: un tramo (" + sp.Count + ")");
            if (sp.Count == 1) { Near(sp[0].A, 0, "tramo A"); Near(sp[0].B, 2000, "tramo B"); }
            sp = Geometry2D.LineCut(r, new Pt(Mm(1000), Mm(-100)), new Pt(0, 1), Mm(80), Mm(2));
            Check(sp.Count == 1, "LineCut vertical con franja: un tramo");
            if (sp.Count == 1) { Near(sp[0].A, 100, "entra en v=0 (t=100)"); Near(sp[0].B, 1100, "sale en v=1000"); }
            sp = Geometry2D.LineCut(r, new Pt(Mm(1000), Mm(950)), new Pt(1, 0), Mm(80), Mm(2));
            Check(sp.Count == 0, "franja de 80 a 50 del borde: fuera");
            Pt diag = Geometry2D.Unit(new Pt(1, 1));
            sp = Geometry2D.LineCut(r, new Pt(0, 0), diag, 0, Mm(2));
            Check(sp.Count == 1 && Math.Abs(ToMm(sp[0].B) - Math.Round(1000 * Math.Sqrt(2), 1)) < 1.5, "diagonal: sale en v=1000 (t=" + ToMm(sp.Count > 0 ? sp[0].B : 0) + ", +-1 mm por la franja)");
            double t = Geometry2D.LineParam(new Pt(0, 0), new Pt(1, 0), new Pt(Mm(300), Mm(-50)), new Pt(0, 1));
            Near(t, 300, "LineParam cruce de rectas");
            Check(double.IsNaN(Geometry2D.LineParam(new Pt(0, 0), new Pt(1, 0), new Pt(0, 1), new Pt(1, 0))), "LineParam paralelas -> NaN");
            double dist = Geometry2D.DistanceToSegment(new Pt(Mm(500), Mm(100)), new Pt(0, 0), new Pt(Mm(1000), 0), out double tt);
            Near(dist, 100, "DistanceToSegment"); Check(Math.Abs(tt - 0.5) < 1e-9, "parametro del pie 0.5");
            Near(Geometry2D.SignedDistanceToLine(new Pt(Mm(500), Mm(100)), new Pt(0, 0), new Pt(Mm(1000), 0)), 100, "SignedDistanceToLine positiva a la izquierda");
        }

        // =================================================================
        // Poly2D
        // =================================================================
        private static void Poly()
        {
            double tol = Mm(2);
            List<Region2D> u = Poly2D.UnionRings(new[] { Box(0, 0, 1000, 1000), Box(1000, 0, 2000, 1000) }, tol);
            Check(u.Count == 1, "union de dos rectangulos pegados: una region (" + u.Count + ")");
            Near(u[0].Area, 2000 * 1000 / Ft, "area de la union (ft2 * 304.8)", 1);
            List<Region2D> ring = Poly2D.UnionRings(new[] { Box(0, 0, 4800, 3800), Box(150, 150, 4650, 3650) }, tol);
            Check(ring.Count == 1 && ring[0].Holes.Count == 1, "anillo exterior + interior -> una region con un hueco");
            Check(Geometry2D.SignedArea(ring[0].Outer) > 0 && Geometry2D.SignedArea(ring[0].Holes[0]) < 0, "exterior antihorario, hueco horario");
            Check(ring[0].Contains(new Pt(Mm(75), Mm(1000))) && !ring[0].Contains(new Pt(Mm(1000), Mm(1000))), "Contains del anillo");
            List<Region2D> open = Poly2D.Opening(ring, Mm(150), tol);
            Check(open.Count == 0, "apertura de radio 150 vacia el anillo de 150 (murete)");
            List<Region2D> core = Poly2D.UnionRings(new[] { Box(750, 750, 4050, 3050) }, tol);
            open = Poly2D.Opening(core, Mm(150), tol);
            Check(open.Count == 1, "apertura del nucleo lo conserva");
            if (open.Count == 1) { Near(open[0].Width, 3300, "ancho del nucleo tras la apertura"); Near(open[0].Depth, 2300, "fondo del nucleo tras la apertura"); }
            Near(Poly2D.MinWidth(ring[0], tol), 150, "ancho minimo del anillo", 1);
            Near(Poly2D.MinWidth(core[0], tol), 2300, "ancho minimo del nucleo", 2);
            List<Region2D> diff = Poly2D.Difference(core, new[] { new Region2D(Box(1000, 1000, 2000, 2000)) }, tol);
            Check(diff.Count == 1 && diff[0].Holes.Count == 1, "diferencia deja un hueco");
            List<Region2D> off = Poly2D.Offset(core, Mm(100), tol);
            Near(off[0].Width, 3500, "offset +100 (esquinas a inglete)");
            // inset por arista: 50 en las aristas horizontales (0 y 2) y 100 en las verticales (1 y 3)
            List<Region2D> ins = Poly2D.InsetByEdge(core[0], (ring0, i) => i % 2 == 0 ? Mm(50) : Mm(100), tol);
            Check(ins.Count == 1, "inset por arista: una region");
            if (ins.Count == 1)
            {
                Near(ins[0].UMin, 850, "inset u min (100)"); Near(ins[0].UMax, 3950, "inset u max");
                Near(ins[0].VMin, 800, "inset v min (50)"); Near(ins[0].VMax, 3000, "inset v max");
            }
            ins = Poly2D.InsetByEdge(ring[0], (ring0, i) => Mm(80), tol);
            Check(ins.Count == 0, "inset de 80 por cada lado vacia el murete de 150");
        }

        // =================================================================
        // Caso del plano
        // =================================================================
        private static BlockTopology PlanTopology(double wallMm = 150, double tolMm = 2)
        {
            double w = wallMm;
            var bottom = new[] { Box(0, 0, 4800, 3800) };
            var top = new[] { Box(0, 0, 4800, 3800), Box(w, w, 4800 - w, 3800 - w), Box(750, 750, 4050, 3050) };
            var floors = new[] { (new List<List<Pt>> { Box(w, w, 4800 - w, 3800 - w), Box(750, 750, 4050, 3050) }, Mm(500)) };
            return BlockTopology.Build(bottom, top, floors, Mm(1300), Mm(300), Mm(tolMm));
        }

        private static void PlanCase()
        {
            BlockTopology t = PlanTopology();
            Console.WriteLine("  " + t.DescribeDetailed().Replace(Environment.NewLine, Environment.NewLine + "  "));
            Check(t.Error == null, "topologia sin error: " + t.Error);
            Check(t.Recesses.Count == 1, "1 foso (" + t.Recesses.Count + ")");
            if (t.Recesses.Count == 1)
            {
                Near(t.Recesses[0].Depth, 800, "profundidad del foso");
                Near(t.Recesses[0].ZFloor, 500, "fondo del foso");
                Check(!t.Recesses[0].Open, "foso cerrado (no abierto por un lado)");
                Check(t.Recesses[0].Shape.Holes.Count == 1, "foso en anillo");
            }
            Check(t.Platforms == 1 && t.Walls == 1, "1 plataforma + 1 murete (" + t.Platforms + " + " + t.Walls + ")");
            TopRegion plat = t.Regions.FirstOrDefault(r => r.Kind == RegionKind.Platform), wall = t.Regions.FirstOrDefault(r => r.Kind == RegionKind.Wall);
            if (plat != null)
            {
                Near(plat.MinWidth, 2300, "ancho minimo de la plataforma", 2);
                Check(plat.Edges.Count == 4 && plat.Edges.All(e => e.Kind == EdgeKind.Recess), "4 caras de foso en la plataforma");
                Check(plat.Edges.All(e => e.RecessIndex == 0), "las caras de la plataforma dan al foso 1");
            }
            if (wall != null)
            {
                Near(wall.MinWidth, 150, "ancho minimo del murete", 1);
                Check(wall.IsRing, "murete en anillo");
                Check(wall.Edges.Count(e => e.Kind == EdgeKind.Exterior) == 4, "4 aristas exteriores de murete (" + wall.Edges.Count(e => e.Kind == EdgeKind.Exterior) + ")");
                Check(wall.Edges.Count(e => e.Kind == EdgeKind.Recess) == 4, "4 caras de foso de murete (" + wall.Edges.Count(e => e.Kind == EdgeKind.Recess) + ")");
            }
            Check(t.TopAt(new Pt(Mm(75), Mm(1000))) == Mm(1300) && Math.Abs(t.TopAt(new Pt(Mm(400), Mm(1000))).Value - Mm(500)) < 1e-9 && t.TopAt(new Pt(Mm(2400), Mm(1900))) == Mm(1300), "TopAt murete / foso / nucleo");

            AppConfig c = Cfg();
            PlanDiameters d = Diam(c);
            BlockPlan p = BlockPlan.Build(t, c, d);
            Console.WriteLine("  " + p.Describe());
            foreach (string w in p.Warnings) Console.WriteLine("  aviso: " + w);
            Console.WriteLine(Indent(p.QuantityTable()));
            Check(p.Error == null, "plan sin error: " + p.Error);
            if (p.Error != null) return;

            // --- cotas del apilado ---
            Near(p.LayerZ["F1:u"], 75 + 7.9375, "F1 u a recubrimiento inferior + d/2");
            Near(p.LayerZ["F1:v"], 75 + 15.875 + 7.9375, "F1 v encima");
            Near(p.LayerZ["F2:u"], 500 - 75 - 7.9375, "F2 u a 75 bajo el fondo del foso");
            Near(p.LayerZ["F2:v"], 500 - 75 - 15.875 - 7.9375, "F2 v colgada debajo");
            Near(p.LayerZ["F3:u"], 1300 - 50 - 7.9375, "F3 u a recubrimiento superior + d/2");
            Near(p.LayerZ["F3:v"], 1300 - 50 - 15.875 - 7.9375, "F3 v debajo");

            // --- F1 ---
            var f1u = p.Bars.Where(b => b.Family == Family.F1 && b.Layer == "u").ToList();
            var f1v = p.Bars.Where(b => b.Family == Family.F1 && b.Layer == "v").ToList();
            Check(f1u.Count == 31, "F1 u: 31 barras @125 en 3800 (" + f1u.Count + ")");
            Check(f1v.Count == 38, "F1 v: 38 barras @125 en 4800 (" + f1v.Count + ")");
            if (f1u.Count > 0)
            {
                PlannedBar b = f1u[0];
                Check(b.Points.Count == 4, "F1 u con pata en los dos extremos (4 puntos)");
                Near(b.Points[1].U, 75 + 7.9375, "pata de F1 a recubrimiento de borde + d/2 de la cara");
                Near(b.Points[0].Z - b.Points[1].Z, 300, "pata de F1 de 300 hacia arriba");
                Near(b.Length, 4800 - 2 * 82.9375 + 600, "longitud de la barra u de F1");
                Near(b.Points[1].V, 82.9375, "primera barra u de F1 al recubrimiento + d/2");
            }
            if (f1v.Count > 0) Near(f1v[0].Points[1].U, 75 + 15.875 + 7.9375, "las barras v de F1 se retranquean un diametro mas");
            Check(p.GroupsOf(Family.F1) == 2, "F1 en 2 conjuntos (" + p.GroupsOf(Family.F1) + ")");

            // --- F2 (retranqueada d1 + d2 en los bordes exteriores: sus patas bajan por dentro de las de F1) ---
            Check(p.CountOf(Family.F2) == 30 + 38, "F2 full: 30 + 38 barras (" + p.CountOf(Family.F2) + ")");
            PlannedBar f2 = p.Bars.First(b => b.Family == Family.F2 && b.Layer == "u");
            Near(f2.Points[1].Z - f2.Points[0].Z, 220, "pata de F2 de 220 hacia abajo");
            Near(f2.Points[1].U, 75 + 15.875 + 15.875 + 7.9375, "pata de F2 a recubrimiento + d1 + d2 + d/2 de la cara (un diametro libre con la pata de F1)");
            Near(f2.Points[1].V, 75 + 15.875 + 15.875 + 7.9375, "primera barra u de F2 con el mismo retranqueo");

            // --- F3 ---
            var f3u = p.Bars.Where(b => b.Family == Family.F3 && b.Layer == "u").ToList();
            var f3v = p.Bars.Where(b => b.Family == Family.F3 && b.Layer == "v").ToList();
            Check(f3u.Count == 19, "F3 u: 19 barras en el nucleo (" + f3u.Count + ")");
            Check(f3v.Count == 27, "F3 v: 27 barras en el nucleo (" + f3v.Count + ")");
            if (f3u.Count > 0)
            {
                Near(f3u[0].Points[1].U, 750 + 40 + 15.875 + 7.9375, "F3 u empieza por dentro de F4 (cw + d4 + d/2)");
                Near(f3u[0].Length, 3300 - 2 * 63.8125 + 680, "longitud de F3 u con patas de 340");
                Near(f3u[0].Points[1].Z - f3u[0].Points[0].Z, 340, "pata de F3 hacia abajo");
                Check(f3u.All(b => b.Points.Count == 4), "todas las F3 u con pata en los dos extremos");
            }

            // --- F4 ---
            var f4 = p.Bars.Where(b => b.Family == Family.F4).ToList();
            Check(f4.Count == 84, "F4 sin esquinas duplicadas y entre las barras de F2: 25 + 25 + 17 + 17 = 84 barras (" + f4.Count + ")");
            Check(p.GroupsOf(Family.F4) == 4, "F4 en 4 conjuntos, uno por cara (" + p.GroupsOf(Family.F4) + ")");
            if (f4.Count > 0)
            {
                // una barra de una cara a lo largo de u (pie sin desfasar) y otra de una cara a lo largo de v (pie un diametro mas bajo)
                PlannedBar b = f4.First(x => Math.Abs(x.Normal.U) > 0.5);
                PlannedBar bv = f4.First(x => Math.Abs(x.Normal.V) > 0.5);
                Near(b.Points[1].Z - bv.Points[1].Z, 15.875, "los pies de las caras a lo largo de v van un diametro mas bajos que los de las caras a lo largo de u");
                Check(b.Points.Count == 3, "F4 en L (3 puntos)");
                Near(b.Points[0].Z, 1300 - 50 - 7.9375, "F4 empieza en el tope menos recubrimiento");
                Near(b.Points[0].Z - b.Points[1].Z, 1000, "vertical de F4 de 1000");
                Near(b.Points[1].Plan.DistanceTo(b.Points[2].Plan), 370, "pie de F4 de 370");
                Near(b.Length, 1370, "longitud de F4");
                double zFoot = b.Points[1].Z;
                Check(zFoot > p.LayerZ["F1:v"] + Mm(15) && zFoot < p.LayerZ["F2:v"] - Mm(15), "el pie de F4 (z=" + ToMm(zFoot) + ") queda entre F1 y F2");
                // la vertical esta pegada a la cara (cw + d/2) y el pie apunta al foso
                Pt v = b.Points[0].Plan;
                double distFace = Math.Min(Math.Min(Math.Abs(v.U - Mm(750)), Math.Abs(v.U - Mm(4050))), Math.Min(Math.Abs(v.V - Mm(750)), Math.Abs(v.V - Mm(3050))));
                Near(distFace, 40 + 7.9375, "vertical de F4 a cw + d/2 de la cara");
                Check(t.RecessAt(b.Points[2].Plan) != null, "el extremo del pie de F4 queda bajo el foso");
            }

            // --- F5 ---
            var f5 = p.Bars.Where(b => b.Family == Family.F5).ToList();
            Check(f5.Count == 16, "F5 fromTop: 4 caras x 4 niveles = 16 (" + f5.Count + ")");
            Check(p.GroupsOf(Family.F5) == 4, "F5 en 4 conjuntos verticales (" + p.GroupsOf(Family.F5) + ")");
            if (f5.Count > 0)
            {
                Pt v = new Pt(0.5 * (f5[0].Points[0].U + f5[0].Points[1].U), 0.5 * (f5[0].Points[0].V + f5[0].Points[1].V));
                double distFace = Math.Min(Math.Min(Math.Abs(v.U - Mm(750)), Math.Abs(v.U - Mm(4050))), Math.Min(Math.Abs(v.V - Mm(750)), Math.Abs(v.V - Mm(3050))));
                Near(distFace, 40 + 15.875 + 15.875 + 15.875 + 4.7625, "F5 por dentro de F4 y de la pata mas interior de F3 (la de las barras v)", 0.6);
                var f5u = f5.Where(b => Math.Abs(b.Points[0].V - b.Points[1].V) < Mm(0.5)).ToList();   // tramos a lo largo de u
                var f5v = f5.Where(b => Math.Abs(b.Points[0].U - b.Points[1].U) < Mm(0.5)).ToList();   // tramos a lo largo de v
                Near(f5u.Max(b => b.Points[0].Z), 1300 - 50 - 31.75 - 4.7625, "nivel superior de F5 justo bajo F3");
                Check(f5u.Select(b => Math.Round(b.Points[0].Z * Ft)).Distinct().Count() == 4, "4 niveles de F5 a 200 exactos desde arriba (" + string.Join(", ", f5u.Select(b => Math.Round(b.Points[0].Z * Ft)).Distinct().OrderByDescending(z => z)) + ")");
                Near(f5u.Min(b => b.Points[0].Z), 1300 - 50 - 31.75 - 4.7625 - 600, "ultimo nivel de F5 a 3 x 200 del superior (el resto queda abajo)");
                Near(f5v.Max(b => b.Points[0].Z), 1300 - 50 - 31.75 - 4.7625 - 9.525, "los tramos a lo largo de v van un diametro mas abajo (cruce de esquina sin choque)");
                double lmax = f5.Max(b => b.Length);
                Near(lmax, 3300 - 2 * (40 + 4.7625), "F5 largo prolongado hasta la esquina (recubrimiento de la cara contigua)");
            }

            // --- F6 ---
            var f6 = p.Bars.Where(b => b.Family == Family.F6).ToList();
            Check(f6.Count == 136, "F6 sin esquinas duplicadas: 38 + 38 + 30 + 30 = 136 verticales (" + f6.Count + ")");
            Check(p.GroupsOf(Family.F6) == 4, "F6 en 4 conjuntos (" + p.GroupsOf(Family.F6) + ")");
            if (f6.Count > 0)
            {
                Near(f6[0].Points[0].Z, 75 + 4.7625, "F6 arranca en recubrimiento inferior + d/2 (traslape con F1)");
                Near(f6[0].Points[1].Z, 1300 - 50 - 9.525 - 4.7625, "F6 termina bajo la horquilla");
                Pt v = f6[0].Points[0].Plan;
                double distExt = Math.Min(Math.Min(v.U, Mm(4800) - v.U), Math.Min(v.V, Mm(3800) - v.V));
                Near(distExt, 40 + 4.7625, "F6 a cw + d/2 de la cara exterior");
                // regla de esquinas: en cada esquina una sola vertical (a 44.8 de una cara y 54.3 de la otra)
                int corner = f6.Count(b => Math.Min(b.Points[0].U, Mm(4800) - b.Points[0].U) < Mm(60) && Math.Min(b.Points[0].V, Mm(3800) - b.Points[0].V) < Mm(60));
                Check(corner == 4, "una sola vertical de F6 por esquina (" + corner + " en las 4 esquinas)");
                var longFace = f6.Where(b => b.Points[0].V < Mm(50)).Select(b => b.Points[0].U).OrderBy(u => u).ToList();
                Near(longFace[0], 40 + 4.7625 + 125, "la cara larga empieza a una separacion de la barra de esquina de la cara contigua");
                Near(longFace[longFace.Count - 1], 4800 - 40 - 1.5 * 9.525, "y termina con su propia barra de esquina");
            }

            // --- F7 ---
            var f7 = p.Bars.Where(b => b.Family == Family.F7).ToList();
            Check(f7.Count == 136, "F7 alineada con F6: 136 horquillas (" + f7.Count + ")");
            Check(p.GroupsOf(Family.F7) == 4, "F7 en 4 conjuntos (" + p.GroupsOf(Family.F7) + ")");
            if (f7.Count > 0)
            {
                PlannedBar b = f7[0];
                Check(b.Points.Count == 4, "horquilla en U invertida (4 puntos)");
                Near(b.Points[1].Z, 1300 - 50 - 4.7625, "corona de la horquilla a recubrimiento superior + d/2");
                Near(b.Points[1].Z - b.Points[0].Z, 350, "patas de 350");
                Near(b.Points[1].Plan.DistanceTo(b.Points[2].Plan), 150 - (40 + 9.525 + 4.7625) - (40 + 4.7625), "distancia entre patas: pata exterior por dentro de F6");
                // alineada: misma posicion a lo largo del murete que una F6
                Check(f6.Any(x => Math.Abs(x.Points[0].U - b.Points[0].U) < Mm(0.5) || Math.Abs(x.Points[0].V - b.Points[0].V) < Mm(0.5)), "F7 en la misma posicion que una F6");
            }

            // --- F8 ---
            var f8 = p.Bars.Where(b => b.Family == Family.F8).ToList();
            Check(f8.Count == 24, "F8 fromTop: 4 tramos x 6 niveles = 24 (" + f8.Count + ")");
            Check(p.GroupsOf(Family.F8) == 4, "F8 en 4 conjuntos (" + p.GroupsOf(Family.F8) + ")");
            if (f8.Count > 0)
            {
                Pt v = new Pt(0.5 * (f8[0].Points[0].U + f8[0].Points[1].U), 0.5 * (f8[0].Points[0].V + f8[0].Points[1].V));
                double distExt = Math.Min(Math.Min(v.U, Mm(4800) - v.U), Math.Min(v.V, Mm(3800) - v.V));
                Near(distExt, 40 + 9.525 + 9.525 + 4.7625, "F8 entre las patas de F7 (cw + d6 + d7 + d/2)");
                var f8u = f8.Where(b => Math.Abs(b.Points[0].V - b.Points[1].V) < Mm(0.5)).ToList();
                Near(f8u.Max(b => b.Points[0].Z), 1300 - 50 - 9.525 - 4.7625, "nivel superior de F8 bajo la horquilla");
                Check(f8u.Select(b => Math.Round(b.Points[0].Z * Ft)).Distinct().Count() == 6, "6 niveles de F8 a 200 exactos desde la corona (" + string.Join(", ", f8u.Select(b => Math.Round(b.Points[0].Z * Ft)).Distinct().OrderByDescending(z => z)) + ")");
                Near(f8u.Min(b => b.Points[0].Z), 1300 - 50 - 9.525 - 4.7625 - 1000, "ultimo nivel de F8 a 5 x 200 de la corona, por debajo del murete (el resto queda abajo)");
                Check(f8.Min(b => b.Points[0].Z) < Mm(500), "F8 sigue por debajo del murete hasta la base");
                Near(f8.Max(b => b.Length), 4800 - 2 * (40 + 4.7625), "F8 largo prolongado hasta la esquina");
            }
            Check(p.Groups.Count == 2 + 2 + 2 + 4 + 4 + 4 + 4 + 4, "26 conjuntos en total (" + p.Groups.Count + ")");
            Clashes(p, "caso del plano");
            Check(p.Skipped == 0, "sin tramos cortos omitidos (" + p.Skipped + ")");
            Check(p.Warnings.Count == 0, "sin avisos (" + p.Warnings.Count + ")");

            // peso por diametro
            Dictionary<string, double> wd = p.WeightByDiameter();
            Check(wd.Count == 2 && wd.ContainsKey("5/8\"") && wd.ContainsKey("3/8\""), "peso por diametro: 5/8\" y 3/8\"");
            Check(Math.Abs(BlockPlan.KgPerM(Mm(15.875)) - 1.5537) < 0.002, "5/8\" pesa 1.554 kg/m (" + BlockPlan.KgPerM(Mm(15.875)).ToString("0.000") + ")");

            // --- secciones ---
            Sections(p, t, "plano",
                aa: new Dictionary<Family, (int circles, int lines)>
                {
                    [Family.F1] = (38, 1), [Family.F2] = (38, 1), [Family.F3] = (27, 1), [Family.F4] = (0, 2),
                    [Family.F5] = (8, 0), [Family.F6] = (0, 2), [Family.F7] = (0, 2), [Family.F8] = (12, 0)
                },
                bb: new Dictionary<Family, (int circles, int lines)>
                {
                    [Family.F1] = (31, 1), [Family.F2] = (30, 1), [Family.F3] = (19, 1), [Family.F4] = (0, 2),
                    [Family.F5] = (8, 0), [Family.F6] = (0, 2), [Family.F7] = (0, 2), [Family.F8] = (12, 0)
                });
            SectionCut aa0 = BlockSection.Cut(p, t, new SectionLine(true, Mm(1900)), Mm(2), Mm(260606));
            Check(aa0.Profile.Count == 5, "perfil A-A con 5 tramos (" + aa0.Profile.Count + "): " + aa0.ProfileText());
            if (aa0.Profile.Count == 5)
            {
                Check(aa0.Profile[0].Kind == "murete" && aa0.Profile[1].Kind == "foso" && aa0.Profile[2].Kind == "nucleo", "murete / foso / nucleo / foso / murete");
                Near(aa0.Profile[0].Length, 150, "murete 150"); Near(aa0.Profile[1].Length, 600, "foso 600"); Near(aa0.Profile[2].Length, 3300, "nucleo 3300");
            }
            Near(aa0.RecessDepth, 800, "profundidad del foso en la seccion"); Near(aa0.BaseThickness, 500, "espesor de la base");
            Check(aa0.Levels.Count == 3, "3 niveles: tope, fondo de foso, cara inferior (" + aa0.Levels.Count + ")");
            Near(aa0.Levels[0].Elevation, 260606 + 1300, "nivel del tope con la elevacion de la base (261.906)");
            SectionPolyline l4 = aa0.Polylines.FirstOrDefault(x => x.Family == Family.F4);
            Check(l4 != null && l4.Points.Count == 3 && Math.Abs(ToMm(l4.Points[1].V) - (242.1 - 15.875)) < 0.6, "F4 en A-A (caras a lo largo de v) como L con el pie a z=226");
            SectionCut bb0 = BlockSection.Cut(p, t, new SectionLine(false, Mm(2400)), Mm(2));
            SectionPolyline l4b = bb0.Polylines.FirstOrDefault(x => x.Family == Family.F4);
            Check(l4b != null && l4b.Points.Count == 3 && Math.Abs(ToMm(l4b.Points[1].V) - 242.1) < 0.6, "F4 en B-B (caras a lo largo de u) como L con el pie a z=242");
            SectionPolyline l7 = aa0.Polylines.FirstOrDefault(x => x.Family == Family.F7);
            Check(l7 != null && l7.Points.Count == 4, "F7 en A-A como U invertida");
            Check(aa0.Labels.Select(l => (l.Family, l.Side)).Distinct().Count() == aa0.Labels.Count, "una etiqueta por familia y lado (" + aa0.Labels.Count + ")");
            Check(aa0.Labels.Any(l => l.Family == Family.F1 && l.Text == "ø5/8\"@125") && aa0.Labels.Any(l => l.Family == Family.F8 && l.Text.StartsWith("ø3/8\"@")), "etiquetas con la notacion del plano: " + string.Join(" | ", aa0.Labels.Select(l => l.Family + " " + l.Side + " " + l.Text)));
            Check(aa0.Labels.Count(l => l.Family == Family.F6) == 2 && aa0.Labels.Count(l => l.Family == Family.F1) == 1, "F6 con etiqueta a cada lado, F1 una sola");
            // corte por el foso: cambia el perfil y F3 / F4 / F5 quedan fuera
            SectionCut aaRecess = BlockSection.Cut(p, t, new SectionLine(true, Mm(450)), Mm(2));
            Console.WriteLine("  " + aaRecess.Describe());
            Check(aaRecess.Profile.Count == 3 && aaRecess.Profile[1].Kind == "foso" && Math.Abs(ToMm(aaRecess.Profile[1].Length) - 4500) < 1, "corte por el foso: murete / foso 4500 / murete");
            Check(aaRecess.CirclesOf(Family.F3) + aaRecess.PolylinesOf(Family.F3) == 0 && aaRecess.PolylinesOf(Family.F4) == 0 && aaRecess.CirclesOf(Family.F5) == 0, "por el foso no se ven F3, F4 ni F5");
            Check(aaRecess.CirclesOf(Family.F1) == 38 && aaRecess.PolylinesOf(Family.F1) == 1, "por el foso siguen F1 (38 circulos + 1 linea)");
        }

        private static void Sections(BlockPlan p, BlockTopology t, string name, Dictionary<Family, (int circles, int lines)> aa, Dictionary<Family, (int circles, int lines)> bb)
        {
            SectionCut a = BlockSection.Cut(p, t, new SectionLine(true, 0.5 * (t.VMin + t.VMax)), Mm(2));
            SectionCut b = BlockSection.Cut(p, t, new SectionLine(false, 0.5 * (t.UMin + t.UMax)), Mm(2));
            Console.WriteLine("  Seccion " + a.Describe());
            Console.WriteLine("  Seccion " + b.Describe());
            Console.WriteLine("  Circulos por familia (" + name + "):  " + string.Join("  ", Families.All.Select(f => f + ": A-A " + a.CirclesOf(f) + " / B-B " + b.CirclesOf(f))));
            foreach (string w in a.Warnings.Concat(b.Warnings)) Console.WriteLine("  aviso seccion: " + w);
            if (aa != null)
                foreach (var kv in aa)
                    Check(a.CirclesOf(kv.Key) == kv.Value.circles && a.PolylinesOf(kv.Key) == kv.Value.lines,
                          "A-A " + kv.Key + ": " + kv.Value.circles + " circulos y " + kv.Value.lines + " lineas (" + a.CirclesOf(kv.Key) + " / " + a.PolylinesOf(kv.Key) + ")");
            if (bb != null)
                foreach (var kv in bb)
                    Check(b.CirclesOf(kv.Key) == kv.Value.circles && b.PolylinesOf(kv.Key) == kv.Value.lines,
                          "B-B " + kv.Key + ": " + kv.Value.circles + " circulos y " + kv.Value.lines + " lineas (" + b.CirclesOf(kv.Key) + " / " + b.PolylinesOf(kv.Key) + ")");
        }

        private static string Indent(string s) => "  " + s.Replace(Environment.NewLine, Environment.NewLine + "  ");

        /// <summary>Informe de choques del plan (tramo contra tramo, 3D, tolerancia 1 mm) y comprobacion de que no hay ninguno.</summary>
        private static ClashReport Clashes(BlockPlan p, string name)
        {
            ClashReport r = ClashCheck.Check(p, Mm(1));
            Console.WriteLine("  Informe de choques (" + name + "): " + r.Describe().Replace(Environment.NewLine, Environment.NewLine + "  "));
            Check(r.Clashes.Count == 0, "sin choques en " + name + " (" + r.Clashes.Count + ")");
            Check(r.UnexpectedContacts.Count == 0, "sin contactos no previstos en " + name + " (" + r.UnexpectedContacts.Count + ")");
            return r;
        }

        // =================================================================
        // Caso (a): canaleta solo en un lado
        // =================================================================
        private static void CaseA()
        {
            // murete de 150 en el lado v = 0, canaleta de 600 x 500 de profundidad a lo largo de u abierta por los dos extremos, plataforma en el resto
            var bottom = new[] { Box(0, 0, 4800, 3800) };
            var top = new[] { Box(0, 0, 4800, 150), Box(0, 750, 4800, 3800) };
            var floors = new[] { (new List<List<Pt>> { Box(0, 150, 4800, 750) }, Mm(800)) };
            BlockTopology t = BlockTopology.Build(bottom, top, floors, Mm(1300), Mm(300), Mm(2));
            Console.WriteLine("  " + t.DescribeDetailed().Replace(Environment.NewLine, Environment.NewLine + "  "));
            Check(t.Error == null, "topologia sin error: " + t.Error);
            Check(t.Recesses.Count == 1 && t.Recesses[0].Open, "1 canaleta abierta por un lado");
            Check(t.Platforms == 1 && t.Walls == 1, "1 plataforma + 1 murete recto (" + t.Platforms + " + " + t.Walls + ")");
            TopRegion wall = t.Regions.FirstOrDefault(r => r.Kind == RegionKind.Wall), plat = t.Regions.FirstOrDefault(r => r.Kind == RegionKind.Platform);
            if (wall != null)
            {
                Check(!wall.IsRing, "murete recto (sin hueco)");
                Check(wall.Edges.Count(e => e.Kind == EdgeKind.Exterior) == 3 && wall.Edges.Count(e => e.Kind == EdgeKind.Recess) == 1, "murete: 3 aristas exteriores (dos cortas y una larga) + 1 cara de foso");
            }
            if (plat != null)
                Check(plat.Edges.Count(e => e.Kind == EdgeKind.Exterior) == 3 && plat.Edges.Count(e => e.Kind == EdgeKind.Recess) == 1, "plataforma: 3 aristas exteriores + 1 cara de foso");
            Check(t.Recesses[0].Edges.Count(e => e.Kind == EdgeKind.Exterior) == 2, "las dos aristas abiertas de la canaleta cuentan como exteriores");

            AppConfig c = Cfg();
            BlockPlan p = BlockPlan.Build(t, c, Diam(c));
            Console.WriteLine("  " + p.Describe());
            foreach (string w in p.Warnings) Console.WriteLine("  aviso: " + w);
            Console.WriteLine(Indent(p.QuantityTable()));
            Check(p.Error == null, "plan sin error: " + p.Error);
            if (p.Error != null) return;
            Check(p.CountOf(Family.F4) == 37 && p.GroupsOf(Family.F4) == 1, "F4 solo en la cara de foso de la plataforma, fuera de la zona de patas de F1/F2 y entre las barras de F2: 37 barras, 1 conjunto (" + p.CountOf(Family.F4) + " / " + p.GroupsOf(Family.F4) + ")");
            var f4a = p.Bars.Where(b => b.Family == Family.F4).ToList();
            Check(f4a.Min(b => b.Points[0].U) > Mm(75 + 15.875 + 31.75 + 31.75 + 7.9), "el primer vertical de F4 queda por dentro de las patas de F1 y F2 (u=" + ToMm(f4a.Min(b => b.Points[0].U)) + ")");
            Check(p.CountOf(Family.F5) == 2 && p.GroupsOf(Family.F5) == 1, "F5 fromTop: 2 niveles en una cara (1213.5 y 1013.5; 813.5 queda bajo el fondo de la canaleta + recubrimiento) (" + p.CountOf(Family.F5) + ")");
            PlannedBar f5 = p.Bars.Where(b => b.Family == Family.F5).OrderBy(b => b.Points[0].Z).First();
            Near(f5.Points[0].Z, 1300 - 50 - 31.75 - 4.7625 - 200, "nivel inferior de F5 a 200 del superior");
            Near(f5.Length, 4800 - 2 * (75 + 15.875 + 15.875 + 4.7625 + 2), "F5 para antes de la zona de patas de F3 junto a las caras exteriores");
            Check(p.CountOf(Family.F6) == 38 + 2 + 1, "F6 con la regla de esquinas: 38 en el tramo largo + 2 en el testero que llega a la esquina + 1 en el que sale de ella (" + p.CountOf(Family.F6) + ")");
            var f6a = p.Bars.Where(b => b.Family == Family.F6).ToList();
            Check(f6a.Count(b => b.Points[0].U < Mm(60) && b.Points[0].V < Mm(60)) == 1 && f6a.Count(b => b.Points[0].U > Mm(4740) && b.Points[0].V < Mm(60)) == 1, "una sola vertical en cada esquina del murete recto");
            Check(p.CountOf(Family.F7) == 38, "F7 solo en el tramo largo (en los testeros la cara opuesta no es de foso): 38 (" + p.CountOf(Family.F7) + ")");
            Check(p.HairpinsSkipped == 3, "3 horquillas omitidas en los testeros (" + p.HairpinsSkipped + ")");
            Check(p.CountOf(Family.F8) == 6 && p.GroupsOf(Family.F8) == 1, "F8 fromTop: 6 niveles en el tramo largo; los testeros son mas cortos que la barra minima (" + p.CountOf(Family.F8) + ")");
            Check(p.Skipped > 0, "tramos cortos omitidos en los testeros (" + p.Skipped + ")");
            PlannedBar f2 = p.Bars.First(b => b.Family == Family.F2 && b.Layer == "u");
            Near(f2.Points[1].Z, 800 - 75 - 7.9375, "F2 a 75 bajo el fondo de la canaleta");
            // las patas de F1 en los extremos abiertos de la canaleta quedan bajo su fondo
            var f1Open = p.Bars.Where(b => b.Family == Family.F1 && b.Layer == "u" && b.Points[1].V > Mm(150) && b.Points[1].V < Mm(750)).ToList();
            Check(f1Open.Count > 0 && f1Open.All(b => b.Points.Count == 4 && b.Points[0].Z + Mm(8) < Mm(800)), "patas de F1 bajo la canaleta abierta (" + f1Open.Count + " barras)");
            Clashes(p, "canaleta en un lado");
            Sections(p, t, "canaleta",
                aa: new Dictionary<Family, (int, int)> { [Family.F4] = (0, 0), [Family.F5] = (0, 0), [Family.F6] = (0, 0), [Family.F7] = (0, 0), [Family.F8] = (0, 0) },
                bb: new Dictionary<Family, (int, int)> { [Family.F4] = (0, 1), [Family.F5] = (2, 0), [Family.F6] = (0, 1), [Family.F7] = (0, 1), [Family.F8] = (6, 0) });
            SectionCut bb = BlockSection.Cut(p, t, new SectionLine(false, Mm(2400)), Mm(2));
            Check(bb.Profile.Count == 3 && bb.Profile[0].Kind == "murete" && bb.Profile[1].Kind == "foso" && bb.Profile[2].Kind == "nucleo", "B-B: murete / canaleta / nucleo: " + bb.ProfileText());
            Near(bb.RecessDepth, 500, "profundidad de la canaleta en B-B");
        }

        // =================================================================
        // Caso (b): foso rectangular central sin murete
        // =================================================================
        private static void CaseB()
        {
            var bottom = new[] { Box(0, 0, 4000, 3000) };
            var top = new[] { Box(0, 0, 4000, 3000), Box(1250, 1000, 2750, 2000) };
            var floors = new[] { (new List<List<Pt>> { Box(1250, 1000, 2750, 2000) }, Mm(500)) };
            BlockTopology t = BlockTopology.Build(bottom, top, floors, Mm(1200), Mm(300), Mm(2));
            Console.WriteLine("  " + t.DescribeDetailed().Replace(Environment.NewLine, Environment.NewLine + "  "));
            Check(t.Error == null, "topologia sin error: " + t.Error);
            Check(t.Recesses.Count == 1 && !t.Recesses[0].Open, "1 foso cerrado");
            Check(t.Platforms == 1 && t.Walls == 0, "1 plataforma en anillo, 0 muretes (" + t.Platforms + " + " + t.Walls + ")");
            TopRegion plat = t.Regions.FirstOrDefault();
            if (plat != null)
            {
                Check(plat.IsRing, "plataforma en anillo");
                Near(plat.MinWidth, 1250, "ancho (diametro inscrito) de la plataforma en anillo: 1250", 1);
                Check(plat.Edges.Count(e => e.Kind == EdgeKind.Exterior) == 4 && plat.Edges.Count(e => e.Kind == EdgeKind.Recess) == 4, "4 exteriores + 4 caras de foso");
                Check(plat.Edges.Where(e => e.Kind == EdgeKind.Recess).All(e => !e.ConvexEnd), "las esquinas del foso son entrantes para la plataforma");
            }
            AppConfig c = Cfg();
            BlockPlan p = BlockPlan.Build(t, c, Diam(c));
            Console.WriteLine("  " + p.Describe());
            foreach (string w in p.Warnings) Console.WriteLine("  aviso: " + w);
            Console.WriteLine(Indent(p.QuantityTable()));
            Check(p.Error == null, "plan sin error: " + p.Error);
            if (p.Error != null) return;
            Check(p.CountOf(Family.F6) == 0 && p.CountOf(Family.F7) == 0 && p.CountOf(Family.F8) == 0, "sin F6 / F7 / F8 (no hay murete)");
            Check(p.GroupsOf(Family.F4) == 4 && p.GroupsOf(Family.F5) == 4, "F4 y F5 en las 4 caras del foso");
            // F4: posiciones desde d/2 de la esquina entrante: 1500 - 15.875 -> 12 huecos -> 13; 1000 - 15.875 -> 8 -> 9
            Check(p.CountOf(Family.F4) == 2 * 12 + 2 * 9, "F4 entre las barras de F2: 12 + 12 + 9 + 9 barras (" + p.CountOf(Family.F4) + ")");
            PlannedBar f4 = p.Bars.First(b => b.Family == Family.F4);
            Check(t.RecessAt(f4.Points[2].Plan) != null, "el pie de F4 queda bajo el foso central");
            // F5 por tramos prolongados mas alla de la esquina entrante: cruce con la barra contigua + traslape 400
            double o5 = 40 + 15.875 + 15.875 + 15.875 + 4.7625;
            PlannedBar f5long = p.Bars.Where(b => b.Family == Family.F5).OrderByDescending(b => b.Length).First();
            Near(f5long.Length, 1500 + 2 * (o5 + 400), "F5 largo: cara 1500 + 2 x (cruce + traslape 400)", 0.6);
            // F3 rodea el foso: hay barras u partidas en dos tramos a la altura del foso
            var f3uMid = p.Bars.Where(b => b.Family == Family.F3 && b.Layer == "u" && b.Points[1].V > Mm(1000) && b.Points[1].V < Mm(2000)).ToList();
            Check(f3uMid.Count > 0 && f3uMid.Count % 2 == 0 && f3uMid.All(b => b.Points.Count == 4), "F3 u partida en dos a la altura del foso, con patas en el foso y en el exterior (" + f3uMid.Count + ")");
            Check(f3uMid.Any(b => Math.Abs(ToMm(b.Points[2].U) - (1250 - 40 - 15.875 - 7.9375)) < 0.6), "F3 u termina con pata por dentro de F4 en la cara del foso");
            Clashes(p, "foso central");
            Check(f3uMid.All(b => b.Points.Count == 4), "todas las F3 u junto al foso llevan pata en los dos extremos, tambien las que acaban junto a su esquina");
            // conjuntos de F3 u: enteras bajo el foso, dos mitades a su altura, enteras encima
            Check(p.Groups.Count(g => g.Family == Family.F3 && g.Layer == "u") == 4, "F3 u en 4 conjuntos (" + p.Groups.Count(g => g.Family == Family.F3 && g.Layer == "u") + ")");
            Check(p.Groups.Count(g => g.Family == Family.F3 && g.Layer == "v") == 4, "F3 v en 4 conjuntos (" + p.Groups.Count(g => g.Family == Family.F3 && g.Layer == "v") + ")");
            Sections(p, t, "foso central",
                aa: new Dictionary<Family, (int, int)> { [Family.F4] = (0, 2), [Family.F5] = (6, 0), [Family.F6] = (0, 0) },
                bb: new Dictionary<Family, (int, int)> { [Family.F4] = (0, 2), [Family.F5] = (6, 0) });
            SectionCut aa = BlockSection.Cut(p, t, new SectionLine(true, Mm(1500)), Mm(2));
            Check(aa.Profile.Count == 3 && aa.Profile[0].Kind == "nucleo" && aa.Profile[1].Kind == "foso" && Math.Abs(ToMm(aa.Profile[1].Length) - 1500) < 1, "A-A: nucleo / foso 1500 / nucleo: " + aa.ProfileText());
            // anillo cerrado de F5
            c.F5.Shape = "ring";
            BlockPlan pr = BlockPlan.Build(t, c, Diam(c));
            Check(pr.Error == null && pr.CountOf(Family.F5) == 3 && pr.GroupsOf(Family.F5) == 1, "F5 en anillo fromTop: 3 niveles (1113.5, 913.5, 713.5), 1 conjunto (" + pr.CountOf(Family.F5) + " / " + pr.GroupsOf(Family.F5) + ") " + pr.Error);
            if (pr.Error == null) Clashes(pr, "foso central con F5 en anillo");
            if (pr.CountOf(Family.F5) > 0)
            {
                PlannedBar ring = pr.Bars.First(b => b.Family == Family.F5);
                Check(ring.Points.Count == 7, "anillo: 4 lados + vuelta al inicio + traslape (7 puntos, " + ring.Points.Count + ")");
                Near(ring.Length, 2 * (1500 + 2 * o5) + 2 * (1000 + 2 * o5) + 400, "longitud del anillo = perimetro desplazado + traslape 400", 1);
            }
        }

        // =================================================================
        // Casos extra
        // =================================================================
        private static void Extra()
        {
            double tol = Mm(2);
            // sin fosos: se remite a Zapatas
            BlockTopology t0 = BlockTopology.Build(new[] { Box(0, 0, 3000, 2000) }, new[] { Box(0, 0, 3000, 2000) }, new (List<List<Pt>>, double)[0], Mm(800), Mm(300), tol);
            Check(t0.Error != null && t0.Error.Contains("Zapatas"), "bloque sin fosos rechazado: " + t0.Error);
            // solo hueco pasante: tambien
            BlockTopology t1 = BlockTopology.Build(new[] { Box(0, 0, 3000, 2000), Box(1000, 500, 2000, 1500) }, new[] { Box(0, 0, 3000, 2000), Box(1000, 500, 2000, 1500) }, new (List<List<Pt>>, double)[0], Mm(800), Mm(300), tol);
            Check(t1.Error != null && t1.Error.Contains("huecos pasantes"), "solo hueco pasante: " + t1.Error);
            // cavidad cerrada: fondo bajo una cara del tope
            BlockTopology t2 = BlockTopology.Build(new[] { Box(0, 0, 3000, 2000) }, new[] { Box(0, 0, 3000, 2000) }, new[] { (new List<List<Pt>> { Box(1000, 500, 2000, 1500) }, Mm(400)) }, Mm(800), Mm(300), tol);
            Check(t2.Error != null && t2.Error.Contains("cavidad"), "cavidad cerrada rechazada: " + t2.Error);
            // paredes no verticales: el fondo no cubre el hueco del tope
            BlockTopology t3 = BlockTopology.Build(new[] { Box(0, 0, 3000, 2000) }, new[] { Box(0, 0, 3000, 2000), Box(1000, 500, 2000, 1500) }, new[] { (new List<List<Pt>> { Box(1200, 700, 1800, 1300) }, Mm(400)) }, Mm(800), Mm(300), tol);
            Check(t3.Error != null && t3.Error.Contains("no cubren"), "paredes de foso no verticales rechazadas: " + t3.Error);
            // escalon exterior (cara intermedia sin paredes): el tope es menor y la cara intermedia toca el borde
            BlockTopology t4 = BlockTopology.Build(new[] { Box(0, 0, 3000, 2000) }, new[] { Box(0, 0, 2000, 2000) }, new[] { (new List<List<Pt>> { Box(2000, 0, 3000, 2000) }, Mm(400)) }, Mm(800), Mm(300), tol);
            Check(t4.Error == null || t4.Error.Contains("escalon") || t4.Error.Contains("foso"), "escalon exterior: " + (t4.Error ?? "se admite como foso abierto con una pared"));

            // dos fosos de distinta profundidad: F2 bajo el mas profundo, F5 desde cada fondo
            var bottom = new[] { Box(0, 0, 4800, 3800) };
            var top = new[] { Box(0, 0, 4800, 3800), Box(500, 500, 2000, 3300), Box(2800, 500, 4300, 3300) };
            var floors = new[] { (new List<List<Pt>> { Box(500, 500, 2000, 3300) }, Mm(500)), (new List<List<Pt>> { Box(2800, 500, 4300, 3300) }, Mm(700)) };
            BlockTopology t5 = BlockTopology.Build(bottom, top, floors, Mm(1300), Mm(300), tol);
            Console.WriteLine("  " + t5.Describe());
            Check(t5.Error == null && t5.Recesses.Count == 2, "dos fosos: " + t5.Error);
            AppConfig c = Cfg();
            BlockPlan p5 = BlockPlan.Build(t5, c, Diam(c));
            Check(p5.Error == null, "plan con dos fosos sin error: " + p5.Error);
            if (p5.Error == null)
            {
                Near(p5.LayerZ["F2:u"], 500 - 75 - 7.9375, "F2 bajo el foso mas profundo (500)");
                var f5 = p5.Bars.Where(b => b.Family == Family.F5).ToList();
                var lv1 = f5.Where(b => b.Points[0].U < Mm(2400) && b.Points[1].U < Mm(2400) && Math.Abs(b.Normal.Z) > 0.5 && Math.Abs(b.Points[0].V - b.Points[1].V) > Mm(1)).Select(b => Math.Round(b.Points[0].Z * Ft)).Distinct().Count();
                var lv2 = f5.Where(b => b.Points[0].U > Mm(2400) && b.Points[1].U > Mm(2400) && Math.Abs(b.Points[0].V - b.Points[1].V) > Mm(1)).Select(b => Math.Round(b.Points[0].Z * Ft)).Distinct().Count();
                Check(lv1 == 4 && lv2 == 3, "F5 fromTop llega mas abajo en el foso profundo: 4 niveles en las caras del foso de 500 y 3 en las del de 700 (" + lv1 + " / " + lv2 + ")");
                Check(f5.Any(b => b.Face.Contains(" + ")), "los tramos colineales de los dos fosos sobre la misma cara se funden en una barra");
                Check(p5.CountOf(Family.F6) == 0 && p5.Walls() == 0, "sin murete: todo plataforma (" + t5.Walls + " muretes)");
                SectionCut aa = BlockSection.Cut(p5, t5, new SectionLine(true, Mm(1900)), tol);
                Check(aa.Levels.Count == 4 && aa.Levels.Any(l => l.Name.Contains("foso 1")) && aa.Levels.Any(l => l.Name.Contains("foso 2")), "niveles de los dos fondos en A-A (" + string.Join(", ", aa.Levels.Select(l => l.Name + " " + ToMm(l.Z))) + ")");
                Clashes(p5, "dos fosos");
            }

            // pie de F4 que choca con F2: se recoloca a media altura con aviso
            BlockTopology tp = PlanTopology();
            c = Cfg();
            c.F4.VerticalMm = 1242 - 410;   // el pie caeria a z = 410, dentro de F2 (393 - 425)
            BlockPlan pc = BlockPlan.Build(tp, c, Diam(c));
            Check(pc.Error == null, "plan con pie en F2 sin error: " + pc.Error);
            if (pc.Error == null)
            {
                PlannedBar f4 = pc.Bars.First(b => b.Family == Family.F4);
                Near(f4.Points[1].Z, 0.5 * ((75 + 2 * 15.875) + (500 - 75 - 2 * 15.875)), "pie recolocado a media altura entre F1 y F2", 1);
                Check(pc.Warnings.Any(w => w.Contains("F4") && w.Contains("media altura")), "aviso de recolocacion: " + string.Join(" | ", pc.Warnings));
                Clashes(pc, "pie de F4 recolocado");
            }
            // vertical corto: el pie quedaria dentro del foso, se alarga con aviso
            c = Cfg(); c.F4.VerticalMm = 500;
            pc = BlockPlan.Build(tp, c, Diam(c));
            Check(pc.Error == null && pc.Warnings.Any(w => w.Contains("se alarga")), "vertical de F4 alargado bajo el fondo del foso: " + string.Join(" | ", pc.Warnings));
            if (pc.Error == null) Near(pc.Bars.First(b => b.Family == Family.F4).Points[1].Z, 500 - 40 - 7.9375, "pie justo bajo el fondo del foso (cw + d/2)");

            // murete demasiado estrecho para la horquilla
            BlockTopology tn = PlanTopology(100);
            c = Cfg();
            BlockPlan pn = BlockPlan.Build(tn, c, Diam(c));
            Check(pn.Error != null && pn.Error.Contains("F7") && pn.Error.Contains("100"), "murete de 100 rechazado con el ancho en el mensaje: " + pn.Error);
            // murete de 130: caben las 4 barras (118) pero la horquilla no dobla (quedan 31 < 48)
            BlockTopology tn2 = PlanTopology(130);
            pn = BlockPlan.Build(tn2, c, Diam(c));
            Check(pn.Error != null && pn.Error.Contains("horquilla") && pn.Error.Contains("doblado"), "murete de 130: la horquilla no entra por el doblado: " + pn.Error);
            // sin F6 ni F8 la horquilla si entra en 130 (patas a cw + d/2 de cada cara: 130 - 89.5 = 40.5 < 47.6 -> tampoco); con 140 si
            BlockTopology tn3 = PlanTopology(140);
            c = Cfg(); c.F6.Enabled = false; c.F8.Enabled = false;
            pn = BlockPlan.Build(tn3, c, Diam(c));
            Check(pn.Error == null, "murete de 140 solo con horquillas: entra (" + pn.Error + ")");
            if (pn.Error == null) Clashes(pn, "murete de 140 solo con horquillas");

            // region mixta: plataforma + murete en U unidos en una sola cara del tope
            // una sola cara del tope: plataforma arriba y murete en U abajo, unidos por los testeros (caras sin solape)
            var topMixed = new[] { Box(0, 750, 4800, 3800), Box(0, 150, 150, 750), Box(4650, 150, 4800, 750), Box(0, 0, 4800, 150) };
            floors = new[] { (new List<List<Pt>> { Box(150, 150, 4650, 750) }, Mm(800)) };
            BlockTopology tm = BlockTopology.Build(bottom, topMixed, floors, Mm(1300), Mm(300), tol);
            Console.WriteLine("  " + tm.DescribeDetailed().Replace(Environment.NewLine, Environment.NewLine + "  "));
            Check(tm.Error == null, "region mixta sin error: " + tm.Error);
            Check(tm.Platforms == 1 && tm.Walls == 1, "region mixta partida en 1 plataforma + 1 murete en U (" + tm.Platforms + " + " + tm.Walls + ")");
            TopRegion wallU = tm.Regions.FirstOrDefault(r => r.Kind == RegionKind.Wall);
            if (wallU != null)
            {
                Near(wallU.MinWidth, 150, "ancho minimo del murete en U", 1);
                Check(wallU.Edges.Count(e => e.Kind == EdgeKind.Internal) == 2, "el murete en U tiene 2 aristas internas con la plataforma (" + wallU.Edges.Count(e => e.Kind == EdgeKind.Internal) + ")");
                Check(wallU.Edges.Count(e => e.Kind == EdgeKind.Recess) == 3, "y 3 caras de foso (" + wallU.Edges.Count(e => e.Kind == EdgeKind.Recess) + ")");
            }
            BlockPlan pm = BlockPlan.Build(tm, Cfg(), Diam(Cfg()));
            Check(pm.Error == null, "plan de la region mixta sin error: " + pm.Error);
            if (pm.Error == null)
            {
                Console.WriteLine("  " + pm.Describe());
                var f3 = pm.Bars.Where(b => b.Family == Family.F3 && b.Layer == "v").ToList();
                // las barras v de F3 que llegan al limite interno con el murete van rectas (sin pata) por ese extremo
                Check(f3.Any(b => b.Points.Count == 3), "F3 v recta en el limite interno con el murete y con pata en el exterior");
                Clashes(pm, "region mixta");
            }
        }

        private static int Walls(this BlockPlan p) => p.Topo.Walls;

        // =================================================================
        // Config y particion
        // =================================================================
        private static void ConfigAndPartition()
        {
            var c = new AppConfig();
            c.Normalize();
            Check(c.CoverBottomMm == 75 && c.CoverTopMm == 50 && c.CoverEdgeMm == 75 && c.CoverWallMm == 40 && c.WallMaxWidthMm == 300, "recubrimientos por defecto 75 / 50 / 75 / 40, murete max 300");
            Check(c.F1.U.BarTypeName == "5/8\"" && c.F1.U.SpacingMm == 125 && c.F1.LegUpMm == 300, "F1 por defecto 5/8\"@125, patas 300");
            Check(c.F2.Extent == "full" && c.F2.BelowRecessFloorMm == 75 && c.F2.AnchorageMm == 600 && c.F2.LegDownMm == 220, "F2 por defecto full, 75 bajo el fondo, anclaje 600, patas 220");
            Check(c.F3.LegDownMm == 340 && c.F4.VerticalMm == 1000 && c.F4.FootMm == 370, "F3 patas 340; F4 1000 / 370");
            Check(c.F5.BarTypeName == "3/8\"" && c.F5.SpacingMm == 200 && c.F5.Shape == "segments" && c.F5.LapMm == 400, "F5 3/8\"@200 por tramos, traslape 400");
            Check(c.F6.SpacingMm == 125 && c.F7.LegMm == 350 && c.F7.Placement == "aligned" && c.F8.Layers == 1 && c.F8.SpacingMm == 200, "F6 @125, F7 patas 350 alineada, F8 una capa @200");
            Check(c.F5.LayoutMode == "fromTop" && c.F8.LayoutMode == "fromTop" && c.F1.U.LayoutMode == "maxSpacing" && c.F4.LayoutMode == "maxSpacing" && c.F6.LayoutMode == "maxSpacing", "layoutMode: fromTop en F5 y F8, maxSpacing en el resto");
            c.F5.LayoutMode = "EXACT"; c.F1.V.LayoutMode = "raro"; c.F8.LayoutMode = "";
            c.Normalize();
            Check(c.F5.LayoutMode == "fromTop" && c.F1.V.LayoutMode == "maxSpacing" && c.F8.LayoutMode == "fromTop", "layoutMode normalizado (vacio = el de la familia)");
            Check(c.LevelReference == "shared" && c.PartitionTemplate == "BLQ-{marca}-{familia}", "niveles en coordenadas compartidas, particion BLQ-{marca}-{familia}");
            Check(c.BarTypesNeeded().Count == 11, "11 tipos de barra necesarios con todo activo (" + c.BarTypesNeeded().Count + ")");
            // claves exactas en el json
            string json = c.ToJson();
            Check(json.Contains("\"F1_bottomMesh\"") && json.Contains("\"F8_wallHoriz\"") && json.Contains("\"coverWallMm\"") && json.Contains("\"layers\": 1") && json.Contains("\"levelReference\": \"shared\"") && json.Contains("\"layoutMode\": \"fromTop\""), "json con las claves exactas (F1_bottomMesh, F8_wallHoriz, layers, levelReference, layoutMode)");
            // ida y vuelta por el config.json del repositorio
            string repoCfg = System.IO.Path.Combine("..", "config.json");
            if (!System.IO.File.Exists(repoCfg)) repoCfg = "config.json";
            Check(System.IO.File.Exists(repoCfg), "config.json del repositorio encontrado");
            if (System.IO.File.Exists(repoCfg))
            {
                AppConfig fromFile = AppConfig.Load(repoCfg);
                Check(fromFile.F4.FootMm == 370 && fromFile.F7.BarTypeName == "3/8\"" && fromFile.F8.Layers == 1 && fromFile.LevelReference == "shared" && fromFile.SectionViews.Scale == 20 && fromFile.F5.LayoutMode == "fromTop" && fromFile.F8.LayoutMode == "fromTop",
                      "config.json del repositorio se lee con los valores del plano (F5 y F8 fromTop)");
            }
            string tmp = System.IO.Path.GetTempFileName();
            c.F7.Placement = "STAGGERED"; c.F8.Layers = 5; c.F2.Extent = "Recess"; c.LevelReference = "base";
            c.Normalize();
            Check(c.F7.Placement == "staggered" && c.F8.Layers == 1 && c.F2.Extent == "recess" && c.LevelReference == "project", "normalizacion de valores raros");
            c.Save(tmp);
            AppConfig back = AppConfig.Load(tmp);
            Check(back.F7.Placement == "staggered" && back.F2.Extent == "recess" && back.CoverWallMm == 40, "ida y vuelta por json");
            AppConfig clone = c.Clone();
            clone.CoverTopMm = 99;
            Check(c.CoverTopMm != 99, "Clone es independiente");
            System.IO.File.Delete(tmp);
            Check(AppConfig.LevelReferenceName("shared").Contains("compartidas") && AppConfig.LevelReferenceName("project").Contains("base"), "nombres de la referencia de niveles");

            string s = PartitionName.Expand("BLQ-{marca}-{familia}", new PartitionName.Source { Mark = "FT-01", Family = "F4" });
            Check(s == "BLQ-FT-01-F4", "particion con marca y familia: " + s);
            s = PartitionName.Expand("BLQ-{marca}-{familia}", new PartitionName.Source { Mark = "", Id = "1234", Family = "F1" });
            Check(s == "BLQ-1234-F1", "particion sin marca usa el id: " + s);
            s = PartitionName.Expand("BLQ-{marca}-{familia}", new PartitionName.Source { Mark = "FT-01" });
            Check(s == "BLQ-FT-01", "comodin vacio sin separador huerfano: " + s);
            s = PartitionName.Expand("{familia}/{conjunto}", new PartitionName.Source { Family = "F7", SetName = "murete 1 tramo 2" });
            Check(s == "F7/murete 1 tramo 2", "familia y conjunto: " + s);
        }
    }
}
