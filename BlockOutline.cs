using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;

namespace BlockRebar
{
    /// <summary>
    /// El bloque visto en un sistema local concreto: u = direccion de las barras principales
    /// de las mallas, v = perpendicular (Z x u), origen en la esquina minima del contorno
    /// inferior a la cota de la cara inferior. Lleva la topologia pura (fosos, plataformas y
    /// muretes) en coordenadas locales y muestrea el perfil real del solido para las secciones.
    /// </summary>
    public sealed class BlockFrame
    {
        public BlockOutline Block;
        public XYZ Origin, DirU, DirV;
        public string Mode;
        public double AngleDeg;
        public BlockTopology Topology;

        public List<Solid> CheckSolids => Block.Solids;
        public double ZBottom => Block.ZBottom;
        public double ZTop => Block.ZTop;
        public double Thickness => Block.Thickness;
        public double Width => Topology.Width;
        public double Depth => Topology.Depth;

        public XYZ World(double u, double v, double z) =>
            new XYZ(Origin.X + DirU.X * u + DirV.X * v, Origin.Y + DirU.Y * u + DirV.Y * v, ZBottom + z);

        public XYZ World(P3 p) => World(p.U, p.V, p.Z);

        public Pt Local(XYZ p)
        {
            XYZ d = p - Origin;
            return new Pt(d.X * DirU.X + d.Y * DirU.Y, d.X * DirV.X + d.Y * DirV.Y);
        }

        public string LocalMm(XYZ p)
        {
            Pt l = Local(p);
            return "u=" + BlockOutline.ToMm(l.U) + " v=" + BlockOutline.ToMm(l.V) + " z=" + BlockOutline.ToMm(p.Z - ZBottom);
        }

        public string DirectionName
        {
            get
            {
                double deg = Math.Round(Math.Atan2(DirU.Y, DirU.X) * 180 / Math.PI);
                string name;
                switch (Mode)
                {
                    case "short": name = "lado corto"; break;
                    case "x": name = "X del proyecto"; break;
                    case "y": name = "Y del proyecto"; break;
                    case "angle": name = "angulo"; break;
                    default: name = "lado largo"; break;
                }
                return name + " (u a " + deg.ToString(CultureInfo.InvariantCulture) + " grados)";
            }
        }

        public string Describe() =>
            "u " + (Width * 0.3048).ToString("0.00", CultureInfo.InvariantCulture) + " x v " +
            (Depth * 0.3048).ToString("0.00", CultureInfo.InvariantCulture) + " m, u segun " + DirectionName;

        /// <summary>
        /// Cota del hormigon (desde la cara inferior) en el punto s de la linea de corte,
        /// muestreada del solido real con una recta vertical; null si ahi no hay hormigon.
        /// Es el perfil que dibujan las secciones en Revit (el topologico sirve de contraste).
        /// </summary>
        public double? SampledTop(SectionLine line, double s)
        {
            Pt p = line.Plan(s);
            double? z = Block.TopAt(World(p.U, p.V, 0));
            return z == null ? (double?)null : z.Value - ZBottom;
        }
    }

    /// <summary>
    /// Bloque deducido de la geometria real del elemento (cimentacion estructural cuyo solido
    /// ya lleva los fosos recortados): zBase = cara horizontal inferior mas baja, zTope = cara
    /// horizontal superior mas alta, fondos de foso = caras horizontales hacia arriba entre
    /// ambas, contorno inferior y regiones del tope. Rechaza con motivo las caras inclinadas,
    /// las caras hacia abajo a cota intermedia (cavidad o voladizo) y los fondos con hormigon
    /// encima. Todo en pies, coordenadas del modelo; el sistema local se calcula con Frame().
    /// Guarda un diagnostico de todas las caras para el modo "Analizar sin armar".
    /// </summary>
    public sealed class BlockOutline
    {
        public const double MmPerFt = 304.8;
        public static double Mm(double mm) => mm / MmPerFt;
        public static double ToMm(double ft) => Math.Round(ft * MmPerFt);
        private static string M2(double ft2) => (ft2 * 0.09290304).ToString("0.00", CultureInfo.InvariantCulture) + " m2";

