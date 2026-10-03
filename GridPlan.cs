using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BlockRebar
{
    /// <summary>Categoria de un borde de foso para los angulos: lado largo o corto del foso, borde del nucleo o del murete.</summary>
    public enum AngleCategory { LongCore, LongWall, ShortCore, ShortWall }

    public static class AngleCategories
    {
        public static readonly AngleCategory[] All = { AngleCategory.LongCore, AngleCategory.LongWall, AngleCategory.ShortCore, AngleCategory.ShortWall };
        public static string Name(AngleCategory c)
        {
            switch (c)
            {
                case AngleCategory.LongCore: return "lado largo, borde de nucleo";
                case AngleCategory.LongWall: return "lado largo, borde de murete";
                case AngleCategory.ShortCore: return "lado corto, borde de nucleo";
                default: return "lado corto, borde de murete";
            }
        }
        public static string Key(AngleCategory c) => c.ToString().Substring(0, 1).ToLowerInvariant() + c.ToString().Substring(1);
    }

    /// <summary>Franja rectangular de un foso (coordenadas locales, pies) en la que se reparten las piezas.</summary>
    public sealed class GridStrip
    {
        public int Recess;
        public double U0, V0, U1, V1;
        /// <summary>Direccion larga de la franja (y de las piezas).</summary>
        public bool AlongU;
        public double Length => AlongU ? U1 - U0 : V1 - V0;
        public double Width => AlongU ? V1 - V0 : U1 - U0;
        public int Count;
        public double PieceLength, PieceWidth;
        public string Group = "";
        public List<GridPiece> Pieces = new List<GridPiece>();
        public string Describe() =>
            "franja " + Group + " foso " + (Recess + 1) + ": " + BlockPlan.ToMm(Length) + " x " + BlockPlan.ToMm(Width) + " mm " + (AlongU ? "segun u" : "segun v") +
            ", " + Count + " piezas de " + BlockPlan.ToMm(PieceLength) + " x " + BlockPlan.ToMm(PieceWidth) + " mm";
    }

    /// <summary>Una pieza de rejilla: centro en planta, largo (segun su direccion), ancho, cara superior a ZTop.</summary>
    public sealed class GridPiece
    {
        public int Recess;
        public Pt Center;
        public double Length, Width;
        public bool AlongU;
        public string Group = "";
        public double ZTop;
        public double Area => Length * Width;
        public double UMin => Center.U - 0.5 * (AlongU ? Length : Width);
        public double UMax => Center.U + 0.5 * (AlongU ? Length : Width);
        public double VMin => Center.V - 0.5 * (AlongU ? Width : Length);
        public double VMax => Center.V + 0.5 * (AlongU ? Width : Length);
    }

    /// <summary>Un angulo de borde: tramo A -> B sobre el borde del foso con el foso a la IZQUIERDA (Inward = Left(dir)).</summary>
    public sealed class AngleBar
    {
        public int Recess;
        public Pt A, B;
        public Pt Inward;
        public AngleCategory Category;
        public double ZTop;
        public string Note = "";
        public double Length => A.DistanceTo(B);
        public Pt Dir => Geometry2D.Unit(Geometry2D.Sub(B, A));
        public Pt Mid => new Pt(0.5 * (A.U + B.U), 0.5 * (A.V + B.V));
        public string Describe() =>
            "angulo " + AngleCategories.Name(Category) + " foso " + (Recess + 1) + ": L=" + BlockPlan.ToMm(Length) + " mm de (" + BlockPlan.ToMm(A.U) + ", " +
            BlockPlan.ToMm(A.V) + ") a (" + BlockPlan.ToMm(B.U) + ", " + BlockPlan.ToMm(B.V) + ")" + (Note != "" ? " [" + Note + "]" : "");
    }

    /// <summary>
    /// Rejillas y angulos de borde de los fosos, puro (sin Revit). Cada foso rectilineo se
    /// descompone en franjas rectangulares cortandolo en sus esquinas entrantes con lineas
    /// paralelas a su lado corto (asi las franjas de los lados cortos llevan las esquinas, P1,
    /// y las de los lados largos van entre ellas, P2). En cada franja: n = techo(L / largoMax),
    /// pieza = L / n - holgura, ancho = ancho - reduccion. En los dos bordes largos de cada
    /// franja van los angulos, clasificados por lado (largo / corto del foso) y por lo que hay
    /// al otro lado (nucleo / murete), con longitud fija centrada o por retiro.
    /// </summary>
    public sealed class GridPlan
    {
        public BlockTopology Topo;
        public GridsCfg Cfg;
        public GridTypeCfg Type;
        public double KgPerM;
        public List<GridStrip> Strips = new List<GridStrip>();
        public List<GridPiece> Pieces = new List<GridPiece>();
        public List<AngleBar> Angles = new List<AngleBar>();
        public List<string> Warnings = new List<string>();
        public string Error;

        private double _tol;
        private readonly HashSet<string> _warned = new HashSet<string>();
        private void Warn(string s) { if (_warned.Add(s)) Warnings.Add(s); }

        public double AngleLength => Angles.Sum(a => a.Length);
        public double AngleKg => AngleLength * 0.3048 * KgPerM;
        public int Bolts => Angles.Count * Math.Max(0, Cfg.Angles.BoltsPerAngle);
        public double GridArea => Pieces.Sum(p => p.Area);
        public double GridKg => GridArea * 0.09290304 * (Type?.KgPerM2 ?? 0);
        public IEnumerable<string> Groups => Strips.Select(s => s.Group).Distinct().OrderBy(g => g, StringComparer.Ordinal);
        public bool HasAngles => Cfg.Angles.Enabled && Angles.Count > 0;
        public bool HasGrids => Cfg.Mode != "off" && Pieces.Count > 0;

        private static string Num(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
        private static string Num1(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);
        private static string Num2(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        private static double Mm(double mm) => BlockPlan.Mm(mm);
        private static double ToMm(double ft) => BlockPlan.ToMm(ft);

        // =================================================================
        public static GridPlan Build(BlockTopology topo, GridsCfg cfg, double kgPerM, string typeName = null)
        {
            var p = new GridPlan { Topo = topo, Cfg = cfg, KgPerM = kgPerM > 0 ? kgPerM : cfg.Angles.KgPerMDefault, _tol = topo?.Tol ?? Mm(2) };
            p.Type = cfg.TypeNamed(string.IsNullOrWhiteSpace(typeName) ? cfg.DefaultType : typeName);
            try
            {
                if (topo == null || topo.Error != null) { p.Error = topo?.Error ?? "sin topologia"; return p; }
                if (topo.Recesses.Count == 0) { p.Error = "el bloque no tiene fosos"; return p; }
                foreach (Recess rc in topo.Recesses) p.BuildRecess(rc);
                p.NameGroups();
                p.TrimCorners();
            }
            catch (Exception ex) { p.Error = "no se pudieron repartir las rejillas: " + ex.Message; }
            return p;
        }

        // -----------------------------------------------------------------
        // Franjas
        // -----------------------------------------------------------------
        private void BuildRecess(Recess rc)
        {
            Region2D shape = rc.Shape;
            foreach (List<Pt> ring in shape.Rings())
                for (int i = 0; i < ring.Count; i++)
                {
                    Pt a = ring[i], b = ring[(i + 1) % ring.Count];
                    if (Math.Abs(a.U - b.U) > _tol && Math.Abs(a.V - b.V) > _tol)
                    {
                        Warn("foso " + (rc.Index + 1) + ": tiene bordes que no son paralelos a u o v; sin rejillas ni angulos en ese foso");
                        return;
                    }
                }
            double W = shape.Width, D = shape.Depth;
            bool cutU = W >= D - _tol;   // cortes u = cte (paralelos al lado corto) cuando u es el lado largo
            var cuts = new SortedSet<double>();
            cuts.Add(cutU ? shape.UMin : shape.VMin);
            cuts.Add(cutU ? shape.UMax : shape.VMax);
            foreach (Pt v in ReflexVertices(shape)) cuts.Add(cutU ? v.U : v.V);
            var coords = new List<double>();
            foreach (double c in cuts) if (coords.Count == 0 || c - coords[coords.Count - 1] > _tol) coords.Add(c);

            var rects = new List<(double u0, double v0, double u1, double v1)>();
            for (int i = 0; i + 1 < coords.Count; i++)
            {
                List<Pt> slab = cutU
                    ? new List<Pt> { new Pt(coords[i], shape.VMin - 1), new Pt(coords[i + 1], shape.VMin - 1), new Pt(coords[i + 1], shape.VMax + 1), new Pt(coords[i], shape.VMax + 1) }
                    : new List<Pt> { new Pt(shape.UMin - 1, coords[i]), new Pt(shape.UMax + 1, coords[i]), new Pt(shape.UMax + 1, coords[i + 1]), new Pt(shape.UMin - 1, coords[i + 1]) };
                foreach (Region2D comp in Poly2D.Intersection(new[] { shape }, new[] { new Region2D(slab) }, _tol))
                {
                    double bboxArea = comp.Width * comp.Depth;
                    if (comp.Holes.Count > 0 || Math.Abs(comp.Area - bboxArea) > _tol * 2 * (comp.Width + comp.Depth))
                    {
                        Warn("foso " + (rc.Index + 1) + ": un trozo de " + ToMm(comp.Width) + " x " + ToMm(comp.Depth) + " mm no es rectangular; sin rejillas en el");
                        continue;
                    }
                    if (comp.Width > _tol && comp.Depth > _tol) rects.Add((comp.UMin, comp.VMin, comp.UMax, comp.VMax));
                }
            }
            rects = MergeRects(rects, cutU);

            foreach ((double u0, double v0, double u1, double v1) r in rects.OrderBy(r => r.u0).ThenBy(r => r.v0))
            {
                var s = new GridStrip { Recess = rc.Index, U0 = r.u0, V0 = r.v0, U1 = r.u1, V1 = r.v1 };
                s.AlongU = (r.u1 - r.u0) >= (r.v1 - r.v0) - _tol;
                Strips.Add(s);
                if (Cfg.Mode != "off") Pieces.AddRange(Split(s));
                if (Cfg.Angles.Enabled) Angles.AddRange(StripAngles(s, rc, W >= D - _tol));
            }
        }

        /// <summary>Vertices entrantes de la region (angulo interior > 180): giros a la derecha del anillo exterior y esquinas convexas de los huecos.</summary>
        private static IEnumerable<Pt> ReflexVertices(Region2D shape)
        {
            foreach (Pt p in Reflex(shape.Outer, false)) yield return p;
            foreach (List<Pt> h in shape.Holes) foreach (Pt p in Reflex(h, true)) yield return p;
        }

        private static IEnumerable<Pt> Reflex(List<Pt> ring, bool hole)
        {
            List<Pt> r = Geometry2D.SignedArea(ring) < 0 ? Enumerable.Reverse(ring).ToList() : ring;   // CCW
            for (int i = 0; i < r.Count; i++)
            {
                Pt a = r[(i + r.Count - 1) % r.Count], b = r[i], c = r[(i + 1) % r.Count];
                double cross = Geometry2D.Cross(Geometry2D.Sub(b, a), Geometry2D.Sub(c, b));
                if (hole ? cross > 1e-12 : cross < -1e-12) yield return b;
            }
        }

        /// <summary>Une rectangulos contiguos (en la direccion de los cortes) con el mismo rango transversal.</summary>
        private List<(double u0, double v0, double u1, double v1)> MergeRects(List<(double u0, double v0, double u1, double v1)> rects, bool cutU)
        {
            var list = new List<(double u0, double v0, double u1, double v1)>(rects);
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int i = 0; i < list.Count && !merged; i++)
                    for (int j = i + 1; j < list.Count && !merged; j++)
                    {
                        var a = list[i]; var b = list[j];
                        bool ok = cutU
                            ? Math.Abs(a.v0 - b.v0) <= _tol && Math.Abs(a.v1 - b.v1) <= _tol && (Math.Abs(a.u1 - b.u0) <= _tol || Math.Abs(b.u1 - a.u0) <= _tol)
                            : Math.Abs(a.u0 - b.u0) <= _tol && Math.Abs(a.u1 - b.u1) <= _tol && (Math.Abs(a.v1 - b.v0) <= _tol || Math.Abs(b.v1 - a.v0) <= _tol);
                        if (!ok) continue;
                        list[i] = (Math.Min(a.u0, b.u0), Math.Min(a.v0, b.v0), Math.Max(a.u1, b.u1), Math.Max(a.v1, b.v1));
                        list.RemoveAt(j);
                        merged = true;
                    }
            }
            return list;
        }

        // -----------------------------------------------------------------
        // Piezas
        // -----------------------------------------------------------------
        private List<GridPiece> Split(GridStrip s)
        {
            var list = new List<GridPiece>();
            double L = s.Length, Wd = s.Width;
            double maxLen = Mm(Cfg.MaxLengthMm), clearance = Mm(Cfg.ClearanceMm), red = Mm(Cfg.WidthReductionMm);
            int n = Math.Max(1, (int)Math.Ceiling((L - _tol) / maxLen));   // con tolerancia: 3300 / 825 son 4 piezas, no 5
            double pieceLen = L / n - clearance, pieceWid = Wd - red;
            if (pieceLen <= Mm(20) || pieceWid <= Mm(20))
            {
                Warn("foso " + (s.Recess + 1) + ": franja de " + ToMm(L) + " x " + ToMm(Wd) + " mm demasiado pequena para rejillas");
                return list;
            }
            s.Count = n; s.PieceLength = pieceLen; s.PieceWidth = pieceWid;
            for (int i = 0; i < n; i++)
            {
                double t = (i + 0.5) * L / n;
                Pt c = s.AlongU ? new Pt(s.U0 + t, 0.5 * (s.V0 + s.V1)) : new Pt(0.5 * (s.U0 + s.U1), s.V0 + t);
                var piece = new GridPiece { Recess = s.Recess, Center = c, Length = pieceLen, Width = pieceWid, AlongU = s.AlongU, ZTop = Topo.ZTop };
                s.Pieces.Add(piece);
                list.Add(piece);
            }
            return list;
        }

        /// <summary>P1, P2... por longitud de franja descendente y luego por largo de pieza; las franjas con piezas iguales comparten nombre.</summary>
        private void NameGroups()
        {
            var keys = new List<(double len, double piece, double wid)>();
            foreach (GridStrip s in Strips.Where(s => s.Count > 0))
            {
                var k = (Math.Round(s.Length * 304.8), Math.Round(s.PieceLength * 304.8), Math.Round(s.PieceWidth * 304.8));
                if (!keys.Any(x => x.piece == k.Item2 && x.wid == k.Item3)) keys.Add(k);
            }
            keys = keys.OrderByDescending(k => k.len).ThenByDescending(k => k.piece).ThenByDescending(k => k.wid).ToList();
            foreach (GridStrip s in Strips.Where(s => s.Count > 0))
            {
                int idx = keys.FindIndex(k => k.piece == Math.Round(s.PieceLength * 304.8) && k.wid == Math.Round(s.PieceWidth * 304.8));
                s.Group = "P" + (idx + 1);
                foreach (GridPiece p in s.Pieces) p.Group = s.Group;
            }
        }

        // -----------------------------------------------------------------
        // Angulos
        // -----------------------------------------------------------------
        private enum Across { Core, Wall, Internal, Other }

        private List<AngleBar> StripAngles(GridStrip s, Recess rc, bool recessLongU)
        {
            var list = new List<AngleBar>();
            // los dos bordes largos de la franja, recorridos con la franja a la izquierda
            var borders = s.AlongU
                ? new[] { (new Pt(s.U0, s.V0), new Pt(s.U1, s.V0), new Pt(0, 1)), (new Pt(s.U1, s.V1), new Pt(s.U0, s.V1), new Pt(0, -1)) }
                : new[] { (new Pt(s.U1, s.V0), new Pt(s.U1, s.V1), new Pt(-1, 0)), (new Pt(s.U0, s.V1), new Pt(s.U0, s.V0), new Pt(1, 0)) };
            foreach ((Pt a, Pt b, Pt inward) in borders)
            {
                bool borderAlongU = Math.Abs(b.V - a.V) <= _tol;
                bool longSide = borderAlongU == recessLongU;
                Pt dir = Geometry2D.Unit(Geometry2D.Sub(b, a));
                double L = a.DistanceTo(b);
                // puntos de corte: vertices del foso proyectados dentro del borde
                var ts = new SortedSet<double> { 0, L };
                foreach (List<Pt> ring in rc.Shape.Rings())
                    foreach (Pt v in ring)
                    {
                        double t = Geometry2D.Dot(Geometry2D.Sub(v, a), dir);
                        if (t > _tol && t < L - _tol) ts.Add(t);
                    }
                var tl = new List<double>();
                foreach (double t in ts) if (tl.Count == 0 || t - tl[tl.Count - 1] > _tol) tl.Add(t);
                // clasificar cada tramo por lo que hay al otro lado y unir los iguales
                var segs = new List<(double t0, double t1, Across kind)>();
                double probe = Math.Max(3 * _tol, Mm(5));
                for (int i = 0; i + 1 < tl.Count; i++)
                {
                    Pt mid = Geometry2D.Add(a, Geometry2D.Scale(dir, 0.5 * (tl[i] + tl[i + 1])));
                    Pt outside = Geometry2D.Sub(mid, Geometry2D.Scale(inward, probe));
                    Across k = Classify(outside, rc);
                    if (segs.Count > 0 && segs[segs.Count - 1].kind == k) segs[segs.Count - 1] = (segs[segs.Count - 1].t0, tl[i + 1], k);
                    else segs.Add((tl[i], tl[i + 1], k));
                }
                foreach ((double t0, double t1, Across kind) in segs)
                {
                    if (kind == Across.Internal || kind == Across.Other) continue;
                    AngleCategory cat = longSide ? (kind == Across.Core ? AngleCategory.LongCore : AngleCategory.LongWall)
                                                 : (kind == Across.Core ? AngleCategory.ShortCore : AngleCategory.ShortWall);
                    double len = t1 - t0;
                    AngleCategoryCfg cc = Cfg.Angles.Of(cat);
                    double clear = Mm(Cfg.Angles.CornerClearanceMm);
                    double la;
                    string note = "";
                    if (cc.Mode == "fixedLength" && Mm(cc.LengthMm) <= len - 2 * clear + 1e-9) la = Mm(cc.LengthMm);
                    else
                    {
                        if (cc.Mode == "fixedLength")
                            Warn("foso " + (rc.Index + 1) + ", " + AngleCategories.Name(cat) + ": el angulo de " + Num(cc.LengthMm) + " mm no cabe en un borde de " + ToMm(len) +
                                 " mm; se usa retiro de " + Num(cc.SetbackMm) + " mm");
                        la = len - 2 * Mm(cc.SetbackMm);
                        note = cc.Mode == "fixedLength" ? "retiro " + Num(cc.SetbackMm) + " (no cabe " + Num(cc.LengthMm) + ")" : "retiro " + Num(cc.SetbackMm);
                    }
                    if (la < Mm(200)) { Warn("foso " + (rc.Index + 1) + ", " + AngleCategories.Name(cat) + ": borde de " + ToMm(len) + " mm demasiado corto para un angulo"); continue; }
                    double tc = 0.5 * (t0 + t1);
                    list.Add(new AngleBar
                    {
                        Recess = rc.Index, Category = cat, Inward = inward, ZTop = Topo.ZTop, Note = note,
                        A = Geometry2D.Add(a, Geometry2D.Scale(dir, tc - 0.5 * la)),
                        B = Geometry2D.Add(a, Geometry2D.Scale(dir, tc + 0.5 * la))
                    });
                }
            }
            return list;
        }

        private Across Classify(Pt outside, Recess rc)
        {
            TopRegion reg = Topo.RegionAt(outside);
            if (reg != null) return reg.Kind == RegionKind.Platform ? Across.Core : Across.Wall;
            Recess other = Topo.RecessAt(outside);
            if (other != null) return ReferenceEquals(other, rc) || other.Index == rc.Index ? Across.Internal : Across.Other;
            if (!Topo.InBody(outside))
            {
                Warn("foso " + (rc.Index + 1) + ": un borde da al exterior (canaleta abierta); se trata como borde de murete");
                return Across.Wall;
            }
            return Across.Other;
        }

        /// <summary>Angulos que se tocarian (a menos de la holgura): se recortan por el extremo que choca y se avisa.</summary>
        private void TrimCorners()
        {
            double clear = Mm(Cfg.Angles.CornerClearanceMm);
            if (clear <= 0) return;
            for (int i = 0; i < Angles.Count; i++)
                for (int j = 0; j < Angles.Count; j++)
                {
                    if (i == j) continue;
                    AngleBar a = Angles[i], b = Angles[j];
                    bool changed = false;
                    if (Geometry2D.DistanceToSegment(a.B, b.A, b.B, out _) < clear - 1e-9) { a.B = Shrink(a.A, a.B, b, clear); changed = true; }
                    if (Geometry2D.DistanceToSegment(a.A, b.A, b.B, out _) < clear - 1e-9) { a.A = Shrink(a.B, a.A, b, clear); changed = true; }
                    if (changed)
                    {
                        a.Note = (a.Note != "" ? a.Note + "; " : "") + "recortado en esquina";
                        Warn("foso " + (a.Recess + 1) + ": dos angulos se tocaban en una esquina; se recortan con " + Num(Cfg.Angles.CornerClearanceMm) + " mm de holgura");
                    }
                }
        }

        /// <summary>Acerca el extremo e hacia o hasta que quede a la holgura del otro angulo (biseccion).</summary>
        private static Pt Shrink(Pt o, Pt e, AngleBar other, double clear)
        {
            double lo = 0, hi = 1;   // fraccion de o->e; 1 = extremo actual (choca), 0 = origen
            for (int k = 0; k < 40; k++)
            {
                double m = 0.5 * (lo + hi);
                Pt p = Geometry2D.Add(o, Geometry2D.Scale(Geometry2D.Sub(e, o), m));
                if (Geometry2D.DistanceToSegment(p, other.A, other.B, out _) < clear) hi = m; else lo = m;
            }
            return Geometry2D.Add(o, Geometry2D.Scale(Geometry2D.Sub(e, o), lo));
        }

        // =================================================================
        // Descripcion y metrado
        // =================================================================
        public string Describe()
        {
            if (Error != null) return "SIN REJILLAS: " + Error;
            var parts = new List<string>();
            if (Cfg.Angles.Enabled)
            {
                var byLen = Angles.GroupBy(a => Math.Round(a.Length * 304.8)).OrderByDescending(g => g.Key).Select(g => g.Count() + " x " + Num(g.Key));
                parts.Add(Angles.Count + " angulos (" + string.Join(", ", byLen) + ") = " + Num2(AngleLength * 0.3048) + " m, " + Num1(AngleKg) + " kg, " + Bolts + " pernos");
            }
            if (Cfg.Mode != "off")
            {
                var groups = Groups.Select(g =>
                {
                    GridStrip s = Strips.First(x => x.Group == g);
                    int n = Pieces.Count(p => p.Group == g);
                    return n + " " + g + " " + ToMm(s.PieceLength) + " x " + ToMm(s.PieceWidth);
                });
                parts.Add("rejillas " + (Type?.Name ?? "") + (Cfg.Mode == "countOnly" ? " (solo informe)" : "") + ": " + string.Join(", ", groups) + "; " + Num2(GridArea * 0.09290304) + " m2, " + Num1(GridKg) + " kg");
            }
            return string.Join("; ", parts);
        }

        /// <summary>Metrado de ancho fijo: angulos por longitud, pernos, piezas por grupo con area y peso.</summary>
        public string QuantityTable()
        {
            var lines = new List<string>();
            if (Error != null) { lines.Add("SIN REJILLAS: " + Error); return string.Join(Environment.NewLine, lines); }
            if (Cfg.Angles.Enabled)
            {
                lines.Add(string.Format("{0,-34} {1,6} {2,10} {3,10} {4,8}", "Angulos (" + Num2(KgPerM) + " kg/m)", "uds", "L ud (mm)", "L total(m)", "kg"));
                foreach (var g in Angles.GroupBy(a => a.Category).OrderBy(g => (int)g.Key))
                    foreach (var gl in g.GroupBy(a => Math.Round(a.Length * 304.8)).OrderByDescending(x => x.Key))
                        lines.Add(string.Format("{0,-34} {1,6} {2,10} {3,10} {4,8}", AngleCategories.Name(g.Key), gl.Count(), Num(gl.Key),
                            Num2(gl.Sum(a => a.Length) * 0.3048), Num1(gl.Sum(a => a.Length) * 0.3048 * KgPerM)));
                lines.Add(string.Format("{0,-34} {1,6} {2,10} {3,10} {4,8}", "TOTAL angulos", Angles.Count, "", Num2(AngleLength * 0.3048), Num1(AngleKg)));
                lines.Add("Pernos de expansion 1/2\": " + Bolts + " (" + Cfg.Angles.BoltsPerAngle + " por angulo, solo contados)");
            }
            if (Cfg.Mode != "off")
            {
                lines.Add(string.Format("{0,-34} {1,6} {2,12} {3,9} {4,8}", "Rejillas " + (Type?.Name ?? "") + " (" + Num1(Type?.KgPerM2 ?? 0) + " kg/m2)", "piezas", "pieza (mm)", "m2", "kg"));
                foreach (string g in Groups)
                {
                    GridStrip s = Strips.First(x => x.Group == g);
                    var ps = Pieces.Where(p => p.Group == g).ToList();
                    double area = ps.Sum(p => p.Area) * 0.09290304;
                    lines.Add(string.Format("{0,-34} {1,6} {2,12} {3,9} {4,8}", g + " (" + (Type?.Designation ?? "") + ", alto " + Num(Type?.HeightMm ?? 0) + ")", ps.Count,
                        ToMm(s.PieceLength) + " x " + ToMm(s.PieceWidth), Num2(area), Num1(area * (Type?.KgPerM2 ?? 0))));
                }
                lines.Add(string.Format("{0,-34} {1,6} {2,12} {3,9} {4,8}", "TOTAL rejillas", Pieces.Count, "", Num2(GridArea * 0.09290304), Num1(GridKg)));
            }
            return string.Join(Environment.NewLine, lines);
        }
    }
}
