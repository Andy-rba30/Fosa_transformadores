using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Arba.Comun;

namespace BlockRebar
{
    /// <summary>Un conjunto (elemento Rebar) creado para un bloque.</summary>
    public sealed class CreatedSet
    {
        public ElementId Id;
        public string Name;
        public Family Family;
        public string Layer = "";
        /// <summary>Radio nominal de la barra (pies).</summary>
        public double Radius;
        public int Count;
        /// <summary>Suma de las longitudes de polilinea previstas de las barras del conjunto (pies).</summary>
        public double PlannedLength;
        /// <summary>Lo mismo con la deduccion de doblado (radio de doblado del tipo): lo que debe medir Revit.</summary>
        public double ExpectedLength;
        /// <summary>Leido de Revit tras regenerar.</summary>
        public int RealCount;
        public double RealLength;
    }

    /// <summary>Resultado del armado de un elemento.</summary>
    public sealed class BuildResult
    {
        public BlockPlan Plan;
        public List<CreatedSet> Created = new List<CreatedSet>();
        /// <summary>Barras que quedarian fuera del hormigon (o choques previstos). Si hay alguna, el elemento entero se deshace.</summary>
        public List<string> Rejected = new List<string>();
        /// <summary>Conjuntos que Revit no pudo crear.</summary>
        public List<string> Failed = new List<string>();
        public List<string> Warnings = new List<string>();
        /// <summary>Lineas de la comparacion entre lo previsto y lo leido de Revit.</summary>
        public List<string> Comparison = new List<string>();
        public bool ComparisonOk = true;
        public string RoundingNote = "";
        public int Bars;
        public Dictionary<Family, int> ByFamily = new Dictionary<Family, int>();
        public bool Safe => Rejected.Count == 0;
        public string Summary => Bars + " barras en " + Created.Count + " conjuntos (" +
                                 string.Join(", ", ByFamily.OrderBy(k => (int)k.Key).Select(k => Families.Code(k.Key) + " " + k.Value)) + ")" +
                                 (Plan != null ? ", " + Plan.TotalWeight.ToString("0", CultureInfo.InvariantCulture) + " kg previstos" : "");
    }

    /// <summary>
    /// Crea los Rebar del bloque a partir del BlockPlan (la misma clase pura que dibuja la
    /// lamina): cada conjunto con CreateFromCurves (polilinea con sus patas, sin ganchos),
    /// repartido como array (SetLayoutAsFixedNumber), con la Particion del contrato ARBA y
    /// "ARBA - Origen" = BLOQUES / "ARBA - Codigo" = F# para poder encontrarlo y borrarlo despues
    /// (ya no se escribe Comentarios; el comentario antiguo solo se lee como respaldo). Dos redes de seguridad: antes de crear,
    /// cada posicion prevista se comprueba dentro del hormigon; despues de crear y regenerar,
    /// se lee la geometria real (radios de doblado y todas las posiciones) y se vuelve a
    /// comprobar; ademas se comparan cantidades y longitudes reales con las previstas.
    /// </summary>
    public static class RebarGenerator
    {
        /// <summary>
        /// Marca antigua del plugin en el parametro Comentarios ("BlockRebar F#"). Desde el contrato ARBA ya no se
        /// escribe: solo sirve para reconocer (y borrar) conjuntos de modelos armados con versiones anteriores.
        /// </summary>
        public const string Marker = "BlockRebar";
        private const double MinSeg = 0.003;   // ~1 mm en pies
        /// <summary>Longitud de barra que se tolera fuera del solido al comprobar (pies, ~1 mm).</summary>
        private const double InsideTol = 0.0033;

        private static double ToMm(double ft) => BlockPlan.ToMm(ft);
        private static string M(double ft) => (ft * 0.3048).ToString("0.00", CultureInfo.InvariantCulture);

        private sealed class Ctx
        {
            public Document Doc;
            public HostAnalysis Item;
            public BlockFrame F;
            public AppConfig Cfg;
            public BuildResult Result;
            public BlockPlan Plan;
            public PlanDiameters Diam;
            public Dictionary<string, RebarBarType> Types = new Dictionary<string, RebarBarType>();
            public HashSet<string> Noted = new HashSet<string>();
        }