        public Element Host;
        public List<Solid> Solids = new List<Solid>();
        public double ZTop, ZBottom;
        public double Thickness => ZTop - ZBottom;
        /// <summary>Anillos del contorno inferior (coordenadas x, y del modelo).</summary>
        public List<List<Pt>> BottomRings = new List<List<Pt>>();
        /// <summary>Anillos de las caras del tope.</summary>
        public List<List<Pt>> TopRings = new List<List<Pt>>();
        /// <summary>Anillos de cada fondo de foso con su cota absoluta.</summary>
        public List<(List<List<Pt>> rings, double z)> Floors = new List<(List<List<Pt>>, double)>();
        public int VerticalFaces, SlopedFaces;
        public string Note = "";
        /// <summary>Lineas del diagnostico (solidos, caras con cotas y areas, rechazos).</summary>
        public List<string> Diagnostics = new List<string>();

        public static string LastError;

        private readonly Dictionary<string, BlockFrame> _frames = new Dictionary<string, BlockFrame>();
        private double _tol, _wallMax;

        public string Describe() =>
            "canto " + ToMm(Thickness) + " mm, " + Floors.Count + (Floors.Count == 1 ? " cara de fondo" : " caras de fondo") + Note;

        // =================================================================
        // Lectura del solido
        // =================================================================
        public static BlockOutline Probe(Document doc, Element host, AppConfig cfg, List<string> diagnostics = null)
        {
            LastError = null;
            var s = new BlockOutline { Host = host, _tol = Mm(cfg.ToleranceMm), _wallMax = Mm(cfg.WallMaxWidthMm) };
            if (diagnostics != null) s.Diagnostics = diagnostics;
            double tol = s._tol;
            var d = s.Diagnostics;

            List<Solid> all = AllSolids(host);
            if (all.Count == 0) { LastError = "el elemento no tiene geometria solida"; d.Add("sin solidos"); return null; }
            s.Solids = all.Where(x => x.Volume >= 0.01 * all[0].Volume).ToList();
            d.Add("solidos: " + all.Count + " (" + string.Join(", ", all.Select(x => (x.Volume * 0.0283168).ToString("0.000", CultureInfo.InvariantCulture) + " m3")) + ")" +
                  (all.Count > s.Solids.Count ? ", " + (all.Count - s.Solids.Count) + " despreciable(s) descartado(s)" : ""));

            // --- clasificacion de caras ---
            var ups = new List<PlanarFace>();
            var downs = new List<PlanarFace>();
            var sloped = new List<(Face f, XYZ n, double zMin, double zMax)>();
            int vertical = 0, curvedVertical = 0;
            var curvedOther = new List<Face>();
            foreach (Solid sol in s.Solids)
                foreach (Face f in sol.Faces)
                {
                    if (f is PlanarFace pf)
                    {
                        double nz = pf.FaceNormal.Z;
                        if (nz > 0.999) ups.Add(pf);
                        else if (nz < -0.999) downs.Add(pf);
                        else if (Math.Abs(nz) < 0.001) vertical++;
                        else
                        {
                            BoundingBoxUV bb = pf.GetBoundingBox();
                            double z0 = double.MaxValue, z1 = double.MinValue;
                            foreach (CurveLoop loop in pf.GetEdgesAsCurveLoops())
                                foreach (Curve c in loop) { z0 = Math.Min(z0, Math.Min(c.GetEndPoint(0).Z, c.GetEndPoint(1).Z)); z1 = Math.Max(z1, Math.Max(c.GetEndPoint(0).Z, c.GetEndPoint(1).Z)); }
                            sloped.Add((pf, pf.FaceNormal, z0, z1));
                        }
                    }
                    else if (f is CylindricalFace cf && Math.Abs(cf.Axis.Normalize().Z) > 0.999) curvedVertical++;
                    else curvedOther.Add(f);
                }
            s.VerticalFaces = vertical + curvedVertical;
            s.SlopedFaces = sloped.Count + curvedOther.Count;

            if (downs.Count == 0) { LastError = "el bloque no tiene cara inferior horizontal; solo se arman bloques de base plana"; d.Add("sin caras horizontales hacia abajo"); return null; }
            if (ups.Count == 0) { LastError = "el bloque no tiene ninguna cara horizontal hacia arriba"; d.Add("sin caras horizontales hacia arriba"); return null; }
            s.ZBottom = downs.Min(f => f.Origin.Z);
            s.ZTop = ups.Max(f => f.Origin.Z);

            // --- diagnostico de caras ---
            d.Add("caras: " + downs.Count + " hacia abajo, " + ups.Count + " hacia arriba, " + vertical + " verticales planas, " + curvedVertical +
                  " curvas verticales, " + sloped.Count + " inclinadas, " + curvedOther.Count + " curvas no verticales");
            foreach (PlanarFace f in downs.OrderBy(f => f.Origin.Z))
                d.Add("  cara inferior (hacia abajo) a z interna " + ToMm(f.Origin.Z) + " mm (" + (f.Origin.Z - s.ZBottom <= tol ? "cara inferior del bloque, fija zBase" : "a " + ToMm(f.Origin.Z - s.ZBottom) + " mm sobre zBase") + "), area " + M2(f.Area));
            foreach (PlanarFace f in ups.OrderByDescending(f => f.Origin.Z))
            {
                string kind = s.ZTop - f.Origin.Z <= tol ? "tope del bloque" : (f.Origin.Z - s.ZBottom <= tol ? "a la cota de la base" : "fondo de foso, profundidad " + ToMm(s.ZTop - f.Origin.Z) + " mm");
                d.Add("  cara superior (hacia arriba) a z interna " + ToMm(f.Origin.Z) + " mm (" + kind + "), area " + M2(f.Area));
            }
            foreach ((Face f, XYZ n, double z0, double z1) in sloped)
                d.Add("  cara INCLINADA normal (" + n.X.ToString("0.00", CultureInfo.InvariantCulture) + ", " + n.Y.ToString("0.00", CultureInfo.InvariantCulture) + ", " +
                      n.Z.ToString("0.00", CultureInfo.InvariantCulture) + ") entre z " + ToMm(z0) + " y " + ToMm(z1) + " mm, area " + M2(f.Area));
            foreach (Face f in curvedOther) d.Add("  cara CURVA no vertical (" + f.GetType().Name + "), area " + M2(f.Area));
            d.Add("zBase (interna) = " + ToMm(s.ZBottom) + " mm, zTope = " + ToMm(s.ZTop) + " mm, canto " + ToMm(s.Thickness) + " mm");

            if (s.Thickness < Mm(100)) { LastError = "el bloque es demasiado delgado (" + ToMm(s.Thickness) + " mm)"; return null; }

            // --- rechazos por caras ---
            if (sloped.Count > 0 || curvedOther.Count > 0)
            {
                var kinds = new List<string>();
                if (sloped.Any(x => x.n.Z > 0.001)) kinds.Add("fondo inclinado (cara hacia arriba no horizontal)");
                if (sloped.Any(x => x.n.Z < -0.001)) kinds.Add("cara inferior inclinada");
                if (curvedOther.Count > 0) kinds.Add("cara curva no vertical");
                if (sloped.Any(x => Math.Abs(x.n.Z) <= 0.5)) kinds.Add("pared no vertical (de foso o exterior)");
                LastError = "hay " + (sloped.Count + curvedOther.Count) + " cara(s) inclinada(s): " + string.Join(", ", kinds.Distinct()) +
                            ". Solo se arman bloques con caras horizontales y verticales (ver el diagnostico)";
                return null;
            }
            List<PlanarFace> midDowns = downs.Where(f => f.Origin.Z - s.ZBottom > tol).ToList();
            if (midDowns.Count > 0)
            {
                LastError = midDowns.Count + " cara(s) horizontal(es) hacia abajo a cota intermedia (z=" + string.Join(", ", midDowns.Select(f => ToMm(f.Origin.Z - s.ZBottom))) +
                            " mm sobre la base): techo de una cavidad cerrada o voladizo; el bloque debe apoyar entero en su cara inferior";
                return null;
            }

            // --- contornos ---
            List<PlanarFace> bottomFaces = downs.Where(f => f.Origin.Z - s.ZBottom <= tol).ToList();
            List<PlanarFace> topFaces = ups.Where(f => s.ZTop - f.Origin.Z <= tol).ToList();
            if (!Rings(bottomFaces, tol, out List<List<Pt>> bottomRings, out string err)) { LastError = "no se pudo leer el contorno inferior (" + err + ")"; return null; }
            if (!Rings(topFaces, tol, out List<List<Pt>> topRings, out err)) { LastError = "no se pudo leer el contorno del tope (" + err + ")"; return null; }
            s.BottomRings = bottomRings;
            s.TopRings = topRings;

            // --- fondos de foso: caras hacia arriba a cota intermedia, sin hormigon encima ---
            foreach (PlanarFace f in ups.Where(f => f.Origin.Z - s.ZBottom > tol && s.ZTop - f.Origin.Z > tol))
            {
                if (!Rings(new List<PlanarFace> { f }, tol, out List<List<Pt>> rings, out err)) { LastError = "no se pudo leer un fondo de foso (" + err + ")"; return null; }
                // cavidad cerrada: sonda vertical desde un punto interior del fondo hasta el tope
                List<Pt> outer = rings.OrderByDescending(r => Math.Abs(Geometry2D.SignedArea(r))).First();
                Pt inner = Geometry2D.InnerPoint(outer);
                if (s.ConcreteAbove(inner, f.Origin.Z))
                {
                    LastError = "cavidad cerrada: la cara horizontal a z=" + ToMm(f.Origin.Z - s.ZBottom) + " mm sobre la base tiene hormigon encima (no es un foso abierto por arriba)";
                    return null;
                }
                s.Floors.Add((rings, f.Origin.Z));
            }
            return s;
        }

