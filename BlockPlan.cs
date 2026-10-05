using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BlockRebar
{
    /// <summary>Punto 3D en coordenadas locales del bloque (u, v en planta; z desde la cara inferior), en pies.</summary>
    public struct P3
    {
        public double U, V, Z;
        public P3(double u, double v, double z) { U = u; V = v; Z = z; }
        public P3(Pt p, double z) { U = p.U; V = p.V; Z = z; }
        public Pt Plan => new Pt(U, V);
        public static P3 operator +(P3 a, P3 b) => new P3(a.U + b.U, a.V + b.V, a.Z + b.Z);
        public static P3 operator -(P3 a, P3 b) => new P3(a.U - b.U, a.V - b.V, a.Z - b.Z);
        public static P3 operator *(P3 a, double k) => new P3(a.U * k, a.V * k, a.Z * k);
        public double Dot(P3 o) => U * o.U + V * o.V + Z * o.Z;
        public double Length => Math.Sqrt(U * U + V * V + Z * Z);
        public double DistanceTo(P3 o) => (this - o).Length;
        public static readonly P3 Up = new P3(0, 0, 1);
        public static readonly P3 AxisU = new P3(1, 0, 0);
        public static readonly P3 AxisV = new P3(0, 1, 0);
        public override string ToString() => "(" + BlockPlan.ToMm(U) + ", " + BlockPlan.ToMm(V) + ", " + BlockPlan.ToMm(Z) + ")";
    }

    /// <summary>Diametro y diametros de doblado (pies) de un tipo de barra, con su etiqueta del plano ("5/8\"").</summary>
    public sealed class FamilyDiam
    {
        public double D;
        /// <summary>Diametro interior minimo de doblado estandar (pies); 0 = desconocido.</summary>
        public double BendInside;
        /// <summary>Diametro interior minimo de doblado de estribo / horquilla (pies); 0 = desconocido (se usa el estandar).</summary>
        public double TieBendInside;
        public string Label = "";
        public double TieBend => TieBendInside > 0 ? TieBendInside : (BendInside > 0 ? BendInside : 6 * D);
    }

    /// <summary>Diametros resueltos por familia (y capa u / v en las mallas).</summary>
    public sealed class PlanDiameters
    {
        private readonly Dictionary<string, FamilyDiam> _d = new Dictionary<string, FamilyDiam>();
        private static string Key(Family f, string layer) => f + ":" + (layer ?? "");

        public void Set(Family f, string layer, double dFt, string label, double bendInsideFt = 0, double tieBendInsideFt = 0) =>
            _d[Key(f, layer)] = new FamilyDiam { D = dFt, Label = label ?? "", BendInside = bendInsideFt, TieBendInside = tieBendInsideFt };

        public FamilyDiam Get(Family f, string layer = "") => _d.TryGetValue(Key(f, layer), out FamilyDiam v) ? v : null;
        public bool Has(Family f, string layer = "") => _d.ContainsKey(Key(f, layer));
        public double D(Family f, string layer = "") => Get(f, layer)?.D ?? 0;
        public string Label(Family f, string layer = "") => Get(f, layer)?.Label ?? "";
    }

    /// <summary>
    /// Una barra planificada: polilinea en coordenadas locales (tramo recto y patas como
    /// tramos; Revit anade los radios), diametro y direccion del array (normal al plano de
    /// la polilinea). Las barras iguales equiespaciadas a lo largo de Normal forman un conjunto.
    /// </summary>
    public sealed class PlannedBar
    {
        public Family Family;
        /// <summary>"u" / "v" en las mallas, "" en las demas.</summary>
        public string Layer = "";
        public List<P3> Points = new List<P3>();
        public double D;
        /// <summary>Direccion (unitaria) a lo largo de la cual se repite el conjunto.</summary>
        public P3 Normal;
        /// <summary>Cara o region de la que sale la barra (para agrupar, describir y etiquetar).</summary>
        public string Face = "";
        public int RegionIndex = -1;
        /// <summary>Separacion nominal de la familia (pies), la que se escribe en la etiqueta ("@125"); el paso real del conjunto es menor o igual.</summary>
        public double NominalSpacing;
        /// <summary>Arista (cara) de la que sale la barra en las familias de cara (F4...F8); null en las mallas.</summary>
        public RegionEdge Edge;

        public P3 First => Points[0];
        public bool IsStraight => Points.Count == 2;
        public bool HasLegs => Points.Count > 2;
        public double ZMin => Points.Min(p => p.Z);
        public double ZMax => Points.Max(p => p.Z);
        public double Along(P3 n) => First.Dot(n);

        public double Length
        {
            get
            {
                double l = 0;
                for (int i = 0; i + 1 < Points.Count; i++) l += Points[i].DistanceTo(Points[i + 1]);
                return l;
            }
        }

        /// <summary>
        /// Misma familia, capa, diametro y normal, y la otra barra es una traslacion de esta a
        /// lo largo de la normal (los puntos coinciden al quitar la componente segun la normal):
        /// candidata al mismo conjunto. Dos mitades de una barra partida por un foso no lo son.
        /// </summary>
        public bool SameShape(PlannedBar o, double tol)
        {
            if (Family != o.Family || Layer != o.Layer || Math.Abs(D - o.D) > 1e-9 || Points.Count != o.Points.Count) return false;
            if (Normal.Dot(o.Normal) < 1 - 1e-6) return false;
            for (int i = 0; i < Points.Count; i++)
            {
                P3 a = Points[i] - Normal * Points[i].Dot(Normal);
                P3 b = o.Points[i] - o.Normal * o.Points[i].Dot(o.Normal);
                if (a.DistanceTo(b) > tol) return false;
            }
            return true;
        }

        public string Describe() =>
            Families.Code(Family) + (Layer != "" ? "(" + Layer + ")" : "") + " Ø" + BlockPlan.Dia(D) + " L=" + BlockPlan.ToMm(Length) + " mm" +
            (HasLegs ? " (" + (Points.Count - 1) + " tramos)" : "") + " desde " + Points[0] + (Face != "" ? " [" + Face + "]" : "");
    }

    /// <summary>Barras iguales y equiespaciadas: un conjunto (array) de Revit.</summary>
    public sealed class BarGroup
    {
        public List<PlannedBar> Bars = new List<PlannedBar>();
        /// <summary>Separacion entre barras del conjunto (pies); 0 si es una sola.</summary>
        public double Spacing;
        public PlannedBar First => Bars[0];
        public int Count => Bars.Count;
        public Family Family => First.Family;
        public string Layer => First.Layer;
        public P3 Normal => First.Normal;
        public string Face => First.Face;
        public double D => First.D;
    }

    /// <summary>
    /// Armado completo del bloque en coordenadas locales: las ocho familias F1...F8 como
    /// polilineas, con el apilado de capas por cara, las comprobaciones de cabida (horquilla
    /// del murete, pie de F4, patas) y la agrupacion en conjuntos. Pura (sin Revit): la
    /// misma clase la usan la lamina (para dibujar) y el generador (para crear las barras).
    /// </summary>
    public sealed class BlockPlan
    {
        public const double MmPerFt = 304.8;
        public static double Mm(double mm) => mm / MmPerFt;
        public static double ToMm(double ft) => Math.Round(ft * MmPerFt);
        public static string Dia(double ft) => (ft * MmPerFt).ToString("0.#", CultureInfo.InvariantCulture);
        private static string Num(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);

        public BlockTopology Topo;
        public AppConfig Cfg;
        public PlanDiameters Diam;
        public double Thickness;
        public double Cb, Ct, Ce, Cw;

        public List<PlannedBar> Bars = new List<PlannedBar>();
        public List<BarGroup> Groups = new List<BarGroup>();
        /// <summary>Cota de cada capa colocada (clave "F1:u", "F3:v", "F4"...), para la lamina.</summary>
        public Dictionary<string, double> LayerZ = new Dictionary<string, double>();
        public List<string> Warnings = new List<string>();
        public string Error;
        /// <summary>Tramos demasiado cortos omitidos.</summary>
        public int Skipped;
        /// <summary>Posiciones de F7 omitidas porque la cara opuesta no es de foso.</summary>
        public int HairpinsSkipped;

        private double _tol, _minLen;
        private readonly HashSet<string> _warned = new HashSet<string>();
        /// <summary>Posiciones (t a lo largo de cada arista exterior de murete) de F6, para alinear F7.</summary>
        private readonly Dictionary<RegionEdge, List<double>> _f6Positions = new Dictionary<RegionEdge, List<double>>();
        private readonly Dictionary<string, Outline2D> _clamps = new Dictionary<string, Outline2D>();
        /// <summary>Rango admisible (t0, t1) a lo largo de cada cara por familia de cara, para la comprobacion de separaciones.</summary>
        public Dictionary<(Family, RegionEdge), (double t0, double t1)> FaceRanges = new Dictionary<(Family, RegionEdge), (double, double)>();

        // cotas de las mallas
        private double _zF1u, _zF1v, _f1Top, _zF2u, _zF2v, _f2Bot, _f2Top, _zF3u, _zF3v, _f3Bot;

        public int CountOf(Family f) => Bars.Count(b => b.Family == f);
        public int GroupsOf(Family f) => Groups.Count(g => g.Family == f);
        public double LengthOf(Family f) => Bars.Where(b => b.Family == f).Sum(b => b.Length);
        public IEnumerable<Family> UsedFamilies => Bars.Select(b => b.Family).Distinct().OrderBy(f => (int)f);
        public static double KgPerM(double dFt) { double mm = dFt * MmPerFt; return Math.PI / 4 * mm * mm / 1e6 * 7850; }
        public double WeightOf(Family f) => Bars.Where(b => b.Family == f).Sum(b => KgPerM(b.D) * b.Length * 0.3048);
        public double TotalWeight => Bars.Sum(b => KgPerM(b.D) * b.Length * 0.3048);

        /// <summary>Peso (kg) por diametro (clave: etiqueta del tipo o diametro en mm).</summary>
        public Dictionary<string, double> WeightByDiameter()
        {
            var w = new Dictionary<string, double>();
            foreach (PlannedBar b in Bars)
            {
                string key = Diam.Label(b.Family, b.Layer);
                if (string.IsNullOrEmpty(key)) key = "Ø" + Dia(b.D);
                w[key] = (w.TryGetValue(key, out double prev) ? prev : 0) + KgPerM(b.D) * b.Length * 0.3048;
            }
            return w;
        }

        private void Warn(string s) { if (_warned.Add(s)) Warnings.Add(s); }

        // =================================================================
        // Descripcion
        // =================================================================

        public string Describe()
        {
            if (Error != null) return "SIN ARMAR: " + Error;
            return string.Join(", ", UsedFamilies.Select(f => Families.Code(f) + " " + CountOf(f) + "/" + GroupsOf(f))) +
                   "; " + Bars.Count + " barras en " + Groups.Count + " conjuntos, " + Num(TotalWeight) + " kg" +
                   (Skipped > 0 ? " (" + Skipped + " tramos cortos omitidos)" : "");
        }

        /// <summary>Tabla de cantidades por familia y peso por diametro (texto de ancho fijo).</summary>
        public string QuantityTable()
        {
            var lines = new List<string>();
            lines.Add(string.Format("{0,-4} {1,-42} {2,6} {3,6} {4,-7} {5,10} {6,10} {7,9}", "Fam", "familia", "barras", "conj.", "tipo", "L barra", "L total", "peso"));
            lines.Add(string.Format("{0,-4} {1,-42} {2,6} {3,6} {4,-7} {5,10} {6,10} {7,9}", "", "", "", "", "", "(mm)", "(m)", "(kg)"));
            foreach (Family f in UsedFamilies)
            {
                var fb = Bars.Where(b => b.Family == f).ToList();
                foreach (string layer in fb.Select(b => b.Layer).Distinct())
                {
                    var lb = fb.Where(b => b.Layer == layer).ToList();
                    double lmin = lb.Min(b => b.Length), lmax = lb.Max(b => b.Length);
                    string lbar = Math.Abs(lmax - lmin) <= _tol ? ToMm(lmin).ToString(CultureInfo.InvariantCulture) : ToMm(lmin) + "-" + ToMm(lmax);
                    lines.Add(string.Format("{0,-4} {1,-42} {2,6} {3,6} {4,-7} {5,10} {6,10} {7,9}",
                        Families.Code(f) + (layer != "" ? layer : ""), Families.Name(f) + (layer != "" ? " (" + layer + ")" : ""),
                        lb.Count, Groups.Count(g => g.Family == f && g.Layer == layer), Diam.Label(f, layer),
                        lbar, (lb.Sum(b => b.Length) * 0.3048).ToString("0.0", CultureInfo.InvariantCulture),
                        lb.Sum(b => KgPerM(b.D) * b.Length * 0.3048).ToString("0.0", CultureInfo.InvariantCulture)));
                }
            }
            lines.Add(string.Format("{0,-4} {1,-42} {2,6} {3,6} {4,-7} {5,10} {6,10} {7,9}", "", "TOTAL", Bars.Count, Groups.Count, "", "",
                (Bars.Sum(b => b.Length) * 0.3048).ToString("0.0", CultureInfo.InvariantCulture), TotalWeight.ToString("0.0", CultureInfo.InvariantCulture)));
            foreach (var kv in WeightByDiameter().OrderBy(k => k.Key))
                lines.Add("     peso " + kv.Key + ": " + kv.Value.ToString("0.0", CultureInfo.InvariantCulture) + " kg");
            return string.Join(Environment.NewLine, lines);
        }

        // =================================================================
        // Construccion
        // =================================================================

        public static BlockPlan Build(BlockTopology topo, AppConfig cfg, PlanDiameters d)
        {
            var p = new BlockPlan
            {
                Topo = topo, Cfg = cfg, Diam = d, Thickness = topo.ZTop,
                Cb = Mm(cfg.CoverBottomMm), Ct = Mm(cfg.CoverTopMm), Ce = Mm(cfg.CoverEdgeMm), Cw = Mm(cfg.CoverWallMm),
                _tol = Mm(cfg.ToleranceMm), _minLen = Mm(cfg.MinBarLengthMm)
            };
            try
            {
                if (topo.Error != null) { p.Error = topo.Error; return p; }
                foreach ((Family f, string layer, string name) in cfg.BarTypesNeeded())
                    if (!d.Has(f, layer) || d.D(f, layer) <= 0)
                    {
                        p.Error = "sin tipo de barra para " + Families.Code(f) + (layer != "" ? " (" + layer + ")" : "") +
                                  (string.IsNullOrWhiteSpace(name) ? ": elige uno" : ": \"" + name + "\" no existe en el proyecto");
                        return p;
                    }
                if (!cfg.F1.Enabled && !cfg.F2.Enabled && !cfg.F3.Enabled && !cfg.F4.Enabled && !cfg.F5.Enabled && !cfg.F6.Enabled && !cfg.F7.Enabled && !cfg.F8.Enabled)
                {
                    p.Error = "todas las familias estan desactivadas";
                    return p;
                }
                p.Levels();
                if (p.Error != null) return p;
                if (cfg.F1.Enabled) p.BuildF1();
                if (cfg.F2.Enabled) p.BuildF2();
                if (cfg.F3.Enabled) p.BuildF3();
                if (cfg.F4.Enabled) p.BuildF4();
                if (cfg.F5.Enabled) p.BuildF5();
                if (cfg.F6.Enabled) p.BuildF6();
                if (cfg.F7.Enabled) p.BuildF7();
                if (cfg.F8.Enabled) p.BuildF8();
                if (p.Error != null) return p;
                p.CheckInside();
                if (p.Error != null) return p;
                if (p.Bars.Count == 0) { p.Error = "no se obtiene ninguna barra con esta configuracion"; return p; }
                p.Group();
            }
            catch (Exception ex)
            {
                p.Error = ex.Message;
            }
            return p;
        }

        private double D(Family f, string layer = "") => Diam.D(f, layer);

        /// <summary>
        /// Posiciones entre "from" y "to": "maxSpacing" = reparto con separacion maxima
        /// (n = techo(L / s) huecos iguales, barra en los dos extremos); "fromTop" = separacion
        /// exacta desde "from" y la ultima barra donde caiga (sin forzar el otro extremo).
        /// </summary>
        private List<double> Spread(double from, double to, double s, string mode)
        {
            if (mode == "fromTop")
            {
                var list = new List<double>();
                if (to - from < -_tol) return list;
                if (s <= _tol) { list.Add(from); return list; }
                for (double t = from; t <= to + _tol; t += s) list.Add(t);
                return list;
            }
            return Geometry2D.Positions(from, to, s, _tol);
        }

        private static bool Overlap(double a0, double a1, double b0, double b1) => Math.Min(a1, b1) >= Math.Max(a0, b0);

        /// <summary>Retranqueo extra de las patas de F2 en los bordes exteriores (diametro de F1 + propio).</summary>
        private double LegClearF2 => (Cfg.F1.Enabled ? Math.Max(D(Family.F1, "u"), D(Family.F1, "v")) : 0) + Math.Max(D(Family.F2, "u"), D(Family.F2, "v"));

        /// <summary>
        /// Zona de patas de las mallas junto a las caras exteriores: hasta que distancia de la
        /// cara llega el borde interior de la pata mas interior de las mallas cuyas patas
        /// ocupan cotas entre zMin y zMax. Las barras que corren hacia una cara exterior (pies
        /// de F4, prolongaciones de F5) y los verticales junto a ella (F4) paran antes.
        /// </summary>
        private double LegZone(double zMin, double zMax)
        {
            double zone = 0;
            if (Cfg.F1.Enabled && Cfg.F1.LegUpMm > 0)
            {
                double leg = Mm(Cfg.F1.LegUpMm);
                if (Overlap(_zF1u, _zF1v + leg, zMin, zMax)) zone = Math.Max(zone, Ce + D(Family.F1, "u") + D(Family.F1, "v"));
            }
            if (Cfg.F2.Enabled && Cfg.F2.LegDownMm > 0)
            {
                double leg = Mm(Cfg.F2.LegDownMm);
                if (Overlap(_zF2v - leg, _zF2u, zMin, zMax)) zone = Math.Max(zone, Ce + LegClearF2 + D(Family.F2, "u") + D(Family.F2, "v"));
            }
            if (Cfg.F3.Enabled && Cfg.F3.LegDownMm > 0)
            {
                double leg = Mm(Cfg.F3.LegDownMm);
                if (Overlap(_zF3v - leg, _zF3u, zMin, zMax)) zone = Math.Max(zone, Ce + D(Family.F3, "u") + D(Family.F3, "v"));
            }
            return zone;
        }

        /// <summary>
        /// Coordenadas (t a lo largo de la arista) de las barras de malla que cruzan el plano
        /// vertical de una familia de cara (perpendiculares a la cara, a cotas entre zMin y
        /// zMax y cuyo tramo recto pasa por el plano a "offset" de la cara). Un vertical colocado
        /// en esas t chocaria con ellas.
        /// </summary>
        private List<double> CrossingMeshCoords(RegionEdge edge, double offset, double zMin, double zMax, double tMin, double tMax, out double meshD)
        {
            var list = new List<double>();
            meshD = 0;
            if (!edge.AlongU && Math.Abs(edge.Dir.V) < 0.999) return list;
            if (edge.AlongU && Math.Abs(edge.Dir.U) < 0.999) return list;
            // barras perpendiculares a la cara: a lo largo de u si la cara va a lo largo de v, y al reves
            string layer = edge.AlongU ? "v" : "u";
            Pt plane = Geometry2D.Add(edge.Mid, Geometry2D.Scale(edge.Normal, offset));
            foreach (PlannedBar b in Bars)
            {
                if (b.Layer != layer || (b.Family != Family.F1 && b.Family != Family.F2 && b.Family != Family.F3)) continue;
                // cota del tramo recto (las patas no cruzan el plano en la cara)
                double level = b.Points[Math.Min(1, b.Points.Count - 1)].Z;
                if (level < zMin || level > zMax) continue;
                double lo, hi, coord;
                if (layer == "u") { lo = b.Points.Min(q => q.U); hi = b.Points.Max(q => q.U); coord = b.Points[1].V; if (plane.U < lo || plane.U > hi) continue; }
                else { lo = b.Points.Min(q => q.V); hi = b.Points.Max(q => q.V); coord = b.Points[1].U; if (plane.V < lo || plane.V > hi) continue; }
                double t = edge.AlongU ? (coord - edge.A.U) / edge.Dir.U : (coord - edge.A.V) / edge.Dir.V;
                // solo las que cruzan el plano dentro del tramo de la cara (las de fuera no estorban)
                if (t < tMin || t > tMax) continue;
                list.Add(t);
                meshD = Math.Max(meshD, b.D);
            }
            list.Sort();
            return list;
        }

        /// <summary>
        /// Posiciones entre t0 y t1 que esquivan una reticula de barras (coordenadas "grid",
        /// paso g): el paso se toma igual a g (o g / k si g supera la separacion maxima) y la
        /// fase se elige lo mas centrada posible dentro de la banda libre (a mas de "clearance"
        /// de cada barra de la reticula). Null si no hay reticula o no cabe.
        /// </summary>
        private List<double> SnapPositions(double t0, double t1, double s, List<double> grid, double clearance, out string why)
        {
            why = null;
            if (grid == null || grid.Count < 2) return null;
            var gaps = new List<double>();
            for (int i = 0; i + 1 < grid.Count; i++) gaps.Add(grid[i + 1] - grid[i]);
            gaps.Sort();
            double g = gaps[gaps.Count / 2];
            if (g <= _tol) return null;
            int k = Math.Max(1, (int)Math.Ceiling(g / s - 1e-9));
            double step = g / k;
            if (step < 2 * clearance) { why = "la reticula de la malla (paso " + ToMm(g) + " mm) no deja hueco libre"; return null; }
            double len = t1 - t0;
            int m = (int)Math.Floor(len / step + 1e-9);
            double phi = t0 + 0.5 * (len - m * step);
            double r = ((phi - grid[0]) % step + step) % step;
            if (r < clearance) phi += clearance - r;
            else if (r > step - clearance) phi -= r - (step - clearance);
            var pos = new List<double>();
            for (int i = 0; i <= m + 1; i++)
            {
                double t = phi + i * step;
                if (t < t0 - _tol) continue;
                if (t > t1 + _tol) break;
                pos.Add(t);
            }
            return pos;
        }

        /// <summary>
        /// Niveles entre z0 (abajo) y z1 (arriba): "fromTop" = separacion exacta desde el nivel
        /// superior hacia abajo, el resto queda abajo (como en el plano); "maxSpacing" = reparto
        /// con nivel en los dos extremos.
        /// </summary>
        private List<double> LevelsBetween(double z0, double z1, double s, string mode)
        {
            if (mode == "fromTop")
            {
                var list = new List<double>();
                if (z1 - z0 < -_tol) return list;
                if (s <= _tol) { list.Add(z1); return list; }
                for (double z = z1; z >= z0 - _tol; z -= s) list.Add(z);
                list.Reverse();
                return list;
            }
            return Geometry2D.Positions(z0, z1, s, _tol);
        }

        /// <summary>Cotas de las mallas y comprobacion de que no se solapan.</summary>
        private void Levels()
        {
            double T = Thickness;
            if (Cfg.F1.Enabled)
            {
                double du = D(Family.F1, "u"), dv = D(Family.F1, "v");
                _zF1u = Cb + 0.5 * du; _zF1v = Cb + du + 0.5 * dv; _f1Top = Cb + du + dv;
                LayerZ["F1:u"] = _zF1u; LayerZ["F1:v"] = _zF1v;
            }
            if (Cfg.F2.Enabled)
            {
                double du = D(Family.F2, "u"), dv = D(Family.F2, "v");
                double b = Mm(Cfg.F2.BelowRecessFloorMm);
                _zF2u = Topo.DeepestFloor - b - 0.5 * du; _zF2v = _zF2u - 0.5 * du - 0.5 * dv;
                _f2Bot = _zF2v - 0.5 * dv; _f2Top = _zF2u + 0.5 * du;
                LayerZ["F2:u"] = _zF2u; LayerZ["F2:v"] = _zF2v;
                if (Cfg.F1.Enabled && _f2Bot < _f1Top + Mm(25))
                {
                    Error = "F2 (malla bajo foso, a z=" + ToMm(_zF2v) + " mm) se solapa con F1 (hasta z=" + ToMm(_f1Top) +
                            " mm): el fondo del foso mas profundo (" + ToMm(Topo.DeepestFloor) + " mm) deja poca base";
                    return;
                }
                if (_f2Bot < Cb)
                {
                    Error = "F2 queda por debajo del recubrimiento inferior (z=" + ToMm(_f2Bot) + " mm)";
                    return;
                }
            }
            if (Cfg.F3.Enabled)
            {
                double du = D(Family.F3, "u"), dv = D(Family.F3, "v");
                _zF3u = T - Ct - 0.5 * du; _zF3v = _zF3u - 0.5 * du - 0.5 * dv; _f3Bot = _zF3v - 0.5 * dv;
                LayerZ["F3:u"] = _zF3u; LayerZ["F3:v"] = _zF3v;
                double below = Cfg.F2.Enabled ? _f2Top : (Cfg.F1.Enabled ? _f1Top : Cb);
                if (_f3Bot < below + Mm(25))
                {
                    Error = "F3 (malla superior, desde z=" + ToMm(_f3Bot) + " mm) se solapa con la malla inferior (hasta z=" + ToMm(below) + " mm): canto " + ToMm(T) + " mm";
                    return;
                }
            }
        }

        // -----------------------------------------------------------------
        // Mallas (F1, F2, F3)
        // -----------------------------------------------------------------

        /// <summary>Region sobre la que se reparte una malla, con sus aristas tipadas.</summary>
        private sealed class MeshRegion
        {
            public Region2D Shape;
            public List<RegionEdge> Edges;
            public RegionKind? Kind;
            public string Name;
            public int RegionIndex = -1;
        }

        private IEnumerable<MeshRegion> BodyRegions()
        {
            int i = 0;
            foreach (Region2D b in Topo.Body)
            {
                i++;
                yield return new MeshRegion
                {
                    Shape = b, Name = Topo.Body.Count == 1 ? "cuerpo" : "cuerpo " + i,
                    Edges = Topo.BuildEdges(b, -1, (ring, idx, e) => ring == 0 ? EdgeKind.Exterior : EdgeKind.Hole)
                };
            }
        }

        private void BuildF1()
        {
            double du = D(Family.F1, "u"), dv = D(Family.F1, "v");
            double leg = Mm(Cfg.F1.LegUpMm);
            foreach (MeshRegion mr in BodyRegions())
            {
                Mesh(Family.F1, "u", mr, e => Ce, 0, _zF1u, du, Mm(Cfg.F1.U.SpacingMm), Cfg.F1.U.LayoutMode, leg, k => k == EdgeKind.Exterior);
                Mesh(Family.F1, "v", mr, e => Ce, du, _zF1v, dv, Mm(Cfg.F1.V.SpacingMm), Cfg.F1.V.LayoutMode, leg, k => k == EdgeKind.Exterior);
            }
        }

        private void BuildF2()
        {
            double du = D(Family.F2, "u"), dv = D(Family.F2, "v");
            double leg = -Mm(Cfg.F2.LegDownMm);
            IEnumerable<MeshRegion> regions;
            if (Cfg.F2.Extent == "recess")
            {
                double anchorage = Mm(Cfg.F2.AnchorageMm);
                List<Region2D> grown = Poly2D.Offset(Topo.Recesses.Select(r => r.Shape), anchorage, _tol);
                List<Region2D> clipped = Poly2D.Intersection(grown, Topo.Body, _tol);
                var list = new List<MeshRegion>();
                int i = 0;
                foreach (Region2D r in clipped)
                {
                    i++;
                    list.Add(new MeshRegion
                    {
                        Shape = r, Name = clipped.Count == 1 ? "bajo foso" : "bajo foso " + i,
                        Edges = Topo.BuildEdges(r, -1, (ring, idx, e) => KindOnBody(e))
                    });
                }
                regions = list;
                if (clipped.Count == 0) Warn("F2: la zona bajo los fosos queda vacia");
            }
            else regions = BodyRegions();
            // en los bordes exteriores las patas de F2 bajan por dentro de las de F1: se retranquea
            // el diametro de F1 mas el propio (asi queda un diametro libre entre ambas patas)
            double legClear = (Cfg.F1.Enabled ? Math.Max(D(Family.F1, "u"), D(Family.F1, "v")) : 0) + Math.Max(du, dv);
            Func<RegionEdge, double> cover = e => e.Kind == EdgeKind.Exterior ? Ce + legClear : (e.Kind == EdgeKind.Free ? 0 : Ce);
            // planos de los verticales de F4 (a cw + d4/2 de cada cara de foso de plataforma, en
            // caras paralelas a los ejes): las barras de la malla paralelas a esos planos los esquivan
            var planesV = new List<double>();   // planos v = cte (caras a lo largo de u): los esquivan las barras u
            var planesU = new List<double>();   // planos u = cte (caras a lo largo de v): los esquivan las barras v
            double clearance = 0;
            if (Cfg.F4.Enabled)
            {
                double d4 = D(Family.F4), o4 = Cw + 0.5 * d4;
                clearance = 0.5 * d4 + 0.5 * Math.Max(du, dv) + _tol + Mm(1);
                foreach (TopRegion reg in Topo.Regions.Where(r => r.Kind == RegionKind.Platform))
                    foreach (RegionEdge e in reg.Edges.Where(e => e.Kind == EdgeKind.Recess))
                    {
                        Pt plane = Geometry2D.Add(e.Mid, Geometry2D.Scale(e.Normal, o4));
                        if (Math.Abs(e.Dir.U) > 0.999) planesV.Add(plane.V);
                        else if (Math.Abs(e.Dir.V) > 0.999) planesU.Add(plane.U);
                    }
            }
            foreach (MeshRegion mr in regions)
            {
                Mesh(Family.F2, "u", mr, cover, 0, _zF2u, du, Mm(Cfg.F2.U.SpacingMm), Cfg.F2.U.LayoutMode, leg, k => k == EdgeKind.Exterior, planesV, clearance);
                Mesh(Family.F2, "v", mr, cover, du, _zF2v, dv, Mm(Cfg.F2.V.SpacingMm), Cfg.F2.V.LayoutMode, leg, k => k == EdgeKind.Exterior, planesU, clearance);
            }
        }

        /// <summary>Tipo de una arista de una region auxiliar: el de la arista del contorno inferior sobre la que cae, o libre.</summary>
        private EdgeKind KindOnBody(RegionEdge e)
        {
            Pt m = e.Mid;
            foreach (RegionEdge b in Topo.BodyEdges)
            {
                if (Math.Abs(Geometry2D.Cross(b.Dir, e.Dir)) > 1e-3) continue;
                double dist = Geometry2D.DistanceToSegment(m, b.A, b.B, out double t);
                if (dist <= 2 * _tol && t > -1e-6 && t < 1 + 1e-6) return b.Kind;
            }
            return EdgeKind.Free;
        }

        private void BuildF3()
        {
            double du = D(Family.F3, "u"), dv = D(Family.F3, "v");
            double leg = -Mm(Cfg.F3.LegDownMm);
            double d4 = Cfg.F4.Enabled ? D(Family.F4) : 0;
            foreach (TopRegion reg in Topo.Regions.Where(r => r.Kind == RegionKind.Platform))
            {
                var mr = new MeshRegion { Shape = reg.Shape, Edges = reg.Edges, Kind = reg.Kind, Name = "plataforma " + (reg.KindIndex + 1), RegionIndex = reg.Index };
                Func<RegionEdge, double> cover = e =>
                {
                    switch (e.Kind)
                    {
                        case EdgeKind.Exterior: case EdgeKind.Hole: return Ce;
                        case EdgeKind.Recess: return Cw + d4;
                        default: return 0;
                    }
                };
                Func<EdgeKind, bool> legAt = k => k == EdgeKind.Exterior || k == EdgeKind.Recess || k == EdgeKind.Hole;
                Mesh(Family.F3, "u", mr, cover, 0, _zF3u, du, Mm(Cfg.F3.U.SpacingMm), Cfg.F3.U.LayoutMode, leg, legAt);
                Mesh(Family.F3, "v", mr, cover, du, _zF3v, dv, Mm(Cfg.F3.V.SpacingMm), Cfg.F3.V.LayoutMode, leg, legAt);
            }
        }

        /// <summary>
        /// Capa de barras paralelas a u (layer "u", repartidas en v) o a v (repartidas en u)
        /// dentro de la region retranqueada arista a arista (recubrimiento de cada arista +
        /// medio diametro + "extra"). Cada recta se recorta contra la region; cada tramo es
        /// una barra. En los extremos que caen en una arista de los tipos "legAt" la barra
        /// lleva una pata vertical de longitud |leg| (hacia arriba si leg > 0); en los demas
        /// extremos va recta y llega al recubrimiento.
        /// </summary>
        private void Mesh(Family f, string layer, MeshRegion mr, Func<RegionEdge, double> coverOf, double extra, double z, double d,
                          double spacing, string mode, double leg, Func<EdgeKind, bool> legAt, List<double> obstacles = null, double clearance = 0)
        {
            bool alongU = layer == "u";
            var byKey = mr.Edges.ToDictionary(e => (e.Ring, e.Index), e => e);
            Func<RegionEdge, double> insetOf = e => coverOf(e) + 0.5 * d + extra;
            List<Region2D> parts = Poly2D.InsetByEdge(mr.Shape, (ring, idx) => byKey.TryGetValue((ring, idx), out RegionEdge e) ? insetOf(e) : 0.5 * d + extra, _tol);
            if (parts.Count == 0) { Warn(Families.Code(f) + " (" + layer + "): no cabe en " + mr.Name); return; }
            var outline = new Outline2D(parts.SelectMany(r => r.Rings()), _tol);
            double from = alongU ? outline.VMin : outline.UMin, to = alongU ? outline.VMax : outline.UMax;
            if (to - from < -_tol) { Warn(Families.Code(f) + " (" + layer + "): no cabe en " + mr.Name); return; }
            P3 normal = alongU ? P3.AxisV : P3.AxisU;
            Pt barDir = alongU ? new Pt(1, 0) : new Pt(0, 1);
            List<double> coords = Spread(from, to, spacing, mode);
            if (obstacles != null && obstacles.Count > 0)
                coords = AvoidObstacles(coords, from, to, spacing, mode, obstacles, clearance, Families.Code(f) + " (" + layer + ")");
            foreach (double c in coords)
                foreach (Span s in outline.Cut(alongU, c, 0, _tol))
                {
                    if (s.Length < Math.Max(_minLen, _tol)) { Skipped++; continue; }
                    Pt p0 = alongU ? new Pt(s.A, c) : new Pt(c, s.A);
                    Pt p1 = alongU ? new Pt(s.B, c) : new Pt(c, s.B);
                    EdgeKind k0 = KindAt(p0, mr.Edges, insetOf, barDir), k1 = KindAt(p1, mr.Edges, insetOf, barDir);
                    bool l0 = Math.Abs(leg) > _tol && legAt(k0), l1 = Math.Abs(leg) > _tol && legAt(k1);
                    // extremo recto: la barra llega al recubrimiento (medio diametro mas alla de la linea de ejes);
                    // en un limite interno (plataforma / murete) se queda justo en el limite
                    if (!l0 && k0 != EdgeKind.Internal) p0 = Geometry2D.Sub(p0, Geometry2D.Scale(barDir, 0.5 * d));
                    if (!l1 && k1 != EdgeKind.Internal) p1 = Geometry2D.Add(p1, Geometry2D.Scale(barDir, 0.5 * d));
                    var pts = new List<P3>();
                    if (l0) pts.Add(new P3(p0, z + leg));
                    pts.Add(new P3(p0, z));
                    pts.Add(new P3(p1, z));
                    if (l1) pts.Add(new P3(p1, z + leg));
                    Bars.Add(new PlannedBar { Family = f, Layer = layer, Points = pts, D = d, Normal = normal, Face = mr.Name, RegionIndex = mr.RegionIndex, NominalSpacing = spacing });
                }
        }

        /// <summary>
        /// Si alguna posicion de la malla queda a menos de "clearance" de un plano de verticales
        /// (F4 en las caras de foso), se reparte con mas barras (paso menor, siempre bajo la
        /// separacion maxima) hasta que ninguna coincida. Si no se consigue, aviso.
        /// </summary>
        private List<double> AvoidObstacles(List<double> coords, double from, double to, double spacing, string mode, List<double> obstacles, double clearance, string what)
        {
            Func<List<double>, bool> clear = cs => cs.All(c => obstacles.All(o => Math.Abs(c - o) >= clearance));
            if (clear(coords)) return coords;
            if (mode == "maxSpacing" && to - from > _tol)
            {
                int n = Math.Max(1, coords.Count - 1);
                for (int extra = 1; extra <= 12; extra++)
                {
                    int n2 = n + extra;
                    double step = (to - from) / n2;
                    var cs = new List<double>();
                    for (int k = 0; k <= n2; k++) cs.Add(from + k * step);
                    if (clear(cs))
                    {
                        Warn(what + ": se reparte con " + (n2 + 1) + " barras en vez de " + (n + 1) + " (paso " + ToMm(step) + " mm) para no coincidir con los verticales de F4");
                        return cs;
                    }
                }
            }
            Warn(what + ": alguna barra coincide con un vertical de F4; revisa la separacion");
            return coords;
        }

        /// <summary>
        /// Tipo de la arista contra la que termina una barra en el punto p: la arista cuya
        /// recta desplazada su propio retranqueo pasa por p (y p cae en su extension); entre
        /// varias, la mas perpendicular a la barra.
        /// </summary>
        private EdgeKind KindAt(Pt p, IEnumerable<RegionEdge> edges, Func<RegionEdge, double> insetOf, Pt barDir)
        {
            RegionEdge best = null; double bestScore = double.MaxValue;
            foreach (RegionEdge e in edges)
            {
                double inset = insetOf(e);
                // la recta retranqueada se prolonga mas alla de la arista (en una esquina entrante, el
                // retranqueo de la arista contigua): se admite el extremo dentro de ese margen
                double along = Geometry2D.Dot(Geometry2D.Sub(p, e.A), e.Dir);
                double margin = Math.Abs(inset) + 4 * _tol;
                if (along < -margin || along > e.Length + margin) continue;
                double err = Math.Abs(Geometry2D.SignedDistanceToLine(p, e.A, e.B) - inset);
                if (err > 4 * _tol) continue;
                double score = err / _tol + Math.Abs(Geometry2D.Dot(e.Dir, barDir));
                if (score < bestScore) { bestScore = score; best = e; }
            }
            return best?.Kind ?? EdgeKind.Free;
        }

        // -----------------------------------------------------------------
        // Utilidades de caras
        // -----------------------------------------------------------------

        private double CoverOf(TopRegion reg, RegionEdge e)
        {
            switch (e.Kind)
            {
                case EdgeKind.Exterior: case EdgeKind.Hole: return reg.Kind == RegionKind.Wall ? Cw : Ce;
                case EdgeKind.Recess: return Cw;
                default: return 0;
            }
        }

        /// <summary>Region retranqueada el recubrimiento + medio diametro en cada arista: donde pueden ir los ejes de barras de diametro d.</summary>
        /// <param name="exteriorMin">Distancia minima adicional a las caras exteriores (zona de patas de las mallas), 0 = solo el recubrimiento.</param>
        private Outline2D Clamp(TopRegion reg, double d, double exteriorMin = 0)
        {
            string key = reg.Index + ":" + d.ToString("R", CultureInfo.InvariantCulture) + ":" + exteriorMin.ToString("R", CultureInfo.InvariantCulture);
            if (_clamps.TryGetValue(key, out Outline2D o)) return o;
            var byKey = reg.Edges.ToDictionary(e => (e.Ring, e.Index), e => e);
            List<Region2D> parts = Poly2D.InsetByEdge(reg.Shape, (ring, idx) =>
            {
                if (!byKey.TryGetValue((ring, idx), out RegionEdge e)) return 0.5 * d;
                if (e.Kind == EdgeKind.Internal || e.Kind == EdgeKind.Free) return 0;
                double inset = CoverOf(reg, e) + 0.5 * d;
                if ((e.Kind == EdgeKind.Exterior || e.Kind == EdgeKind.Hole) && exteriorMin > 0) inset = Math.Max(inset, exteriorMin + 0.5 * d + _tol);
                return inset;
            }, _tol);
            o = parts.Count == 0 ? null : new Outline2D(parts.SelectMany(r => r.Rings()), _tol);
            _clamps[key] = o;
            return o;
        }

        /// <summary>
        /// Retranqueo desde la esquina para la primera barra de una cara (a lo largo de ella):
        /// en una esquina convexa con otra cara del mismo tipo, recubrimiento + 1.5 d (la barra
        /// queda junto a la primera de la cara contigua); con una cara exterior, su
        /// recubrimiento + d/2; en una esquina entrante, d/2.
        /// </summary>
        private double CornerInset(TopRegion reg, RegionEdge edge, bool atStart, double d)
        {
            RegionEdge adj = atStart ? edge.Prev : edge.Next;
            bool convex = atStart ? edge.ConvexStart : edge.ConvexEnd;
            if (adj == null || !convex) return 0.5 * d;
            switch (adj.Kind)
            {
                case EdgeKind.Recess: return edge.Kind == EdgeKind.Recess ? Cw + 1.5 * d : Cw + 0.5 * d;
                case EdgeKind.Exterior: case EdgeKind.Hole: return edge.Kind == adj.Kind ? CoverOf(reg, adj) + 1.5 * d : CoverOf(reg, adj) + 0.5 * d;
                default: return 0.5 * d;
            }
        }

        /// <summary>
        /// Extension [tStart, tEnd] de una barra recta paralela a la arista "edge", a "o" de
        /// ella hacia dentro, prolongada hasta la esquina: hasta el cruce con la linea de la
        /// barra de la cara contigua (si "continues" en esa cara) mas "lap", y siempre
        /// recortada al hormigon (clamp).
        /// </summary>
        private bool FaceExtent(RegionEdge edge, double o, Func<RegionEdge, bool> continues, Func<RegionEdge, double> offsetOfAdjacent,
                                Outline2D clamp, double lap, out double tStart, out double tEnd)
        {
            tStart = tEnd = 0;
            if (clamp == null) return false;
            Pt origin = Geometry2D.Add(edge.A, Geometry2D.Scale(edge.Normal, o));
            Pt dir = edge.Dir;
            double L = edge.Length;
            Span best = default; double bestOv = -1;
            foreach (Span s in Geometry2D.LineCut(clamp, origin, dir, 0, _tol))
            {
                double ov = Math.Min(s.B, L) - Math.Max(s.A, 0);
                if (ov > bestOv) { bestOv = ov; best = s; }
            }
            if (bestOv <= _tol) return false;
            tStart = best.A; tEnd = best.B;
            if (edge.Prev != null && continues(edge.Prev))
            {
                double tc = Geometry2D.LineParam(origin, dir, Geometry2D.Add(edge.Prev.A, Geometry2D.Scale(edge.Prev.Normal, offsetOfAdjacent(edge.Prev))), edge.Prev.Dir);
                if (!double.IsNaN(tc)) tStart = Math.Max(best.A, tc - lap);
            }
            if (edge.Next != null && continues(edge.Next))
            {
                double tc = Geometry2D.LineParam(origin, dir, Geometry2D.Add(edge.Next.A, Geometry2D.Scale(edge.Next.Normal, offsetOfAdjacent(edge.Next))), edge.Next.Dir);
                if (!double.IsNaN(tc)) tEnd = Math.Min(best.B, tc + lap);
            }
            return tEnd - tStart > _tol;
        }

        /// <summary>Anillo de una region cuyas aristas son todas del tipo dado (para los anillos cerrados de F5 / F8), o null.</summary>
        private static List<Pt> FullRing(TopRegion reg, EdgeKind kind, out int ringIndex, out bool isHole)
        {
            ringIndex = -1; isHole = false;
            int ring = 0;
            foreach (List<Pt> pts in reg.Shape.Rings())
            {
                var edges = reg.Edges.Where(e => e.Ring == ring).ToList();
                if (edges.Count >= 3 && edges.All(e => e.Kind == kind)) { ringIndex = ring; isHole = ring > 0; return pts; }
                ring++;
            }
            return null;
        }

        /// <summary>
        /// Polilinea cerrada por traslape: el anillo desplazado "o" hacia el interior de la
        /// region (hacia dentro si es el exterior, hacia fuera si es un hueco), empezando en el
        /// punto medio de su arista mas larga, dando la vuelta y siguiendo "lap" mas alla.
        /// </summary>
        private List<Pt> RingPolyline(List<Pt> ring, bool isHole, double o, double lap)
        {
            List<Region2D> off = Poly2D.OffsetRing(ring, isHole ? o : -o, _tol);
            if (off.Count == 0) return null;
            List<Pt> r = isHole ? off[0].Outer : off.OrderByDescending(x => x.Area).First().Outer;
            if (r.Count < 3) return null;
            // arista mas larga
            int k = 0; double best = -1;
            for (int i = 0; i < r.Count; i++)
            {
                double l = r[i].DistanceTo(r[(i + 1) % r.Count]);
                if (l > best) { best = l; k = i; }
            }
            Pt a = r[k], b = r[(k + 1) % r.Count];
            Pt start = new Pt(0.5 * (a.U + b.U), 0.5 * (a.V + b.V));
            var poly = new List<Pt> { start };
            for (int i = 1; i <= r.Count; i++) poly.Add(r[(k + i) % r.Count]);
            poly.Add(start);
            Pt d = Geometry2D.Unit(Geometry2D.Sub(b, a));
            poly.Add(Geometry2D.Add(start, Geometry2D.Scale(d, Math.Min(lap, 0.5 * best))));
            return poly;
        }

        // -----------------------------------------------------------------
        // F4: L en caras de foso de plataforma
        // -----------------------------------------------------------------

        /// <summary>Datos de una cara de foso de plataforma para F4.</summary>
        private sealed class F4Face
        {
            public TopRegion Region;
            public RegionEdge Edge;
            public string Name;
            public double ZFoot, FootLen, T0, T1, TFirstMin, TFirstMax, Clearance;
            public bool PrevOwns, OwnsEnd;
            public List<double> Grid = new List<double>();
            public List<double> Positions = new List<double>();
        }

        private void BuildF4()
        {
            double d4 = D(Family.F4);
            double o4 = Cw + 0.5 * d4;
            double zTop4 = Thickness - Ct - 0.5 * d4;
            double vertical = Mm(Cfg.F4.VerticalMm), foot = Mm(Cfg.F4.FootMm), s = Mm(Cfg.F4.SpacingMm);
            LayerZ["F4"] = zTop4;
            foreach (TopRegion reg in Topo.Regions.Where(r => r.Kind == RegionKind.Platform))
            {
                // 1. datos de cada cara: cota del pie, pie, rango admisible y reticula de la malla que cruza el vertical
                var faces = new List<F4Face>();
                int faceNo = 0;
                foreach (RegionEdge edge in reg.Edges)
                {
                    if (edge.Kind != EdgeKind.Recess) continue;
                    faceNo++;
                    var fc = new F4Face { Region = reg, Edge = edge, Name = "plataforma " + (reg.KindIndex + 1) + " cara " + faceNo };
                    if (!PrepareF4Face(fc, d4, o4, zTop4, vertical, foot)) return;
                    faces.Add(fc);
                }
                // 2. posiciones, en el orden del anillo, acoplando cada cara con la barra de esquina de la anterior
                var byEdge = faces.ToDictionary(f => f.Edge, f => f);
                foreach (F4Face fc in faces)
                {
                    double? prevA = null;
                    if (fc.PrevOwns && byEdge.TryGetValue(fc.Edge.Prev, out F4Face pf) && pf.Positions.Count > 0)
                        prevA = pf.Edge.Length - pf.Positions.Max();
                    fc.Positions = F4Positions(fc, s, d4, o4, prevA);
                    if (fc.Positions.Count == 0) Warn("F4: " + fc.Name + " demasiado corta");
                }
                // 3. segunda pasada: la primera cara del anillo se acoplo con la posicion nominal de la barra de
                //    esquina de la ultima; si esa barra quedo lejos del vertice, se anade la que falte
                foreach (F4Face fc in faces)
                {
                    if (!fc.PrevOwns || !byEdge.TryGetValue(fc.Edge.Prev, out F4Face prev) || prev.Positions.Count == 0 || fc.Positions.Count == 0) continue;
                    double tMax = FirstBarMax(prev.Edge.Length - prev.Positions.Max(), s, o4);
                    double first = fc.Positions.Min();
                    if (first > tMax + _tol)
                    {
                        double? t = LargestClear(fc, tMax, fc.TFirstMin, first - 2 * d4 - Mm(1));
                        if (t != null) fc.Positions.Insert(0, t.Value);
                        else Warn("F4: en " + fc.Name + " la primera barra queda a mas de la separacion de la barra de esquina de la cara anterior");
                    }
                }
                // 4. barras
                foreach (F4Face fc in faces)
                {
                    RegionEdge edge = fc.Edge;
                    Pt outward = Geometry2D.Scale(edge.Normal, -1);
                    P3 normal = new P3(edge.Dir.U, edge.Dir.V, 0);
                    FaceRanges[(Family.F4, edge)] = (fc.T0, fc.T1);
                    foreach (double t in fc.Positions.OrderBy(x => x))
                    {
                        Pt b = Geometry2D.Add(edge.At(t), Geometry2D.Scale(edge.Normal, o4));
                        Pt fe = Geometry2D.Add(b, Geometry2D.Scale(outward, fc.FootLen));
                        Bars.Add(new PlannedBar
                        {
                            Family = Family.F4, D = d4, Normal = normal, Face = fc.Name, RegionIndex = reg.Index, NominalSpacing = s, Edge = edge,
                            Points = new List<P3> { new P3(b, zTop4), new P3(b, fc.ZFoot), new P3(fe, fc.ZFoot) }
                        });
                    }
                }
            }
        }

        /// <summary>
        /// Cota del pie (bajo el fondo del foso, fuera de F1 / F2, desfasado un diametro en las
        /// caras a lo largo de v), longitud del pie (hasta el recubrimiento de la cara opuesta y
        /// fuera de la zona de patas), rango admisible a lo largo de la cara (regla de esquinas,
        /// zona de patas junto a las caras exteriores) y reticula de la malla que cruza el
        /// vertical. False si hay un error (queda en Error).
        /// </summary>
        private bool PrepareF4Face(F4Face fc, double d4, double o4, double zTop4, double vertical, double foot)
        {
            RegionEdge edge = fc.Edge; TopRegion reg = fc.Region; string face = fc.Name;
            Recess rc = Topo.Recesses[edge.RecessIndex];
            // pie bajo el fondo del foso
            double zFoot = zTop4 - vertical;
            double maxFoot = rc.ZFloor - Cw - 0.5 * d4;
            if (zFoot > maxFoot + _tol)
            {
                zFoot = maxFoot;
                Warn("F4: en " + face + " el vertical se alarga a " + ToMm(zTop4 - zFoot) + " mm para que el pie quede bajo el fondo del foso (z=" + ToMm(rc.ZFloor) + " mm)");
            }
            double lo = Cfg.F1.Enabled ? _f1Top : Cb;
            double hi = Cfg.F2.Enabled ? _f2Bot : maxFoot;
            // pie que llega a F1 (o mas abajo): patilla apoyada sobre la parrilla inferior
            if (Cfg.F1.Enabled && zFoot - 0.5 * d4 < _f1Top + _tol)
            {
                double rest = _f1Top + 0.5 * d4;
                if (rest > maxFoot + _tol)
                {
                    Error = "F4: no hay sitio para el pie entre F1 y el fondo del foso en " + face;
                    return false;
                }
                if (zFoot < rest - _tol)
                    Warn("F4: el pie en " + face + " alcanzaba F1 (z=" + ToMm(zFoot) + " mm); se apoya sobre la parrilla inferior (z=" + ToMm(rest) + " mm)");
                zFoot = rest;
            }
            // choque con F2: a media altura entre ambas mallas
            bool hitsF2 = Cfg.F2.Enabled && zFoot + 0.5 * d4 > _f2Bot - _tol && zFoot - 0.5 * d4 < _f2Top + _tol;
            if (hitsF2)
            {
                double mid = 0.5 * (lo + hi);
                Warn("F4: el pie en " + face + " chocaba con F2 a z=" + ToMm(zFoot) + " mm; se coloca a media altura entre ambas mallas (z=" + ToMm(mid) + " mm)");
                zFoot = mid;
                if (zFoot - 0.5 * d4 < lo - _tol || zFoot + 0.5 * d4 > hi + _tol || zFoot > maxFoot + _tol)
                {
                    Error = "F4: no hay sitio para el pie entre F1 y F2 en " + face + " (quedan " + ToMm(hi - lo) + " mm)";
                    return false;
                }
            }
            if (zFoot - 0.5 * d4 < Cb - _tol)
            {
                Error = "F4: el pie en " + face + " queda bajo el recubrimiento inferior (z=" + ToMm(zFoot) + " mm); reduce verticalMm";
                return false;
            }
            // los pies de caras contiguas convergen en las esquinas entrantes (fosos) a la misma cota:
            // los de las caras a lo largo de v van un diametro mas abajo (contacto previsto)
            if (!edge.AlongU)
            {
                if (zFoot - d4 - 0.5 * d4 >= lo + _tol) zFoot -= d4;
                else if (zFoot + d4 + 0.5 * d4 <= hi - _tol && zFoot + d4 <= maxFoot + _tol) zFoot += d4;
                else { Error = "F4: no hay sitio para desfasar un diametro los pies de " + face; return false; }
            }
            fc.ZFoot = zFoot;
            // sitio para el pie hacia el foso: hasta el recubrimiento de la cara opuesta del cuerpo,
            // sin entrar en la zona de patas de las mallas junto a esa cara
            Pt mid2 = Geometry2D.Add(edge.At(0.5 * edge.Length), Geometry2D.Scale(edge.Normal, o4));
            Pt outward = Geometry2D.Scale(edge.Normal, -1);
            double footZone = LegZone(zFoot - 0.5 * d4, zFoot + 0.5 * d4);
            double room = double.MaxValue;
            foreach (Span sp in Geometry2D.LineCut(Topo.Bottom, mid2, outward, 0, _tol))
                if (sp.A <= _tol && sp.B > _tol) { room = sp.B - Math.Max(Ce, footZone + 0.5 * d4 + _tol); break; }
            double footLen = foot;
            if (room < footLen - _tol)
            {
                footLen = room;
                Warn("F4: el pie en " + face + " se acorta a " + ToMm(footLen) + " mm (hasta el recubrimiento de la cara opuesta)");
            }
            if (footLen < _minLen && footLen < foot - _tol)
            {
                Error = "F4: el pie no cabe en " + face + " (solo " + ToMm(footLen) + " mm hasta la cara opuesta)";
                return false;
            }
            fc.FootLen = footLen;
            // rango admisible a lo largo de la cara
            double s = Mm(Cfg.F4.SpacingMm);
            double zone = LegZone(zFoot - 0.5 * d4, zTop4);
            double exteriorInset = zone + 0.5 * d4 + _tol;
            Func<RegionEdge, bool> same = e => e.Kind == EdgeKind.Recess;
            Func<RegionEdge, bool> ext = e => e.Kind == EdgeKind.Exterior || e.Kind == EdgeKind.Hole;
            fc.PrevOwns = edge.Prev != null && same(edge.Prev) && edge.ConvexStart;
            fc.OwnsEnd = edge.Next != null && same(edge.Next) && edge.ConvexEnd;
            fc.T1 = edge.Length - CornerInset(reg, edge, false, d4);
            if (edge.Next != null && ext(edge.Next) && edge.ConvexEnd) fc.T1 = Math.Min(fc.T1, edge.Length - exteriorInset);
            if (fc.PrevOwns)
            {
                // la barra de esquina de la cara anterior (a o4 de aquella cara, a Cw + 1.5 d del vertice):
                // la primera de esta cara va a una separacion (en planta) de ella, como mucho
                fc.TFirstMin = Cw + 1.5 * d4;
                fc.TFirstMax = FirstBarMax(Cw + 1.5 * d4, s, o4);
                fc.T0 = fc.TFirstMax;
            }
            else
            {
                fc.T0 = CornerInset(reg, edge, true, d4);
                if (edge.Prev != null && ext(edge.Prev) && edge.ConvexStart) fc.T0 = Math.Max(fc.T0, exteriorInset);
                fc.TFirstMin = fc.TFirstMax = fc.T0;
            }
            // barras de la malla que cruzan el plano del vertical dentro del tramo util de la cara
            fc.Grid = CrossingMeshCoords(edge, o4, zFoot - 0.5 * d4, zTop4, Math.Min(fc.TFirstMin, fc.T0) - _tol, fc.T1 + _tol, out double meshD);
            fc.Clearance = 0.5 * d4 + 0.5 * meshD + _tol + Mm(1);
            return true;
        }

        /// <summary>
        /// Mayor t de la primera barra de una cara para que su distancia en planta a la barra de
        /// esquina de la cara anterior no supere la separacion s: esa barra esta a o4 de esta
        /// cara (medido a lo largo de ella) y a "a" del vertice medido a lo largo de la anterior.
        /// </summary>
        private static double FirstBarMax(double a, double s, double o4)
        {
            double perp = a - o4;
            double along = Math.Sqrt(Math.Max(0, s * s - perp * perp));
            return o4 + along;
        }

        private bool ClearOfGrid(F4Face fc, double t) => fc.Grid.All(g => Math.Abs(t - g) >= fc.Clearance);

        /// <summary>Mayor t libre de la reticula en [tMin, min(tMax, below)], de milimetro en milimetro, o null.</summary>
        private double? LargestClear(F4Face fc, double tMax, double tMin, double below)
        {
            for (double t = Math.Min(tMax, below); t >= tMin - 1e-9; t -= Mm(1))
                if (ClearOfGrid(fc, t)) return t;
            return null;
        }

        /// <summary>
        /// Posiciones de F4 en una cara: reparto de extremo a extremo (barra exacta en la esquina
        /// que posee; la primera a una separacion de la barra de esquina de la cara anterior) con
        /// separacion maxima, comprobando que cada posicion esquiva las barras de la malla que
        /// cruzan el plano del vertical; si alguna coincide, con mas barras; si no, retrasando el
        /// inicio dentro de lo admisible; y en ultimo termino ajustando a la reticula de la malla
        /// y anadiendo la barra que falte en la esquina.
        /// </summary>
        private List<double> F4Positions(F4Face fc, double s, double d, double o4, double? prevA)
        {
            double tStartMax = prevA != null ? Math.Min(fc.TFirstMax, FirstBarMax(prevA.Value, s, o4)) : fc.TFirstMax;
            double tStartMin = fc.TFirstMin, t1 = fc.T1;
            if (t1 - tStartMax < -_tol)
            {
                var one = new List<double>();
                if (fc.PrevOwns && t1 - o4 >= 2 * d && ClearOfGrid(fc, t1)) one.Add(t1);
                return one;
            }
            Func<List<double>, bool> ok = ps => ps.All(t => ClearOfGrid(fc, t));
            if (Cfg.F4.LayoutMode == "fromTop")
            {
                List<double> ex = Spread(tStartMax, t1, s, "fromTop");
                if (ok(ex)) return ex;
            }
            // 1. reparto normal con extremos exactos, con mas barras si alguna coincide con la malla
            double len = t1 - tStartMax;
            int n0 = Math.Max(1, (int)Math.Ceiling(len / s - 1e-9));
            for (int n = n0; n <= n0 + 6; n++)
            {
                var ps = new List<double>();
                for (int k = 0; k <= n; k++) ps.Add(tStartMax + k * len / n);
                if (ok(ps)) return ps;
            }
            // 2. moviendo el inicio dentro de lo admisible (el final sigue exacto en la esquina): hacia
            //    atras si hay barra de esquina de la cara anterior, hacia dentro (hasta una separacion
            //    del limite) si el inicio es un limite exterior o una esquina entrante
            var starts = new List<double>();
            if (fc.PrevOwns) for (double t0 = tStartMax - Mm(5); t0 >= tStartMin - 1e-9; t0 -= Mm(5)) starts.Add(t0);
            else for (double t0 = tStartMax + Mm(5); t0 <= tStartMax + s + 1e-9; t0 += Mm(5)) starts.Add(t0);
            // y, si el final no es una esquina propia, tambien adelantando el final hasta una separacion del limite
            var ends = new List<double> { t1 };
            if (!fc.OwnsEnd) for (double te = t1 - Mm(5); te >= t1 - s - 1e-9; te -= Mm(5)) ends.Add(te);
            foreach (double te in ends)
                foreach (double t0 in starts.Count > 0 ? starts : new List<double> { tStartMax })
                {
                    if (te == t1 && t0 == tStartMax) continue;
                    double l = te - t0;
                    if (l < -_tol) continue;
                    int n = Math.Max(1, (int)Math.Ceiling(l / s - 1e-9));
                    for (int nn = n; nn <= n + 2; nn++)
                    {
                        var ps = new List<double>();
                        for (int k = 0; k <= nn; k++) ps.Add(t0 + k * l / nn);
                        if (ok(ps)) return ps;
                    }
                }
            // 3. ajuste a la reticula de la malla y barras sueltas en los extremos
            List<double> snapped = SnapPositions(tStartMin, t1, s, fc.Grid, fc.Clearance, out string why);
            if (snapped != null && snapped.Count > 0)
            {
                if (snapped[0] > tStartMax + _tol)
                {
                    double? t = LargestClear(fc, tStartMax, tStartMin, snapped[0] - 2 * d - Mm(1));
                    if (t != null) snapped.Insert(0, t.Value);
                }
                if (fc.OwnsEnd && t1 - snapped[snapped.Count - 1] > Mm(5))
                {
                    double? t = LargestClear(fc, t1, snapped[snapped.Count - 1] + 2 * d + Mm(1), t1);
                    if (t != null) snapped.Add(t.Value);
                }
                Warn("F4: en " + fc.Name + " las posiciones se ajustan a la reticula de la malla (" + snapped.Count + " barras)");
                return snapped;
            }
            Warn("F4: en " + fc.Name + " no se pueden esquivar las barras de la malla (" + why + ")");
            return Spread(tStartMax, t1, s, Cfg.F4.LayoutMode);
        }

        // -----------------------------------------------------------------
        // F5: horizontales en caras de foso de plataforma (por dentro de F4 y de las patas de F3)
        // -----------------------------------------------------------------

        /// <summary>
        /// Plano de F5: por dentro de F4 y de la pata MAS interior de F3 (la de las barras v,
        /// que se retranquean un diametro mas que las u), el mismo en todas las caras y en toda
        /// la altura.
        /// </summary>
        private double OffsetF5(RegionEdge edge) =>
            Cw + (Cfg.F4.Enabled ? D(Family.F4) : 0) + (Cfg.F3.Enabled ? D(Family.F3, "u") + D(Family.F3, "v") : 0) + 0.5 * D(Family.F5);

        private void BuildF5()
        {
            double d5 = D(Family.F5), s = Mm(Cfg.F5.SpacingMm), lap = Mm(Cfg.F5.LapMm);
            double zTop5 = Thickness - Ct - (Cfg.F3.Enabled ? D(Family.F3, "u") + D(Family.F3, "v") : 0) - 0.5 * d5;
            // las prolongaciones hacia las caras exteriores paran antes de la zona de patas de las mallas
            double zoneF5 = LegZone(Topo.DeepestFloor, zTop5);
            foreach (TopRegion reg in Topo.Regions.Where(r => r.Kind == RegionKind.Platform))
            {
                Outline2D clamp = Clamp(reg, d5, zoneF5);
                bool ring = false;
                if (Cfg.F5.Shape == "ring")
                {
                    List<Pt> full = FullRing(reg, EdgeKind.Recess, out int ringIdx, out bool isHole);
                    if (full != null)
                    {
                        RegionEdge any = reg.Edges.First(e => e.Ring == ringIdx);
                        double o = OffsetF5(any);
                        List<Pt> poly = RingPolyline(full, isHole, o, lap);
                        if (poly != null)
                        {
                            double zFloor = reg.Edges.Where(e => e.Ring == ringIdx).Max(e => Topo.Recesses[e.RecessIndex].ZFloor);
                            double z0 = zFloor + Cw + 0.5 * d5;
                            string face = "plataforma " + (reg.KindIndex + 1) + " anillo";
                            foreach (double z in LevelsBetween(z0, zTop5, s, Cfg.F5.LayoutMode))
                                Bars.Add(new PlannedBar { Family = Family.F5, D = d5, Normal = P3.Up, Face = face, RegionIndex = reg.Index, NominalSpacing = s, Points = poly.Select(p => new P3(p, z)).ToList() });
                            ring = true;
                        }
                    }
                    if (!ring) Warn("F5: las caras de foso de la plataforma " + (reg.KindIndex + 1) + " no cierran un anillo; se arma por tramos");
                }
                if (ring) continue;
                int faceNo = 0;
                foreach (RegionEdge edge in reg.Edges)
                {
                    if (edge.Kind != EdgeKind.Recess) continue;
                    faceNo++;
                    string face = "plataforma " + (reg.KindIndex + 1) + " cara " + faceNo;
                    double o = OffsetF5(edge);
                    double z0 = Topo.Recesses[edge.RecessIndex].ZFloor + Cw + 0.5 * d5;
                    if (zTop5 - z0 < -_tol) { Warn("F5: no hay altura en " + face); continue; }
                    if (!FaceExtent(edge, o, a => a.Kind == EdgeKind.Recess, OffsetF5, clamp, lap, out double tStart, out double tEnd)) { Warn("F5: no cabe en " + face); continue; }
                    if (tEnd - tStart < _minLen) { Skipped++; continue; }
                    Pt origin = Geometry2D.Add(edge.A, Geometry2D.Scale(edge.Normal, o));
                    Pt p0 = Geometry2D.Add(origin, Geometry2D.Scale(edge.Dir, tStart)), p1 = Geometry2D.Add(origin, Geometry2D.Scale(edge.Dir, tEnd));
                    // los tramos prolongados de caras perpendiculares se cruzarian en la esquina al mismo
                    // nivel: los de las caras a lo largo de v van un diametro mas abajo (contacto previsto)
                    double shift = edge.AlongU ? 0 : -d5;
                    foreach (double z in LevelsBetween(z0, zTop5, s, Cfg.F5.LayoutMode))
                        Bars.Add(new PlannedBar { Family = Family.F5, D = d5, Normal = P3.Up, Face = face, RegionIndex = reg.Index, NominalSpacing = s, Points = new List<P3> { new P3(p0, z + shift), new P3(p1, z + shift) } });
                }
            }
            MergeCollinear(Family.F5);
        }

        /// <summary>
        /// Tramos rectos de la misma familia, cota y recta que se solapan o se tocan (p. ej. las
        /// prolongaciones de dos fosos alineados sobre la misma cara) se funden en una sola barra.
        /// </summary>
        private void MergeCollinear(Family f)
        {
            var straight = Bars.Where(b => b.Family == f && b.Points.Count == 2).ToList();
            if (straight.Count < 2) return;
            var groups = new Dictionary<string, List<PlannedBar>>();
            foreach (PlannedBar b in straight)
            {
                P3 d = b.Points[1] - b.Points[0];
                bool alongU = Math.Abs(d.U) >= Math.Abs(d.V);
                if (Math.Abs(d.Z) > _tol) continue;
                if (alongU && Math.Abs(d.V) > _tol) continue;
                if (!alongU && Math.Abs(d.U) > _tol) continue;
                double line = alongU ? b.Points[0].V : b.Points[0].U;
                string key = (alongU ? "u" : "v") + ":" + Math.Round(b.Points[0].Z / _tol) + ":" + Math.Round(line / _tol) + ":" + Math.Round(b.D * 1e4);
                if (!groups.TryGetValue(key, out List<PlannedBar> list)) groups[key] = list = new List<PlannedBar>();
                list.Add(b);
            }
            foreach (List<PlannedBar> list in groups.Values)
            {
                if (list.Count < 2) continue;
                bool alongU = list[0].Points[1].U - list[0].Points[0].U != 0 && Math.Abs(list[0].Points[1].U - list[0].Points[0].U) >= Math.Abs(list[0].Points[1].V - list[0].Points[0].V);
                Func<PlannedBar, (double a, double b)> span = b =>
                {
                    double a = alongU ? b.Points[0].U : b.Points[0].V, c = alongU ? b.Points[1].U : b.Points[1].V;
                    return (Math.Min(a, c), Math.Max(a, c));
                };
                var sorted = list.OrderBy(b => span(b).a).ToList();
                var merged = new List<List<PlannedBar>>();
                double end = double.NegativeInfinity;
                foreach (PlannedBar b in sorted)
                {
                    (double a, double c) = span(b);
                    if (merged.Count > 0 && a <= end + _tol) { merged[merged.Count - 1].Add(b); end = Math.Max(end, c); }
                    else { merged.Add(new List<PlannedBar> { b }); end = c; }
                }
                foreach (List<PlannedBar> m in merged)
                {
                    if (m.Count < 2) continue;
                    double a = m.Min(b => span(b).a), c = m.Max(b => span(b).b);
                    PlannedBar first = m[0];
                    P3 p0 = first.Points[0], p1 = first.Points[1];
                    var nb = new PlannedBar
                    {
                        Family = f, D = first.D, Normal = first.Normal, NominalSpacing = first.NominalSpacing, RegionIndex = first.RegionIndex,
                        Face = string.Join(" + ", m.Select(b => b.Face).Distinct().OrderBy(x => x, StringComparer.Ordinal)),
                        Points = alongU
                            ? new List<P3> { new P3(a, p0.V, p0.Z), new P3(c, p0.V, p0.Z) }
                            : new List<P3> { new P3(p0.U, a, p0.Z), new P3(p0.U, c, p0.Z) }
                    };
                    foreach (PlannedBar b in m) Bars.Remove(b);
                    Bars.Add(nb);
                }
            }
        }

        // -----------------------------------------------------------------
        // Murete: F6 verticales, F7 horquillas, F8 horizontales
        // -----------------------------------------------------------------

        /// <summary>
        /// Posiciones (t a lo largo de la arista) de las barras de una familia de cara (F4, F6,
        /// F7). Regla de esquinas: la barra de esquina pertenece a UNA sola cara, la que llega a
        /// la esquina (su ultimo reparto, a recubrimiento + 1.5 d del vertice); la cara que sale
        /// de la esquina empieza su reparto a una separacion de esa barra o, si es demasiado
        /// corta para ello, termina antes con una sola barra en su otro extremo. Asi no se
        /// duplican barras en las esquinas.
        /// </summary>
        private List<double> FacePositions(TopRegion reg, RegionEdge edge, double d, double s, string mode, Func<RegionEdge, bool> sameFamily)
        {
            bool prevOwns = edge.Prev != null && sameFamily(edge.Prev) && edge.ConvexStart;
            double t1 = edge.Length - CornerInset(reg, edge, false, d);
            double t0, cornerBar = double.NaN;
            if (prevOwns)
            {
                // la barra de esquina de la cara anterior esta a su recubrimiento + d/2 de aquella cara:
                // esa es su distancia al vertice medida a lo largo de esta cara
                cornerBar = CoverOf(reg, edge.Prev) + 0.5 * d;
                t0 = cornerBar + s;
            }
            else t0 = CornerInset(reg, edge, true, d);
            if (t1 - t0 >= -_tol) return Spread(t0, t1, s, mode);
            var one = new List<double>();
            if (prevOwns && t1 - cornerBar >= 2 * d) one.Add(t1);   // cara corta: termina antes, sin duplicar la esquina
            return one;
        }

        private List<double> WallPositions(Family fam, TopRegion reg, RegionEdge edge, double d, double s, string mode)
        {
            List<double> pos = FacePositions(reg, edge, d, s, mode, e => e.Kind == EdgeKind.Exterior);
            bool prevOwns = edge.Prev != null && edge.Prev.Kind == EdgeKind.Exterior && edge.ConvexStart;
            double t0 = prevOwns ? CoverOf(reg, edge.Prev) + 0.5 * d + s : CornerInset(reg, edge, true, d);
            FaceRanges[(fam, edge)] = (t0, edge.Length - CornerInset(reg, edge, false, d));
            return pos;
        }

        /// <summary>Ancho del murete en una posicion de su cara exterior (sonda perpendicular) y tipo de la cara opuesta.</summary>
        private bool WallWidthAt(TopRegion reg, RegionEdge edge, double t, out double width, out EdgeKind opposite)
        {
            width = 0; opposite = EdgeKind.Free;
            Pt fp = edge.At(t);
            foreach (Span sp in Geometry2D.LineCut(reg.Outline, fp, edge.Normal, 0, _tol))
                if (sp.A <= 2 * _tol && sp.B > 2 * _tol)
                {
                    width = sp.B;
                    Pt exit = Geometry2D.Add(fp, Geometry2D.Scale(edge.Normal, width));
                    opposite = KindAt(exit, reg.Edges, e => 0, edge.Normal);
                    return true;
                }
            return false;
        }

        private void BuildF6()
        {
            double d6 = D(Family.F6), s = Mm(Cfg.F6.SpacingMm);
            double o6 = Cw + 0.5 * d6;
            double z0 = Cb + 0.5 * d6;
            double z1 = Thickness - Ct - (Cfg.F7.Enabled ? D(Family.F7) : 0) - 0.5 * d6;
            LayerZ["F6"] = z1;
            foreach (TopRegion reg in Topo.Regions.Where(r => r.Kind == RegionKind.Wall))
            {
                int faceNo = 0;
                foreach (RegionEdge edge in reg.Edges)
                {
                    if (edge.Kind != EdgeKind.Exterior) continue;
                    faceNo++;
                    string face = "murete " + (reg.KindIndex + 1) + " tramo " + faceNo;
                    List<double> pos = WallPositions(Family.F6, reg, edge, d6, s, Cfg.F6.LayoutMode);
                    _f6Positions[edge] = pos;
                    if (pos.Count == 0) { Warn("F6: " + face + " demasiado corto"); continue; }
                    P3 normal = new P3(edge.Dir.U, edge.Dir.V, 0);
                    foreach (double t in pos)
                    {
                        Pt b = Geometry2D.Add(edge.At(t), Geometry2D.Scale(edge.Normal, o6));
                        Bars.Add(new PlannedBar { Family = Family.F6, D = d6, Normal = normal, Face = face, RegionIndex = reg.Index, NominalSpacing = s, Edge = edge, Points = new List<P3> { new P3(b, z0), new P3(b, z1) } });
                    }
                }
            }
        }

        private void BuildF7()
        {
            double d7 = D(Family.F7), s = Mm(Cfg.F7.SpacingMm), leg = Mm(Cfg.F7.LegMm);
            double d6 = Cfg.F6.Enabled ? D(Family.F6) : 0, d8 = Cfg.F8.Enabled ? D(Family.F8) : 0;
            double zt = Thickness - Ct - 0.5 * d7;
            double zb = Math.Max(zt - leg, Cb + 0.5 * d7);
            if (zb > zt - leg + _tol) Warn("F7: las patas se acortan a " + ToMm(zt - zb) + " mm (recubrimiento inferior)");
            LayerZ["F7"] = zt;
            FamilyDiam fd = Diam.Get(Family.F7);
            double need = fd.TieBend + d7;   // distancia minima entre ejes de patas: diametro interior de doblado + un diametro
            foreach (TopRegion reg in Topo.Regions.Where(r => r.Kind == RegionKind.Wall))
            {
                int faceNo = 0;
                foreach (RegionEdge edge in reg.Edges)
                {
                    if (edge.Kind != EdgeKind.Exterior) continue;
                    faceNo++;
                    string face = "murete " + (reg.KindIndex + 1) + " tramo " + faceNo;
                    List<double> pos;
                    if (Cfg.F6.Enabled && _f6Positions.TryGetValue(edge, out List<double> f6)) pos = f6;
                    else pos = WallPositions(Family.F7, reg, edge, d7, s, Cfg.F7.LayoutMode);
                    if (FaceRanges.TryGetValue((Family.F6, edge), out (double t0, double t1) r6)) FaceRanges[(Family.F7, edge)] = r6;
                    if (Cfg.F7.Placement == "staggered" && pos.Count >= 2)
                        pos = pos.Take(pos.Count - 1).Select(t => t + 0.5 * s).Where(t => t < pos[pos.Count - 1]).ToList();
                    if (pos.Count == 0) continue;
                    P3 normal = new P3(edge.Dir.U, edge.Dir.V, 0);
                    // ancho nominal del tramo (sonda desde su punto medio): en las esquinas la sonda
                    // recorre el murete perpendicular y no encuentra la cara del foso; ahi se usa el nominal
                    bool hasNominal = WallWidthAt(reg, edge, 0.5 * edge.Length, out double nominalW, out EdgeKind nominalOpp) && nominalOpp == EdgeKind.Recess;
                    foreach (double t in pos)
                    {
                        bool ok = WallWidthAt(reg, edge, t, out double w, out EdgeKind opp) && opp == EdgeKind.Recess;
                        if (!ok && hasNominal) { w = nominalW; ok = true; }
                        if (!ok)
                        {
                            HairpinsSkipped++;
                            Warn("F7: en " + face + " la cara opuesta no es de foso; se omiten esas horquillas");
                            continue;
                        }
                        double xe = Cw + d6 + 0.5 * d7, xi = w - Cw - 0.5 * d7, cc = xi - xe;
                        double stack = 2 * Cw + d6 + d7 + d8 + d7;
                        if (stack > w + _tol)
                        {
                            Error = "F7: no caben las barras del murete de " + ToMm(w) + " mm en " + face + ": F6 (" + Dia(d6) + ") + pata de F7 (" + Dia(d7) +
                                    ") + F8 (" + Dia(d8) + ") + pata de F7 (" + Dia(d7) + ") + 2 x " + ToMm(Cw) + " de recubrimiento = " + ToMm(stack) + " mm";
                            return;
                        }
                        if (cc < need - _tol)
                        {
                            Error = "F7: la horquilla de Ø" + Dia(d7) + " no entra en el murete de " + ToMm(w) + " mm en " + face + ": entre ejes de patas quedan " +
                                    ToMm(cc) + " mm y el doblado minimo (Ø" + ToMm(fd.TieBend) + " interior + d) exige " + ToMm(need) + " mm";
                            return;
                        }
                        Pt fp = edge.At(t);
                        Pt pe = Geometry2D.Add(fp, Geometry2D.Scale(edge.Normal, xe)), pi = Geometry2D.Add(fp, Geometry2D.Scale(edge.Normal, xi));
                        Bars.Add(new PlannedBar
                        {
                            Family = Family.F7, D = d7, Normal = normal, Face = face, RegionIndex = reg.Index, NominalSpacing = s, Edge = edge,
                            Points = new List<P3> { new P3(pe, zb), new P3(pe, zt), new P3(pi, zt), new P3(pi, zb) }
                        });
                    }
                }
            }
        }

        private double OffsetF8Exterior => Cw + (Cfg.F6.Enabled ? D(Family.F6) : 0) + (Cfg.F7.Enabled ? D(Family.F7) : 0) + 0.5 * D(Family.F8);
        private double OffsetF8Recess => Cw + (Cfg.F7.Enabled ? D(Family.F7) : 0) + 0.5 * D(Family.F8);

        private void BuildF8()
        {
            double d8 = D(Family.F8), s = Mm(Cfg.F8.SpacingMm), lap = Mm(Cfg.F8.LapMm);
            // el primer nivel posible queda medio diametro libre por encima de F1 (sin contacto)
            double z0 = Cfg.F1.Enabled ? _f1Top + d8 : Cb + 0.5 * d8;
            double z1 = Thickness - Ct - (Cfg.F7.Enabled ? D(Family.F7) : 0) - 0.5 * d8;
            if (z1 - z0 < -_tol) { Warn("F8: no hay altura para las horizontales del murete"); return; }
            LayerZ["F8"] = z1;
            foreach (TopRegion reg in Topo.Regions.Where(r => r.Kind == RegionKind.Wall))
            {
                Outline2D clamp = Clamp(reg, d8);
                // capa 1: cara exterior (de zBase al tope)
                WallHorizontals(reg, clamp, EdgeKind.Exterior, OffsetF8Exterior, z0, z1, s, lap, d8, "");
                // capa 2: cara del foso, solo en la altura del murete
                if (Cfg.F8.Layers == 2)
                {
                    double zFloor = reg.Edges.Where(e => e.Kind == EdgeKind.Recess).Select(e => Topo.Recesses[e.RecessIndex].ZFloor).DefaultIfEmpty(Topo.DeepestFloor).Min();
                    WallHorizontals(reg, clamp, EdgeKind.Recess, OffsetF8Recess, zFloor + Cw + 0.5 * d8, z1, s, lap, d8, " interior");
                }
            }
            MergeCollinear(Family.F8);
        }

        private void WallHorizontals(TopRegion reg, Outline2D clamp, EdgeKind kind, double o, double z0, double z1, double s, double lap, double d8, string suffix)
        {
            if (z1 - z0 < -_tol) return;
            if (Cfg.F8.Shape == "ring")
            {
                List<Pt> full = FullRing(reg, kind, out int ringIdx, out bool isHole);
                List<Pt> poly = full != null ? RingPolyline(full, isHole, o, lap) : null;
                if (poly != null)
                {
                    string face = "murete " + (reg.KindIndex + 1) + " anillo" + suffix;
                    foreach (double z in LevelsBetween(z0, z1, s, Cfg.F8.LayoutMode))
                        Bars.Add(new PlannedBar { Family = Family.F8, D = d8, Normal = P3.Up, Face = face, RegionIndex = reg.Index, NominalSpacing = s, Points = poly.Select(p => new P3(p, z)).ToList() });
                    return;
                }
                Warn("F8: las caras del murete " + (reg.KindIndex + 1) + " no cierran un anillo; se arma por tramos");
            }
            int faceNo = 0;
            foreach (RegionEdge edge in reg.Edges)
            {
                if (edge.Kind != kind) continue;
                faceNo++;
                string face = "murete " + (reg.KindIndex + 1) + " tramo " + faceNo + suffix;
                if (!FaceExtent(edge, o, a => a.Kind == kind, a => o, clamp, lap, out double tStart, out double tEnd)) { Skipped++; continue; }
                if (tEnd - tStart < _minLen) { Skipped++; continue; }
                Pt origin = Geometry2D.Add(edge.A, Geometry2D.Scale(edge.Normal, o));
                Pt p0 = Geometry2D.Add(origin, Geometry2D.Scale(edge.Dir, tStart)), p1 = Geometry2D.Add(origin, Geometry2D.Scale(edge.Dir, tEnd));
                // los tramos de las caras a lo largo de v van un diametro mas abajo para no cruzarse en la esquina
                double shift = edge.AlongU ? 0 : -d8;
                foreach (double z in LevelsBetween(z0, z1, s, Cfg.F8.LayoutMode))
                    Bars.Add(new PlannedBar { Family = Family.F8, D = d8, Normal = P3.Up, Face = face, RegionIndex = reg.Index, NominalSpacing = s, Points = new List<P3> { new P3(p0, z + shift), new P3(p1, z + shift) } });
            }
        }

        // -----------------------------------------------------------------
        // Comprobacion de que todo queda dentro del hormigon (version 2D + cotas; la de
        // Revit, con el solido real, es la red de seguridad definitiva)
        // -----------------------------------------------------------------

        private void CheckInside()
        {
            double cmin = Math.Min(Math.Min(Cb, Ct), Math.Min(Ce, Cw));
            if (Cfg.F2.Enabled) cmin = Math.Min(cmin, Mm(Cfg.F2.BelowRecessFloorMm));
            foreach (PlannedBar b in Bars)
                for (int i = 0; i + 1 < b.Points.Count; i++)
                {
                    P3 p = b.Points[i], q = b.Points[i + 1];
                    for (int k = 0; k <= 4; k++)
                    {
                        double f = k / 4.0;
                        // un pelo hacia dentro en los extremos para no sondear justo en el filo
                        if (k == 0) f = 1e-3; else if (k == 4) f = 1 - 1e-3;
                        P3 x = p + (q - p) * f;
                        Pt pl = x.Plan;
                        if (!Topo.InBody(pl))
                        {
                            Error = Families.Code(b.Family) + (b.Layer != "" ? " (" + b.Layer + ")" : "") + ": la barra sale del contorno del bloque en u=" + ToMm(x.U) + " v=" + ToMm(x.V) + " mm [" + b.Face + "]";
                            return;
                        }
                        double? top = Topo.TopAt(pl);
                        if (top == null)
                        {
                            Error = Families.Code(b.Family) + ": no hay hormigon sobre u=" + ToMm(x.U) + " v=" + ToMm(x.V) + " mm (caras inclinadas?) [" + b.Face + "]";
                            return;
                        }
                        if (x.Z + 0.5 * b.D > top.Value - cmin + _tol)
                        {
                            Error = Families.Code(b.Family) + (b.Layer != "" ? " (" + b.Layer + ")" : "") + ": " + (b.HasLegs ? "la pata" : "la barra") +
                                    " sube por encima del hormigon en u=" + ToMm(x.U) + " v=" + ToMm(x.V) + " mm (barra a z=" + ToMm(x.Z) +
                                    " mm, tope local a z=" + ToMm(top.Value) + " mm, recubrimiento " + ToMm(cmin) + ") [" + b.Face + "]";
                            return;
                        }
                        if (x.Z - 0.5 * b.D < cmin - _tol && x.Z - 0.5 * b.D < Cb - _tol)
                        {
                            Error = Families.Code(b.Family) + ": la barra baja del recubrimiento inferior en u=" + ToMm(x.U) + " v=" + ToMm(x.V) + " mm (z=" + ToMm(x.Z) + " mm) [" + b.Face + "]";
                            return;
                        }
                    }
                }
        }

        // -----------------------------------------------------------------
        // Conjuntos (arrays)
        // -----------------------------------------------------------------

        /// <summary>
        /// Agrupa las barras de igual forma (misma familia, capa, diametro, normal y polilinea
        /// relativa) y de la misma cara, equiespaciadas a lo largo de su normal, en conjuntos.
        /// </summary>
        private void Group()
        {
            Groups.Clear();
            var clusters = new List<List<PlannedBar>>();
            foreach (PlannedBar b in Bars.OrderBy(b => (int)b.Family).ThenBy(b => b.Face, StringComparer.Ordinal).ThenBy(b => b.Along(b.Normal)))
            {
                List<PlannedBar> c = clusters.FirstOrDefault(x => x[0].Face == b.Face && x[0].SameShape(b, _tol));
                if (c == null) { c = new List<PlannedBar>(); clusters.Add(c); }
                c.Add(b);
            }
            foreach (List<PlannedBar> c in clusters)
                Groups.AddRange(Progressions(c.OrderBy(b => b.Along(b.Normal)).ToList()));
        }

        private List<BarGroup> Progressions(List<PlannedBar> sorted)
        {
            var result = new List<BarGroup>();
            BarGroup g = null;
            for (int i = 0; i < sorted.Count; i++)
            {
                PlannedBar b = sorted[i];
                if (g != null)
                {
                    double step = b.Along(b.Normal) - g.Bars[g.Count - 1].Along(b.Normal);
                    if (g.Count == 1 && step > _tol && (b.NominalSpacing <= 0 || step <= b.NominalSpacing + _tol))
                    {
                        // si la siguiente barra empieza un tramo de paso distinto, esta se queda suelta
                        bool nextRun = i + 2 < sorted.Count &&
                                       Math.Abs((sorted[i + 1].Along(b.Normal) - b.Along(b.Normal)) - step) > _tol &&
                                       Math.Abs((sorted[i + 2].Along(b.Normal) - sorted[i + 1].Along(b.Normal)) - (sorted[i + 1].Along(b.Normal) - b.Along(b.Normal))) <= _tol;
                        if (!nextRun) { g.Spacing = step; g.Bars.Add(b); continue; }
                    }
                    else if (g.Count >= 2 && Math.Abs(step - g.Spacing) <= _tol) { g.Bars.Add(b); continue; }
                }
                g = new BarGroup();
                g.Bars.Add(b);
                result.Add(g);
            }
            return result;
        }

        /// <summary>Conjunto al que pertenece una barra (null si aun no se ha agrupado).</summary>
        public BarGroup GroupOf(PlannedBar b) => Groups.FirstOrDefault(g => g.Bars.Contains(b));

        // -----------------------------------------------------------------
        // Separacion real maxima
        // -----------------------------------------------------------------

        /// <summary>Separaciones reales maximas por familia y violaciones de la nominal + tolerancia.</summary>
        public sealed class SpacingReport
        {
            public Dictionary<Family, double> MaxReal = new Dictionary<Family, double>();
            public Dictionary<Family, double> Nominal = new Dictionary<Family, double>();
            public Dictionary<Family, string> Where = new Dictionary<Family, string>();
            public List<string> Violations = new List<string>();
            public bool Ok => Violations.Count == 0;

            public string Describe()
            {
                var lines = new List<string>();
                foreach (Family f in Families.All)
                    if (MaxReal.TryGetValue(f, out double m))
                        lines.Add(Families.Code(f) + ": nominal " + ToMm(Nominal[f]) + " mm, maxima real " + (m * MmPerFt).ToString("0.0", CultureInfo.InvariantCulture) + " mm (" + Where[f] + ")");
                lines.Add(Ok ? "separaciones: todas dentro de la nominal + tolerancia" : "separaciones FUERA de tolerancia: " + string.Join(" | ", Violations));
                return string.Join(Environment.NewLine, lines);
            }
        }

        /// <summary>
        /// Separacion real entre barras consecutivas de cada conjunto y, en las familias de cara
        /// (F4, F6, F7), entre barras consecutivas a lo largo de cada cara INCLUIDOS los extremos:
        /// desde la ultima barra de una cara hasta la barra de esquina de la cara perpendicular
        /// (distancia en planta) o hasta el limite admisible de la cara. Ninguna puede superar la
        /// separacion nominal mas la tolerancia.
        /// </summary>
        public SpacingReport CheckSpacing(double toleranceMm = 5)
        {
            var rep = new SpacingReport();
            double tol = Mm(toleranceMm);
            Action<Family, double, string, double> note = (f, gap, where, nominal) =>
            {
                if (!rep.MaxReal.TryGetValue(f, out double cur) || gap > cur) { rep.MaxReal[f] = gap; rep.Where[f] = where; }
                rep.Nominal[f] = nominal;
                if (gap > nominal + tol)
                    rep.Violations.Add(Families.Code(f) + ": " + (gap * MmPerFt).ToString("0.0", CultureInfo.InvariantCulture) + " mm > " + ToMm(nominal) + " + " + toleranceMm + " en " + where);
            };
            foreach (BarGroup g in Groups)
            {
                double s = g.First.NominalSpacing;
                if (s <= 0 || g.Count < 2) continue;
                note(g.Family, g.Spacing, "conjunto " + g.Face + (g.Layer != "" ? " (" + g.Layer + ")" : ""), s);
            }
            foreach (Family f in new[] { Family.F4, Family.F6, Family.F7 })
            {
                var byEdge = Bars.Where(b => b.Family == f && b.Edge != null).GroupBy(b => b.Edge).ToDictionary(x => x.Key, x => x.ToList());
                foreach (var kv in byEdge)
                {
                    RegionEdge edge = kv.Key;
                    List<PlannedBar> bars = kv.Value;
                    double s = bars[0].NominalSpacing;
                    Func<PlannedBar, RegionEdge, double> tOf = (b, e) => Geometry2D.Dot(Geometry2D.Sub(b.First.Plan, e.A), e.Dir);
                    List<PlannedBar> sorted = bars.OrderBy(b => tOf(b, edge)).ToList();
                    for (int i = 0; i + 1 < sorted.Count; i++) note(f, tOf(sorted[i + 1], edge) - tOf(sorted[i], edge), "cara " + bars[0].Face, s);
                    bool hasRange = FaceRanges.TryGetValue((f, edge), out (double t0, double t1) range);
                    bool prevSame = edge.Prev != null && byEdge.ContainsKey(edge.Prev) && edge.ConvexStart;
                    bool nextSame = edge.Next != null && byEdge.ContainsKey(edge.Next) && edge.ConvexEnd;
                    if (prevSame)
                    {
                        PlannedBar corner = byEdge[edge.Prev].OrderByDescending(b => tOf(b, edge.Prev)).First();
                        PlannedBar first = sorted[0];
                        note(f, corner.First.Plan.DistanceTo(first.First.Plan), "esquina entre " + corner.Face + " y " + first.Face, s);
                    }
                    else if (hasRange) note(f, tOf(sorted[0], edge) - range.t0, "inicio de " + bars[0].Face, s);
                    if (!nextSame && hasRange) note(f, range.t1 - tOf(sorted[sorted.Count - 1], edge), "final de " + bars[0].Face, s);
                }
            }
            return rep;
        }
    }
}