        // =================================================================
        // Armado de un bloque
        // =================================================================
        public static BuildResult Build(Document doc, HostAnalysis item, AppConfig cfg, IList<BarTypes.Info> allTypes)
        {
            BlockFrame f = item.Frame(cfg);
            if (f == null) throw new InvalidOperationException(item.Error ?? "sin geometria legible");
            if (f.Topology.Error != null) throw new InvalidOperationException(f.Topology.Error);

            PlanDiameters d = BarTypes.Diameters(allTypes, cfg, out List<string> missing, out _);
            if (missing.Count > 0) throw new InvalidOperationException("sin tipo de barra para " + string.Join(", ", missing));

            var c = new Ctx { Doc = doc, Item = item, F = f, Cfg = cfg, Result = new BuildResult(), Diam = d };
            foreach ((Family fam, string layer, string name) in cfg.BarTypesNeeded())
                c.Types[BarTypes.Key(fam, layer)] = BarTypes.FindBarType(doc, d.Label(fam, layer), Families.Code(fam) + (layer != "" ? " (" + layer + ")" : ""));

            c.Plan = BlockPlan.Build(f.Topology, cfg, d);
            if (c.Plan.Error != null) throw new InvalidOperationException(c.Plan.Error);
            c.Result.Plan = c.Plan;
            c.Result.Warnings.AddRange(c.Plan.Warnings);
            if (c.Plan.Skipped > 0) c.Result.Warnings.Add(c.Plan.Skipped + " tramo(s) demasiado cortos omitidos");
            if (c.Plan.HairpinsSkipped > 0) c.Result.Warnings.Add(c.Plan.HairpinsSkipped + " horquilla(s) omitidas (la cara opuesta no es de foso)");

            // --- RED DE SEGURIDAD DEL PLAN: choques y separaciones previstas ---
            ClashReport cr = ClashCheck.Check(c.Plan, BlockPlan.Mm(1));
            if (!cr.Ok)
            {
                c.Result.Rejected.Add("el armado previsto tiene " + cr.Clashes.Count + " choque(s) entre barras: " + cr.Describe(6).Replace(Environment.NewLine, " | "));
                return c.Result;
            }
            BlockPlan.SpacingReport sr = c.Plan.CheckSpacing(5);
            if (!sr.Ok) c.Result.Warnings.Add("separaciones por encima de la nominal: " + string.Join("; ", sr.Violations));

            int n = 0;
            foreach (BarGroup g in c.Plan.Groups)
            {
                n++;
                if (!Place(c, g, n)) break;
            }
            return c.Result;
        }

        // =================================================================
        // Colocacion con red de seguridad
        // =================================================================