        /// <summary>True si hay hormigon del bloque en la vertical del punto por encima de la cota dada.</summary>
        private bool ConcreteAbove(Pt xy, double z)
        {
            XYZ a = new XYZ(xy.U, xy.V, z + Mm(5)), b = new XYZ(xy.U, xy.V, ZTop + 1);
            Line probe;
            try { probe = Line.CreateBound(a, b); } catch { return false; }
            foreach (Solid sol in Solids)
            {
                try
                {
                    var opt = new SolidCurveIntersectionOptions { ResultType = SolidCurveIntersectionMode.CurveSegmentsInside };
                    SolidCurveIntersection ix = sol.IntersectWithCurve(probe, opt);
                    if (ix != null && ix.SegmentCount > 0)
                        for (int i = 0; i < ix.SegmentCount; i++) if (ix.GetCurveSegment(i).Length > Mm(5)) return true;
                }
                catch { }
            }
            return false;
        }

        /// <summary>Anillos (x, y) de las caras dadas, con los bordes curvos teselados.</summary>
        private static bool Rings(List<PlanarFace> faces, double tol, out List<List<Pt>> rings, out string error)
        {
            rings = new List<List<Pt>>();
            error = null;
            var loops = new List<CurveLoop>();
            try { foreach (PlanarFace f in faces) loops.AddRange(f.GetEdgesAsCurveLoops()); }
            catch (Exception ex) { error = ex.Message; return false; }
            foreach (CurveLoop loop in loops)
            {
                var pts = new List<Pt>();
                foreach (Curve c in loop)
                {
                    if (c is Line)
                        pts.Add(new Pt(c.GetEndPoint(0).X, c.GetEndPoint(0).Y));
                    else
                    {
                        IList<XYZ> tess = c.Tessellate();
                        for (int i = 0; i + 1 < tess.Count; i++) pts.Add(new Pt(tess[i].X, tess[i].Y));
                        if (tess.Count == 1) pts.Add(new Pt(tess[0].X, tess[0].Y));
                    }
                }
                pts = Geometry2D.Simplify(pts, tol);
                if (pts.Count >= 3 && Math.Abs(Geometry2D.SignedArea(pts)) > tol * tol) rings.Add(pts);
            }
            if (rings.Count == 0) { error = "contorno vacio"; return false; }
            return true;
        }