        /// <summary>
        /// Crea un conjunto (array de barras iguales). Antes comprueba que cada barra prevista
        /// del conjunto queda dentro del hormigon (eje y cuatro fibras a medio diametro).
        /// False si algo se rechazo (el elemento entero se deshace).
        /// </summary>
        private static bool Place(Ctx c, BarGroup g, int index)
        {
            PlannedBar b = g.First;
            BlockFrame f = c.F;
            RebarBarType bt = c.Types[BarTypes.Key(b.Family, b.Layer)];
            double r = 0.5 * b.D;
            XYZ normal = Dir(f, b.Normal);
            string name = NameOf(b, g, index);
            List<Curve> curves = Curves(f, b);
            if (curves.Count == 0) { c.Result.Rejected.Add(name + ": sin longitud"); return false; }
            bool array = g.Count >= 2 && g.Spacing > MinSeg;

            // --- RED DE SEGURIDAD (1): geometria prevista de cada barra del conjunto, antes de crear nada ---
            for (int k = 0; k < (array ? g.Count : 1); k++)
            {
                List<Curve> bar = k == 0 ? curves : Curves(f, g.Bars[k]);
                if (!BarInside(f, f.CheckSolids, bar, r, out string why))
                {
                    c.Result.Rejected.Add(name + (k > 0 ? " (barra " + (k + 1) + " del conjunto)" : "") + ": " + why);
                    return false;
                }
            }

            RebarStyle style = b.Family == Family.F7 ? RebarStyle.StirrupTie : RebarStyle.Standard;
            Rebar rb = Create(c.Doc, c.Item.Host, style, bt, normal, curves, out string err);
            if (rb == null && style == RebarStyle.StirrupTie)
            {
                string firstErr = err;
                rb = Create(c.Doc, c.Item.Host, RebarStyle.Standard, bt, normal, curves, out err);
                if (rb != null)
                {
                    style = RebarStyle.Standard;
                    if (c.Noted.Add("F7 estilo")) c.Result.Warnings.Add("F7: Revit no acepto el estilo estribo/horquilla (" + firstErr + "); se crea como barra estandar (doblado estandar)");
                }
            }
            if (rb == null) { c.Result.Failed.Add(name + ": Revit no pudo crear la barra (" + err + ")"); return true; }

            try
            {
                if (array) rb.GetShapeDrivenAccessor().SetLayoutAsFixedNumber(g.Count, (g.Count - 1) * g.Spacing, true, true, true);
                else rb.GetShapeDrivenAccessor().SetLayoutAsSingle();
            }
            catch (Exception ex)
            {
                try { c.Doc.Delete(rb.Id); } catch { }
                c.Result.Failed.Add(name + ": no se pudo repartir el conjunto (" + ex.Message + ")");
                return true;
            }

            Finish(c.Doc, rb, c.Item.Host, c.Item.Partition(c.Cfg, Families.Name(b.Family), Families.Code(b.Family), b.Layer), b.Family);
            c.Result.Created.Add(new CreatedSet
            {
                Id = rb.Id, Name = name, Family = b.Family, Layer = b.Layer, Radius = r, Count = g.Count,
                PlannedLength = g.Bars.Sum(x => x.Length),
                ExpectedLength = g.Bars.Sum(x => ExpectedLength(x, c.Diam, style))
            });
            c.Result.Bars += g.Count;
            c.Result.ByFamily[b.Family] = (c.Result.ByFamily.TryGetValue(b.Family, out int prev) ? prev : 0) + g.Count;
            return true;
        }

        private static string NameOf(PlannedBar b, BarGroup g, int index)
        {
            return "conjunto " + index + " " + Families.Code(b.Family) + (b.Layer != "" ? "(" + b.Layer + ")" : "") + " " + Families.Name(b.Family) +
                   (b.Face != "" ? " [" + b.Face + "]" : "") + ", L=" + ToMm(b.Length) + " mm" +
                   (g.Count > 1 ? " (" + g.Count + " barras cada " + ToMm(g.Spacing) + " mm)" : "") + ", desde " + b.First;
        }

        /// <summary>Direccion local (u, v, z) a direccion del modelo.</summary>
        private static XYZ Dir(BlockFrame f, P3 n) => (f.DirU * n.U + f.DirV * n.V + XYZ.BasisZ * n.Z).Normalize();

        private static List<Curve> Curves(BlockFrame f, PlannedBar b)
        {
            var list = new List<Curve>();
            for (int i = 0; i + 1 < b.Points.Count; i++) AddLine(list, f.World(b.Points[i]), f.World(b.Points[i + 1]));
            return list;
        }

        private static void AddLine(List<Curve> list, XYZ a, XYZ b)
        {
            if (a.DistanceTo(b) > MinSeg) list.Add(Line.CreateBound(a, b));
        }