        /// <summary>Cota superior del hormigon en la vertical del punto (x, y), o null si ahi no hay hormigon.</summary>
        public double? TopAt(XYZ xy)
        {
            XYZ a = new XYZ(xy.X, xy.Y, ZBottom - 1), b = new XYZ(xy.X, xy.Y, ZTop + 1);
            Line probe;
            try { probe = Line.CreateBound(a, b); } catch { return null; }
            double? best = null;
            foreach (Solid sol in Solids)
            {
                try
                {
                    var opt = new SolidCurveIntersectionOptions { ResultType = SolidCurveIntersectionMode.CurveSegmentsInside };
                    SolidCurveIntersection ix = sol.IntersectWithCurve(probe, opt);
                    if (ix == null) continue;
                    for (int i = 0; i < ix.SegmentCount; i++)
                    {
                        Curve seg = ix.GetCurveSegment(i);
                        double z = Math.Max(seg.GetEndPoint(0).Z, seg.GetEndPoint(1).Z);
                        if (best == null || z > best.Value) best = z;
                    }
                }
                catch { }
            }
            return best;
        }

        // =================================================================
        // Sistema local y topologia
        // =================================================================

        /// <summary>El bloque en el sistema local de la direccion pedida; se calcula una vez por combinacion.</summary>
        public BlockFrame Frame(string mode, double angleDeg) => Frame(mode, angleDeg, _wallMax);