        /// <summary>
        /// Longitud que debe medir la barra real: la polilinea menos la deduccion de cada
        /// doblez (2 R tan(a/2) - R a, con R = radio de doblado del eje = medio diametro
        /// interior de doblado del tipo + medio diametro de barra).
        /// </summary>
        public static double ExpectedLength(PlannedBar b, PlanDiameters d, RebarStyle style)
        {
            FamilyDiam fd = d.Get(b.Family, b.Layer);
            double inside = style == RebarStyle.StirrupTie
                ? (fd != null ? fd.TieBend : 4 * b.D)
                : (fd != null && fd.BendInside > 0 ? fd.BendInside : 6 * b.D);
            double R = 0.5 * inside + 0.5 * b.D;
            double len = b.Length;
            for (int i = 1; i + 1 < b.Points.Count; i++)
            {
                P3 a = b.Points[i] - b.Points[i - 1], c = b.Points[i + 1] - b.Points[i];
                if (a.Length < 1e-9 || c.Length < 1e-9) continue;
                double cos = Math.Max(-1, Math.Min(1, a.Dot(c) / (a.Length * c.Length)));
                double theta = Math.Acos(cos);
                if (theta < 1e-6) continue;
                len -= 2 * R * Math.Tan(theta / 2) - R * theta;
            }
            return len;
        }

        /// <summary>
        /// RED DE SEGURIDAD (2): tras crear y regenerar, se lee la geometria REAL de cada barra
        /// de cada conjunto tal y como la ha colocado Revit (radios de doblado y todas las
        /// posiciones del array) y se comprueba entera contra el hormigon del bloque. De paso
        /// se cuentan las barras y se mide su longitud real para compararlas con lo previsto.
        /// </summary>
        public static void VerifyCreated(Document doc, HostAnalysis item, AppConfig cfg, BuildResult res)
        {
            BlockFrame f = item.Frame(cfg);
            foreach (CreatedSet cs in res.Created)
            {
                var rb = doc.GetElement(cs.Id) as Rebar;
                if (rb == null) { res.Rejected.Add(cs.Name + ": el conjunto no existe tras regenerar"); continue; }
                if (!RealInside(f, f.CheckSolids, rb, cs, out string why)) res.Rejected.Add(cs.Name + ": " + why);
            }
            res.RoundingNote = RoundingNote(doc);
            if (res.Safe) Compare(res);
        }

        private static bool RealInside(BlockFrame f, List<Solid> solids, Rebar rb, CreatedSet cs, out string why)
        {
            why = null;
            int n;
            try { n = rb.NumberOfBarPositions; }
            catch (Exception ex) { why = "no se pudo leer el conjunto (" + ex.Message + ")"; return false; }
            cs.RealCount = 0; cs.RealLength = 0;
            for (int k = 0; k < n; k++)
            {
                IList<Curve> cl;
                try
                {
                    if (!rb.DoesBarExistAtPosition(k)) continue;
                    cl = rb.GetCenterlineCurves(false, false, false, MultiplanarOption.IncludeOnlyPlanarCurves, k);
                }
                catch (Exception ex) { why = "barra " + (k + 1) + " de " + n + ": no se pudo leer su geometria (" + ex.Message + ")"; return false; }
                if (cl == null || cl.Count == 0) { why = "barra " + (k + 1) + " de " + n + ": sin geometria"; return false; }
                cs.RealCount++;
                cs.RealLength += cl.Sum(cv => cv.Length);
                if (!BarInside(f, solids, cl, cs.Radius, out string w)) { why = "barra " + (k + 1) + " de " + n + ": " + w; return false; }
            }
            return true;
        }

        /// <summary>Compara, por familia, barras y longitudes previstas con las leidas de Revit.</summary>
        public static void Compare(BuildResult res)
        {
            res.Comparison.Clear();
            res.ComparisonOk = true;
            BlockPlan plan = res.Plan;
            if (plan == null) return;
            res.Comparison.Add(string.Format("{0,-4} {1,8} {2,10} {3,12} {4,8} {5,10}   {6}", "Fam", "previsto", "L prev.(m)", "L esper.(m)", "Revit", "L Revit(m)", "estado"));
            foreach (Family fam in plan.UsedFamilies)
            {
                var sets = res.Created.Where(s => s.Family == fam).ToList();
                int planned = plan.CountOf(fam);
                double plannedLen = plan.LengthOf(fam);
                double expected = sets.Sum(s => s.ExpectedLength);
                int real = sets.Sum(s => s.RealCount);
                double realLen = sets.Sum(s => s.RealLength);
                var notes = new List<string>();
                if (real != planned) { notes.Add("DIFERENCIA de barras: " + real + " frente a " + planned); res.ComparisonOk = false; }
                double tol = Math.Max(0.01 * expected, real * BlockPlan.Mm(5));
                if (Math.Abs(realLen - expected) > tol)
                {
                    notes.Add("DIFERENCIA de longitud: " + ToMm(realLen - expected) + " mm (tolerancia " + ToMm(tol) + ")");
                    res.ComparisonOk = false;
                }
                res.Comparison.Add(string.Format("{0,-4} {1,8} {2,10} {3,12} {4,8} {5,10}   {6}", Families.Code(fam), planned,
                    (plannedLen * 0.3048).ToString("0.00", CultureInfo.InvariantCulture), (expected * 0.3048).ToString("0.00", CultureInfo.InvariantCulture),
                    real, (realLen * 0.3048).ToString("0.00", CultureInfo.InvariantCulture), notes.Count == 0 ? "OK" : string.Join("; ", notes)));
            }
            res.Comparison.Add("L prev. = polilinea prevista; L esper. = con la deduccion de doblado del tipo (lo que debe medir Revit); tolerancia 1 % o 5 mm por barra.");
            // datos para explicar las diferencias: diametros de doblado leidos de cada tipo y redondeo de longitudes del proyecto
            foreach (var kv in plan.UsedFamilies.SelectMany(f => plan.Bars.Where(b => b.Family == f).Select(b => b.Layer).Distinct().Select(l => (f, l))))
            {
                FamilyDiam fd = plan.Diam.Get(kv.f, kv.l);
                if (fd == null) continue;
                res.Comparison.Add("  " + Families.Code(kv.f) + (kv.l != "" ? "(" + kv.l + ")" : "") + " tipo " + fd.Label + ": d = " + BlockPlan.Dia(fd.D) + " mm, doblado estandar leido = " +
                                   (fd.BendInside > 0 ? ToMm(fd.BendInside) + " mm" : "0 (no leido: se asume 6 d = " + ToMm(6 * fd.D) + " mm)") + ", doblado de estribo = " +
                                   (fd.TieBendInside > 0 ? ToMm(fd.TieBendInside) + " mm" : "0 (se asume " + ToMm(fd.TieBend) + " mm)"));
            }
            if (!string.IsNullOrEmpty(res.RoundingNote)) res.Comparison.Add("  " + res.RoundingNote);
        }

        /// <summary>Redondeo de longitudes de barra del proyecto (Configuracion de refuerzo): explica diferencias de unos mm por barra.</summary>
        public static string RoundingNote(Document doc)
        {
            try
            {
                ReinforcementSettings rs = ReinforcementSettings.GetReinforcementSettings(doc);
                RebarRoundingManager rm = rs.GetRebarRoundingManager();
                double seg = UnitUtils.ConvertFromInternalUnits(rm.ApplicableSegmentLengthRounding, UnitTypeId.Millimeters);
                double tot = UnitUtils.ConvertFromInternalUnits(rm.ApplicableTotalLengthRounding, UnitTypeId.Millimeters);
                return "redondeo de longitudes del proyecto: tramos a " + seg.ToString("0.#", CultureInfo.InvariantCulture) + " mm (" + rm.ApplicableSegmentLengthRoundingMethod + "), total a " +
                       tot.ToString("0.#", CultureInfo.InvariantCulture) + " mm (" + rm.ApplicableTotalLengthRoundingMethod + "), origen " + rm.ApplicableReinforcementRoundingSource +
                       (seg > 0 || tot > 0 ? ". Con redondeo activo, la longitud real de cada tramo/barra puede diferir hasta ese valor de la geometrica." : ".");
            }
            catch (Exception ex) { return "redondeo de longitudes: no se pudo leer (" + ex.Message + ")"; }
        }