        /// <summary>Igual, con el ancho maximo de murete (pies) que se quiera: la ventana lo cambia en vivo.</summary>
        public BlockFrame Frame(string mode, double angleDeg, double wallMaxFt)
        {
            mode = AppConfig.NormalizeDirection(mode);
            string key = (mode == "angle" ? mode + ":" + Math.Round(angleDeg, 3).ToString(CultureInfo.InvariantCulture) : mode) +
                         "|w" + Math.Round(wallMaxFt * MmPerFt).ToString(CultureInfo.InvariantCulture);
            if (_frames.TryGetValue(key, out BlockFrame cached)) return cached;

            var bottomWorld = new Outline2D(BottomRings, _tol);
            List<Pt> outerPts = bottomWorld.Outers.SelectMany(r => r).ToList();
            Pt e = Geometry2D.LongestEdgeDirection(bottomWorld.Outers);
            Pt perp = new Pt(-e.V, e.U);
            double le = Extent(outerPts, e), lp = Extent(outerPts, perp);
            Pt dir;
            switch (mode)
            {
                case "x": dir = new Pt(1, 0); break;
                case "y": dir = new Pt(0, 1); break;
                case "angle":
                    double a = angleDeg * Math.PI / 180;
                    dir = new Pt(Math.Cos(a), Math.Sin(a));
                    break;
                case "short": dir = le <= lp ? e : perp; break;
                default: dir = le >= lp ? e : perp; break;
            }
            var f = new BlockFrame { Block = this, Mode = mode, AngleDeg = angleDeg };
            f.DirU = new XYZ(dir.U, dir.V, 0).Normalize();
            f.DirV = XYZ.BasisZ.CrossProduct(f.DirU).Normalize();

            List<List<Pt>> local = BottomRings.Select(r => r.Select(p => ToLocal(p, f)).ToList()).ToList();
            double umin = local.SelectMany(r => r).Min(p => p.U), vmin = local.SelectMany(r => r).Min(p => p.V);
            f.Origin = new XYZ(f.DirU.X * umin + f.DirV.X * vmin, f.DirU.Y * umin + f.DirV.Y * vmin, ZBottom);
            Func<Pt, Pt> loc = p => { Pt l = ToLocal(p, f); return new Pt(l.U - umin, l.V - vmin); };

            List<List<Pt>> bottomLocal = BottomRings.Select(r => r.Select(loc).ToList()).ToList();
            List<List<Pt>> topLocal = TopRings.Select(r => r.Select(loc).ToList()).ToList();
            var floorsLocal = Floors.Select(fl => (fl.rings.Select(r => r.Select(loc).ToList()).ToList(), fl.z - ZBottom)).ToList();
            f.Topology = BlockTopology.Build(bottomLocal, topLocal, floorsLocal, Thickness, wallMaxFt, _tol);
            _frames[key] = f;
            return f;
        }

        private static Pt ToLocal(Pt world, BlockFrame f) =>
            new Pt(world.U * f.DirU.X + world.V * f.DirU.Y, world.U * f.DirV.X + world.V * f.DirV.Y);

        private static double Extent(List<Pt> pts, Pt dir)
        {
            double lo = double.MaxValue, hi = double.MinValue;
            foreach (Pt p in pts)
            {
                double t = p.U * dir.U + p.V * dir.V;
                lo = Math.Min(lo, t); hi = Math.Max(hi, t);
            }
            return hi - lo;
        }

        // =================================================================
        // Solidos
        // =================================================================
        private static Options GeometryOptions() =>
            new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false, IncludeNonVisibleObjects = false };

        /// <summary>Todos los solidos con volumen del elemento, de mayor a menor (coordenadas del modelo).</summary>
        public static List<Solid> AllSolids(Element e)
        {
            var list = new List<Solid>();
            GeometryElement ge;
            try { ge = e.get_Geometry(GeometryOptions()); }
            catch { return list; }
            if (ge == null) return list;
            void Scan(IEnumerable<GeometryObject> objs)
            {
                foreach (GeometryObject go in objs)
                {
                    if (go is Solid sol) { if (sol.Volume > 1e-9) list.Add(sol); }
                    else if (go is GeometryInstance gi) Scan(gi.GetInstanceGeometry());
                }
            }
            Scan(ge);
            return list.OrderByDescending(x => x.Volume).ToList();
        }

        internal static string TypeNameOf(Document doc, Element e)
        {
            ElementId tid = e.GetTypeId();
            Element t = (tid != null && tid != ElementId.InvalidElementId) ? doc.GetElement(tid) : null;
            return t?.Name ?? e.Name;
        }

        internal static string FamilyNameOf(Document doc, Element e)
        {
            ElementId tid = e.GetTypeId();
            var t = (tid != null && tid != ElementId.InvalidElementId) ? doc.GetElement(tid) as ElementType : null;
            return t?.FamilyName ?? e.Category?.Name ?? "";
        }

        /// <summary>
        /// Desfase que hay que sumar a una cota interna para expresarla en la referencia pedida:
        /// "shared" = coordenadas compartidas (punto de reconocimiento), "project" = punto base
        /// del proyecto, "internal" = cota interna.
        /// </summary>
        public static double ElevationOffset(Document doc, string reference)
        {
            try
            {
                switch (AppConfig.NormalizeLevelReference(reference))
                {
                    case "shared":
                        ProjectPosition pos = doc.ActiveProjectLocation.GetProjectPosition(XYZ.Zero);
                        return pos.Elevation;
                    case "project":
                        BasePoint bp = BasePoint.GetProjectBasePoint(doc);
                        return bp != null ? -bp.Position.Z : 0;
                    default:
                        return 0;
                }
            }
            catch { return 0; }
        }
    }
}