        /// <summary>
        /// True si toda la barra (tramos rectos y dobleces) esta dentro del hormigon. Ademas del
        /// eje se comprueban cuatro fibras extremas (eje desplazado +-r en las dos direcciones
        /// perpendiculares a cada tramo), asi una barra tangente a una cara o con medio
        /// diametro fuera tambien falla.
        /// </summary>
        private static bool BarInside(BlockFrame f, List<Solid> solids, IList<Curve> curves, double r, out string why)
        {
            why = null;
            foreach (Curve cv in curves)
            {
                if (cv.Length < MinSeg) continue;
                XYZ dir = (cv.GetEndPoint(1) - cv.GetEndPoint(0));
                if (dir.GetLength() < 1e-9) dir = XYZ.BasisX; else dir = dir.Normalize();
                XYZ s1 = XYZ.BasisZ.CrossProduct(dir);
                if (s1.GetLength() < 1e-6) s1 = f.DirU; else s1 = s1.Normalize();
                XYZ s2 = dir.CrossProduct(s1).Normalize();
                var shifts = new List<XYZ> { XYZ.Zero, s1 * r, s1 * -r, s2 * r, s2 * -r };
                foreach (XYZ sh in shifts)
                {
                    Curve probe = sh.IsZeroLength() ? cv : cv.CreateTransformed(Transform.CreateTranslation(sh));
                    if (!CurveInside(solids, probe, out double outside))
                    {
                        why = "queda fuera del hormigon (" + ToMm(outside) + " mm de barra fuera; tramo de " +
                              f.LocalMm(cv.GetEndPoint(0)) + " a " + f.LocalMm(cv.GetEndPoint(1)) + " mm)";
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Longitud de la curva que queda fuera del hormigon: se unen los tramos interiores de
        /// todos los solidos (por sus parametros) y se resta de la longitud total. No
        /// verificable cuenta como fuera.
        /// </summary>
        private static bool CurveInside(List<Solid> solids, Curve cv, out double outsideLen)
        {
            outsideLen = cv.Length;
            double p0 = cv.GetEndParameter(0), p1 = cv.GetEndParameter(1);
            if (p1 - p0 < 1e-12) { outsideLen = 0; return true; }
            var parts = new List<(double a, double b)>();
            bool any = false;
            foreach (Solid solid in solids)
            {
                try
                {
                    var opt = new SolidCurveIntersectionOptions { ResultType = SolidCurveIntersectionMode.CurveSegmentsInside };
                    SolidCurveIntersection ix = solid.IntersectWithCurve(cv, opt);
                    any = true;
                    if (ix == null) continue;
                    for (int i = 0; i < ix.SegmentCount; i++)
                    {
                        CurveExtents ex = ix.GetCurveSegmentExtents(i);
                        parts.Add((Math.Min(ex.StartParameter, ex.EndParameter), Math.Max(ex.StartParameter, ex.EndParameter)));
                    }
                }
                catch { }
            }
            if (!any) return false;
            double inside = Geometry2D.Merge(parts, 0).Sum(p => p.b - p.a) / (p1 - p0) * cv.Length;
            outsideLen = Math.Max(0, cv.Length - inside);
            return outsideLen <= InsideTol;
        }

        // =================================================================
        // Creacion, marca del plugin y borrado
        // =================================================================
        private static Rebar Create(Document doc, Element host, RebarStyle style, RebarBarType bt, XYZ normal, IList<Curve> curves, out string err)
        {
            err = null;
            try
            {
                // Revit 2027: ganchos y tratamientos de extremo van en BarTerminationsData (aqui sin ganchos: las patas son tramos de la polilinea)
                using (BarTerminationsData term = new BarTerminationsData(doc))
                {
                    term.TerminationOrientationAtStart = RebarTerminationOrientation.Left;
                    term.TerminationOrientationAtEnd = RebarTerminationOrientation.Left;
                    return Rebar.CreateFromCurves(doc, style, bt, host, normal.Normalize(), curves, term, true, true);
                }
            }
            catch (Exception ex)
            {
                err = ex.Message;
                return null;
            }
        }

        /// <summary>
        /// Particion del contrato (ArbaPartition.Write: NUMBER_PARTITION_PARAM y, si no, por nombre en ingles y
        /// espanol), "ARBA - Origen" = BLOQUES, "ARBA - Codigo" = F# y "Metrado - Elemento" = CIMIENTOS
        /// (ArbaOrigin.WriteFor). Los parametros compartidos deben existir (ArbaSharedParams.EnsureAll en el comando).
        /// </summary>
        private static void Finish(Document doc, Rebar r, Element host, string partition, Family family)
        {
            try { ArbaPartition.Write(r, partition); } catch (Exception ex) { Log.Error("Finish particion", ex); }
            try { ArbaOrigin.WriteFor(r, host, ArbaContract.Bloques, Families.Code(family)); } catch (Exception ex) { Log.Error("Finish origen", ex); }
            try { r.SetUnobscuredInView(doc.ActiveView, true); } catch { }
        }

        /// <summary>
        /// Conjuntos de armadura del elemento creados por el plugin: los que llevan "ARBA - Origen" = BLOQUES
        /// (contrato) mas, por compatibilidad con modelos no migrados, los que llevan el comentario antiguo.
        /// </summary>
        public static List<ElementId> FindPluginRebars(Document doc, Element host)
        {
            var ids = new List<ElementId>();
            try
            {
                foreach (Element e in ArbaOrigin.Find(doc, ArbaContract.Bloques, host))
                    if (ArbaPartition.IsRebar(e) && !ids.Contains(e.Id)) ids.Add(e.Id);
            }
            catch (Exception ex) { Log.Error("FindPluginRebars (origen)", ex); }
            foreach (ElementId id in FindLegacyRebars(doc, host))
                if (!ids.Contains(id)) ids.Add(id);
            return ids;
        }

        /// <summary>Respaldo: conjuntos del elemento con el comentario antiguo del plugin ("BlockRebar F#").</summary>
        public static List<ElementId> FindLegacyRebars(Document doc, Element host)
        {
            var ids = new List<ElementId>();
            try
            {
                RebarHostData hd = RebarHostData.GetRebarHostData(host);
                if (hd == null) return ids;
                foreach (Rebar rb in hd.GetRebarsInHost())
                {
                    string cm = "";
                    try { cm = rb.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? ""; } catch { }
                    if (cm.StartsWith(Marker, StringComparison.OrdinalIgnoreCase)) ids.Add(rb.Id);
                }
            }
            catch (Exception ex) { Log.Error("FindLegacyRebars", ex); }
            return ids;
        }

        /// <summary>
        /// Borra los conjuntos del plugin del elemento (dentro de una transaccion abierta): los del contrato
        /// (ArbaOrigin.Find filtrado a armaduras: las rejillas y angulos los borra GridGenerator) y los del comentario antiguo. Devuelve cuantos conjuntos; <paramref name="bars"/> suma las barras.
        /// </summary>
        public static int DeletePluginRebars(Document doc, Element host, out int bars)
        {
            int n = 0; bars = 0;
            try
            {
                // solo armaduras: las rejillas y angulos con origen BLOQUES los borra GridGenerator
                foreach (Element e in ArbaOrigin.Find(doc, ArbaContract.Bloques, host).Where(ArbaPartition.IsRebar).ToList())
                {
                    try
                    {
                        if (e is Rebar rb) { try { bars += rb.NumberOfBarPositions; } catch { } }
                        doc.Delete(e.Id);
                        n++;
                    }
                    catch (Exception ex) { Log.Error("DeletePluginRebars (origen) " + e.Id, ex); }
                }
            }
            catch (Exception ex) { Log.Error("DeletePluginRebars (origen)", ex); }
            foreach (ElementId id in FindLegacyRebars(doc, host))
            {
                if (doc.GetElement(id) == null) continue;   // ya borrado por el origen
                try
                {
                    if (doc.GetElement(id) is Rebar rb) { try { bars += rb.NumberOfBarPositions; } catch { } }
                    doc.Delete(id);
                    n++;
                }
                catch (Exception ex) { Log.Error("DeletePluginRebars " + id, ex); }
            }
            return n;
        }
    }
}
