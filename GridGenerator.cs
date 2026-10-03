using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace BlockRebar
{
    /// <summary>
    /// Capa Revit de las rejillas de foso: angulos de borde como Structural Framing (viga
    /// por linea, con el foso siempre a la izquierda de la viga), rejillas como Generic Model
    /// (Largo, Ancho, Espesor de instancia), familia de rejilla generada desde la plantilla
    /// Generic Model, marca del plugin en Comentarios, busqueda y borrado.
    /// </summary>
    public static class GridGenerator
    {
        public const string MarkerAngle = RebarGenerator.Marker + " ANGLE";
        public const string MarkerGrid = RebarGenerator.Marker + " GRID";
        private static double Mm(double mm) => BlockPlan.Mm(mm);
        private static double ToMm(double ft) => BlockPlan.ToMm(ft);

        // =================================================================
        // Tipos de angulo (Structural Framing)
        // =================================================================
        public sealed class AngleSymbolInfo
        {
            public FamilySymbol Symbol;
            public string FamilyName = "", TypeName = "";
            public string Display => FamilyName + " : " + TypeName;
            public double KgPerM;
            public string KgSource = "";
        }

        public static List<AngleSymbolInfo> ReadAngleSymbols(Document doc, double kgPerMDefault)
        {
            var list = new List<AngleSymbolInfo>();
            try
            {
                foreach (FamilySymbol fs in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFraming).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>())
                {
                    var i = new AngleSymbolInfo { Symbol = fs, FamilyName = fs.Family?.Name ?? fs.FamilyName ?? "", TypeName = fs.Name };
                    i.KgPerM = ReadKgPerM(fs, kgPerMDefault, out i.KgSource);
                    list.Add(i);
                }
            }
            catch (Exception ex) { Log.Error("ReadAngleSymbols", ex); }
            return list.OrderBy(i => i.FamilyName, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.TypeName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Peso lineal del tipo: parametro de tipo "W". Si es masa por longitud se convierte con
        /// la API (kg/m); si es un numero sin unidad (catalogo: 4.10) se toma como lb/ft; si no
        /// se puede leer o no es plausible, el valor por defecto.
        /// </summary>
        public static double ReadKgPerM(FamilySymbol fs, double def, out string source)
        {
            source = "valor por defecto " + def.ToString("0.00", CultureInfo.InvariantCulture) + " kg/m (sin parametro W legible)";
            try
            {
                Parameter p = fs.LookupParameter("W") ?? fs.LookupParameter("w") ?? fs.LookupParameter("Peso") ?? fs.LookupParameter("Weight");
                if (p == null) return def;
                double kg = 0;
                string how = "";
                if (p.StorageType == StorageType.Double)
                {
                    ForgeTypeId spec = null;
                    try { spec = p.Definition.GetDataType(); } catch { }
                    if (spec != null && spec == SpecTypeId.MassPerUnitLength)
                    {
                        kg = UnitUtils.ConvertFromInternalUnits(p.AsDouble(), UnitTypeId.KilogramsPerMeter);
                        how = "parametro W (masa por longitud, convertido con la API)";
                    }
                    else
                    {
                        kg = p.AsDouble() * 1.488164;   // lb/ft -> kg/m
                        how = "parametro W numerico " + p.AsDouble().ToString("0.00", CultureInfo.InvariantCulture) + " tomado como lb/ft";
                    }
                }
                else if (p.StorageType == StorageType.String)
                {
                    string txt = (p.AsString() ?? "").Trim().Replace(',', '.');
                    if (!double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return def;
                    kg = v * 1.488164;
                    how = "parametro W de texto \"" + txt + "\" tomado como lb/ft";
                }
                else if (p.StorageType == StorageType.Integer)
                {
                    kg = p.AsInteger() * 1.488164;
                    how = "parametro W entero tomado como lb/ft";
                }
                else return def;
                if (kg < 0.3 || kg > 500) { source = "valor por defecto (W = " + kg.ToString("0.00", CultureInfo.InvariantCulture) + " kg/m no es plausible)"; return def; }
                source = how;
                return kg;
            }
            catch { return def; }
        }

        /// <summary>
        /// Candidatos para familia + tipo configurados: exacto "Familia : Tipo"; si no, tipos cuyo
        /// nombre de familia y de tipo coinciden (exacto o fragmento) con los configurados.
        /// </summary>
        public static List<AngleSymbolInfo> Candidates(IList<AngleSymbolInfo> all, string familyName, string typeName)
        {
            if (all == null || all.Count == 0) return new List<AngleSymbolInfo>();
            string full = (familyName ?? "").Trim() + " : " + (typeName ?? "").Trim();
            List<string> exact = NameMatch.Candidates(all.Select(a => a.Display), full);
            if (exact.Count == 1) return new List<AngleSymbolInfo> { all.First(a => a.Display == exact[0]) };
            List<string> fams = NameMatch.Candidates(all.Select(a => a.FamilyName).Distinct(), familyName);
            var inFam = all.Where(a => fams.Contains(a.FamilyName)).ToList();
            if (inFam.Count == 0) return inFam;
            List<string> types = NameMatch.Candidates(inFam.Select(a => a.TypeName).Distinct(), typeName);
            return inFam.Where(a => types.Contains(a.TypeName)).ToList();
        }

        // =================================================================
        // Familia de rejilla (Generic Model)
        // =================================================================
        public static Autodesk.Revit.DB.Family FindGridFamily(Document doc, string name)
        {
            try
            {
                var fams = new FilteredElementCollector(doc).OfClass(typeof(Autodesk.Revit.DB.Family)).Cast<Autodesk.Revit.DB.Family>()
                    .Where(f => f.FamilyCategory != null && f.FamilyCategory.Id.Value == (long)BuiltInCategory.OST_GenericModel).ToList();
                string m = NameMatch.Unique(fams.Select(f => f.Name), name);
                return m == null ? null : fams.First(f => f.Name == m);
            }
            catch (Exception ex) { Log.Error("FindGridFamily", ex); return null; }
        }

        public static List<FamilySymbol> GridSymbols(Document doc, Autodesk.Revit.DB.Family fam) =>
            fam == null ? new List<FamilySymbol>() : fam.GetFamilySymbolIds().Select(id => doc.GetElement(id) as FamilySymbol).Where(s => s != null).OrderBy(s => s.Name).ToList();

        /// <summary>Tipo de la familia para el tipo de rejilla; si no existe se duplica el primero con su nombre y su peso por m2.</summary>
        public static FamilySymbol GridSymbolFor(Document doc, Autodesk.Revit.DB.Family fam, GridTypeCfg type, List<string> notes)
        {
            List<FamilySymbol> all = GridSymbols(doc, fam);
            if (all.Count == 0) throw new InvalidOperationException("la familia \"" + fam.Name + "\" no tiene tipos");
            string m = NameMatch.Unique(all.Select(s => s.Name), type.Name);
            FamilySymbol fs = m != null ? all.First(s => s.Name == m) : null;
            if (fs == null)
            {
                fs = all[0].Duplicate(type.Name) as FamilySymbol;
                notes.Add("tipo de rejilla \"" + type.Name + "\" creado en la familia \"" + fam.Name + "\"");
            }
            try
            {
                Parameter w = fs.LookupParameter("Peso por m2");
                if (w != null && !w.IsReadOnly && w.StorageType == StorageType.Double)
                    w.Set(UnitUtils.ConvertToInternalUnits(type.KgPerM2, UnitTypeId.KilogramsPerSquareMeter));
            }
            catch { }
            if (!fs.IsActive) fs.Activate();
            return fs;
        }

        private sealed class LoadOptions : IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues) { overwriteParameterValues = true; return true; }
            public bool OnSharedFamilyFound(Autodesk.Revit.DB.Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
            { source = FamilySource.Family; overwriteParameterValues = true; return true; }
        }

        /// <summary>Plantilla Generic Model: la configurada, o la primera de la carpeta de plantillas cuyo nombre lo indique.</summary>
        public static string FindTemplate(Application app, string configured, out string warn)
        {
            warn = null;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (File.Exists(configured)) return configured;
                warn = "la plantilla configurada no existe: " + configured;
            }
            string dir = "";
            try { dir = app.FamilyTemplatePath; } catch { }
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) { warn = (warn != null ? warn + "; " : "") + "no se encontro la carpeta de plantillas de familia de Revit"; return null; }
            string[] bad = { "face", "cara", "adapt", "line", "linea", "línea", "pattern", "patron", "patrón", "wall", "muro", "ceiling", "techo", "floor", "suelo", "roof", "cubierta", "two level", "dos niveles", "based", "basado", "basada" };
            var files = Directory.EnumerateFiles(dir, "*.rft", SearchOption.AllDirectories)
                .Where(f => { string n = Path.GetFileNameWithoutExtension(f).ToLowerInvariant(); return (n.Contains("generic model") || n.Contains("modelo gen")) && !bad.Any(b => n.Contains(b)); })
                .OrderByDescending(f => { string n = Path.GetFileNameWithoutExtension(f).ToLowerInvariant(); return (n.Contains("metric") || n.Contains("métric") || n.Contains("metric") ? 2 : 0) + (n.Length < 30 ? 1 : 0); })
                .ToList();
            if (files.Count == 0) { warn = (warn != null ? warn + "; " : "") + "no hay ninguna plantilla \"Generic Model\" en " + dir + " (indica la ruta en grids.familyTemplatePath)"; return null; }
            return files[0];
        }

        /// <summary>
        /// Crea la familia de rejilla (extrusion rectangular con Largo, Ancho y Espesor de
        /// instancia; Peso por m2 de tipo; Peso = Largo x Ancho x Peso por m2; material
        /// "Rejilla" con patron de modelo de lineas cada 30 mm; un tipo por cada tipo de la
        /// lista), la guarda junto a la DLL y la carga en el proyecto (transaccion abierta).
        /// </summary>
        public static Autodesk.Revit.DB.Family CreateGridFamily(Application app, Document doc, GridsCfg cfg, List<string> notes)
        {
            string template = FindTemplate(app, cfg.FamilyTemplatePath, out string warn);
            if (warn != null) notes.Add(warn);
            if (template == null) throw new InvalidOperationException("sin plantilla Generic Model");
            notes.Add("plantilla: " + template);

            Document fam = app.NewFamilyDocument(template);
            string path;
            try
            {
                using (Transaction t = new Transaction(fam, "Rejilla"))
                {
                    t.Start();
                    BuildGridFamily(fam, cfg, notes);
                    t.Commit();
                }
                notes.Add("paso 11 (guardar)");
                string dir = Path.GetDirectoryName(AppConfig.ConfigPath()) ?? Path.GetTempPath();
                path = Path.Combine(dir, SafeFile(cfg.FamilyName) + ".rfa");
                try { fam.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = true }); }
                catch (Exception ex)
                {
                    path = Path.Combine(Path.GetTempPath(), SafeFile(cfg.FamilyName) + ".rfa");
                    notes.Add("  no se pudo guardar junto a la DLL (" + ex.Message + "); se guarda en " + path);
                    fam.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = true });
                }
                notes.Add("  familia guardada en " + path);
            }
            finally { try { fam.Close(false); } catch { } }

            // la carga va dentro de la transaccion del proyecto: si falla, se deshace y no queda nada
            notes.Add("paso 12 (cargar en el proyecto)");
            if (!doc.LoadFamily(path, new LoadOptions(), out Autodesk.Revit.DB.Family family) || family == null)
                throw new InvalidOperationException("fallo en paso 12 (cargar): Revit no cargo la familia " + path);
            // asegurar que la familia cargada se llama como en la configuracion
            if (!string.Equals(family.Name, cfg.FamilyName, StringComparison.OrdinalIgnoreCase))
            {
                try { family.Name = cfg.FamilyName; } catch { }
            }
            notes.Add("familia \"" + family.Name + "\" cargada con " + family.GetFamilySymbolIds().Count + " tipo(s)");
            return family;
        }

        private static string SafeFile(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '-');
            return string.IsNullOrWhiteSpace(name) ? "Rejilla" : name.Trim();
        }

        /// <summary>
        /// Construye la familia paso a paso; cualquier fallo lanza con el numero y nombre del
        /// paso, y la familia no se guarda ni se carga (creacion atomica).
        /// </summary>
        private static void BuildGridFamily(Document fam, GridsCfg cfg, List<string> notes)
        {
            string step = "";
            void Step(int n, string name) { step = "paso " + n + " (" + name + ")"; notes.Add(step); }
            try
            {
                FamilyManager fm = fam.FamilyManager;

                // 1. un tipo valido antes de cualquier valor o formula (la plantilla no trae ninguno)
                Step(1, "tipo inicial");
                string firstName = cfg.Types.Count > 0 ? cfg.Types[0].Name : "Rejilla IG-01";
                if (fm.CurrentType == null)
                {
                    FamilyType ft0 = null;
                    foreach (FamilyType existing in fm.Types) if (existing.Name == firstName) ft0 = existing;
                    fm.CurrentType = ft0 ?? fm.NewType(firstName);
                }
                else if (string.IsNullOrWhiteSpace(fm.CurrentType.Name))
                {
                    try { fm.RenameCurrentType(firstName); } catch { fm.CurrentType = fm.NewType(firstName); }
                }
                if (fm.CurrentType == null) throw new InvalidOperationException("no se pudo crear el tipo inicial");

                // 2. parametros
                Step(2, "parametros Largo, Ancho, Espesor (instancia), Peso por m2, Peso, Designacion (tipo)");
                FamilyParameter pL = fm.AddParameter("Largo", GroupTypeId.Geometry, SpecTypeId.Length, true);
                FamilyParameter pA = fm.AddParameter("Ancho", GroupTypeId.Geometry, SpecTypeId.Length, true);
                FamilyParameter pE = fm.AddParameter("Espesor", GroupTypeId.Geometry, SpecTypeId.Length, true);
                FamilyParameter pW2 = fm.AddParameter("Peso por m2", GroupTypeId.General, SpecTypeId.MassPerUnitArea, false);
                FamilyParameter pW = fm.AddParameter("Peso", GroupTypeId.General, SpecTypeId.Mass, false);
                FamilyParameter pD = fm.AddParameter("Designacion", GroupTypeId.IdentityData, SpecTypeId.String.Text, false);

                double L0 = Mm(695), A0 = Mm(590), E0 = Mm(cfg.Types.Count > 0 ? cfg.Types[0].HeightMm : 38);
                fm.Set(pL, L0); fm.Set(pA, A0); fm.Set(pE, E0);
                fm.Set(pW2, UnitUtils.ConvertToInternalUnits(cfg.Types.Count > 0 ? cfg.Types[0].KgPerM2 : 31.0, UnitTypeId.KilogramsPerSquareMeter));

                // 3. formula del peso (largo x ancho = area; area x masa por area = masa)
                Step(3, "formula Peso = Largo * Ancho * Peso por m2");
                try { fm.SetFormula(pW, "Largo * Ancho * Peso por m2"); }
                catch (Exception ex)
                {
                    notes.Add("  aviso: no se pudo poner la formula de Peso (" + ex.Message + "); la familia sigue, el peso lo calcula el plugin");
                }

                // 4. planos de referencia de los cuatro lados y los centrales de la plantilla
                Step(4, "planos de referencia");
                View view = new FilteredElementCollector(fam).OfClass(typeof(ViewPlan)).Cast<ViewPlan>().FirstOrDefault(v => !v.IsTemplate);
                if (view == null) throw new InvalidOperationException("la plantilla no tiene vista de planta");
                ReferencePlane rpL = fam.FamilyCreate.NewReferencePlane(new XYZ(-L0 / 2, -2, 0), new XYZ(-L0 / 2, 2, 0), XYZ.BasisZ, view); rpL.Name = "Rejilla izq";
                ReferencePlane rpR = fam.FamilyCreate.NewReferencePlane(new XYZ(L0 / 2, -2, 0), new XYZ(L0 / 2, 2, 0), XYZ.BasisZ, view); rpR.Name = "Rejilla der";
                ReferencePlane rpF = fam.FamilyCreate.NewReferencePlane(new XYZ(-2, -A0 / 2, 0), new XYZ(2, -A0 / 2, 0), XYZ.BasisZ, view); rpF.Name = "Rejilla frente";
                ReferencePlane rpB = fam.FamilyCreate.NewReferencePlane(new XYZ(-2, A0 / 2, 0), new XYZ(2, A0 / 2, 0), XYZ.BasisZ, view); rpB.Name = "Rejilla fondo";
                ReferencePlane cLR = null, cFB = null;
                foreach (ReferencePlane rp in new FilteredElementCollector(fam).OfClass(typeof(ReferencePlane)).Cast<ReferencePlane>())
                {
                    if (rp.Id == rpL.Id || rp.Id == rpR.Id || rp.Id == rpF.Id || rp.Id == rpB.Id) continue;
                    XYZ n = rp.Normal;
                    if (Math.Abs(Math.Abs(n.X) - 1) < 1e-6 && Math.Abs(rp.FreeEnd.X) < 1e-6) cLR = cLR ?? rp;
                    if (Math.Abs(Math.Abs(n.Y) - 1) < 1e-6 && Math.Abs(rp.FreeEnd.Y) < 1e-6) cFB = cFB ?? rp;
                }
                fam.Regenerate();   // las referencias de los planos recien creados solo son geometricas tras regenerar

                // 5. cotas etiquetadas con Largo y Ancho (y de igualdad con los planos centrales para que crezca simetrica)
                Step(5, "cotas etiquetadas Largo / Ancho");
                Dimension dL = fam.FamilyCreate.NewDimension(view, Line.CreateBound(new XYZ(-L0 / 2, A0 / 2 + 1, 0), new XYZ(L0 / 2, A0 / 2 + 1, 0)), Refs(rpL, rpR));
                dL.FamilyLabel = pL;
                Dimension dA = fam.FamilyCreate.NewDimension(view, Line.CreateBound(new XYZ(L0 / 2 + 1, -A0 / 2, 0), new XYZ(L0 / 2 + 1, A0 / 2, 0)), Refs(rpF, rpB));
                dA.FamilyLabel = pA;
                if (cLR != null)
                {
                    try { Dimension eq = fam.FamilyCreate.NewDimension(view, Line.CreateBound(new XYZ(-L0 / 2, A0 / 2 + 1.5, 0), new XYZ(L0 / 2, A0 / 2 + 1.5, 0)), Refs(rpL, cLR, rpR)); eq.AreSegmentsEqual = true; }
                    catch (Exception ex) { notes.Add("  aviso: sin igualdad izquierda/derecha con el plano central (" + ex.Message + ")"); }
                }
                else notes.Add("  aviso: la plantilla no tiene plano central izquierda/derecha; la pieza crece hacia un lado");
                if (cFB != null)
                {
                    try { Dimension eq = fam.FamilyCreate.NewDimension(view, Line.CreateBound(new XYZ(L0 / 2 + 1.5, -A0 / 2, 0), new XYZ(L0 / 2 + 1.5, A0 / 2, 0)), Refs(rpF, cFB, rpB)); eq.AreSegmentsEqual = true; }
                    catch (Exception ex) { notes.Add("  aviso: sin igualdad frente/fondo con el plano central (" + ex.Message + ")"); }
                }
                else notes.Add("  aviso: la plantilla no tiene plano central frente/fondo; la pieza crece hacia un lado");

                // 6. extrusion rectangular con el espesor asociado a Espesor
                Step(6, "extrusion y asociacion de Espesor");
                var profile = new CurveArrArray();
                var loop = new CurveArray();
                XYZ p1 = new XYZ(-L0 / 2, -A0 / 2, 0), p2 = new XYZ(L0 / 2, -A0 / 2, 0), p3 = new XYZ(L0 / 2, A0 / 2, 0), p4 = new XYZ(-L0 / 2, A0 / 2, 0);
                loop.Append(Line.CreateBound(p1, p2)); loop.Append(Line.CreateBound(p2, p3)); loop.Append(Line.CreateBound(p3, p4)); loop.Append(Line.CreateBound(p4, p1));
                profile.Append(loop);
                SketchPlane sp = SketchPlane.Create(fam, Plane.CreateByNormalAndOrigin(XYZ.BasisZ, XYZ.Zero));
                Extrusion extr = fam.FamilyCreate.NewExtrusion(true, profile, sp, E0);
                fm.AssociateElementParameterToFamilyParameter(extr.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM), pE);
                fam.Regenerate();

                // 7. bloquear las caras laterales del solido a los planos (referencias geometricas de las caras)
                Step(7, "alineaciones de las caras con los planos");
                int aligned = 0;
                var opt = new Options { ComputeReferences = true, DetailLevel = ViewDetailLevel.Fine };
                foreach (GeometryObject go in extr.get_Geometry(opt))
                {
                    if (!(go is Solid sol)) continue;
                    foreach (Face f in sol.Faces)
                    {
                        if (!(f is PlanarFace pf) || pf.Reference == null) continue;
                        XYZ n = pf.FaceNormal;
                        ReferencePlane rp = n.X < -0.99 ? rpL : n.X > 0.99 ? rpR : n.Y < -0.99 ? rpF : n.Y > 0.99 ? rpB : null;
                        if (rp == null) continue;
                        fam.FamilyCreate.NewAlignment(view, rp.GetReference(), pf.Reference);
                        aligned++;
                    }
                }
                if (aligned < 4) throw new InvalidOperationException("solo se alinearon " + aligned + " de 4 caras: Largo y Ancho no gobernarian la geometria");
                fam.Regenerate();

                // 8. material con patron de modelo de lineas cada 30 mm
                Step(8, "material Rejilla con patron de 30 mm");
                try
                {
                    ElementId matId = Material.Create(fam, "Rejilla");
                    var mat = fam.GetElement(matId) as Material;
                    var fp = new FillPattern("Rejilla 30 mm", FillPatternTarget.Model, FillPatternHostOrientation.ToHost, 0, Mm(30));
                    FillPatternElement fpe = FillPatternElement.Create(fam, fp);
                    if (mat != null)
                    {
                        mat.Color = new Color(110, 115, 120);
                        mat.SurfaceForegroundPatternId = fpe.Id;
                        mat.SurfaceForegroundPatternColor = new Color(40, 40, 40);
                    }
                    Parameter mp = extr.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                    if (mp != null && !mp.IsReadOnly) mp.Set(matId);
                }
                catch (Exception ex) { notes.Add("  aviso: material Rejilla no aplicado (" + ex.Message + ")"); }

                // 9. un tipo por cada tipo de la lista, con su peso, su alto y su designacion
                Step(9, "tipos de la lista");
                foreach (GridTypeCfg t in cfg.Types)
                {
                    FamilyType ft = null;
                    foreach (FamilyType existing in fm.Types) if (existing.Name == t.Name) ft = existing;
                    fm.CurrentType = ft ?? fm.NewType(t.Name);
                    fm.Set(pW2, UnitUtils.ConvertToInternalUnits(t.KgPerM2, UnitTypeId.KilogramsPerSquareMeter));
                    fm.Set(pE, Mm(t.HeightMm));
                    fm.Set(pL, L0); fm.Set(pA, A0);
                    try { fm.Set(pD, t.Designation ?? ""); } catch { }
                }

                // 10. comprobacion: flexionar Largo y Ancho y ver que la caja de la extrusion cambia
                Step(10, "comprobacion de que Largo y Ancho gobiernan la extrusion");
                fam.Regenerate();
                BoundingBoxXYZ bb0 = extr.get_BoundingBox(null);
                fm.Set(pL, L0 + Mm(200)); fm.Set(pA, A0 + Mm(100));
                fam.Regenerate();
                BoundingBoxXYZ bb1 = extr.get_BoundingBox(null);
                fm.Set(pL, L0); fm.Set(pA, A0);
                fam.Regenerate();
                if (bb0 == null || bb1 == null || Math.Abs((bb1.Max.X - bb1.Min.X) - (bb0.Max.X - bb0.Min.X) - Mm(200)) > Mm(1) || Math.Abs((bb1.Max.Y - bb1.Min.Y) - (bb0.Max.Y - bb0.Min.Y) - Mm(100)) > Mm(1))
                    throw new InvalidOperationException("al cambiar Largo y Ancho la extrusion no cambio como debia (caja " + (bb0 == null ? "?" : ToMm(bb0.Max.X - bb0.Min.X) + " x " + ToMm(bb0.Max.Y - bb0.Min.Y)) +
                                                        " -> " + (bb1 == null ? "?" : ToMm(bb1.Max.X - bb1.Min.X) + " x " + ToMm(bb1.Max.Y - bb1.Min.Y)) + " mm)");
                notes.Add("  Largo y Ancho gobiernan la extrusion (comprobado); Espesor gobierna su altura por asociacion");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("fallo en " + step + ": " + ex.Message, ex);
            }
        }

        private static ReferenceArray Refs(params ReferencePlane[] planes)
        {
            var ra = new ReferenceArray();
            foreach (ReferencePlane p in planes) ra.Append(p.GetReference());
            return ra;
        }

        // =================================================================
        // Colocacion
        // =================================================================
        public sealed class PlaceResult
        {
            public List<ElementId> Angles = new List<ElementId>();
            public List<ElementId> Grids = new List<ElementId>();
            public List<string> Lines = new List<string>();
            public List<string> Warnings = new List<string>();
            public List<string> Failed = new List<string>();
            public string Summary => Angles.Count + " angulo(s) y " + Grids.Count + " rejilla(s) colocados";
        }

        private static Level LevelFor(Document doc, double z)
        {
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).ToList();
            if (levels.Count == 0) throw new InvalidOperationException("el proyecto no tiene niveles");
            return levels.LastOrDefault(l => l.Elevation <= z + Mm(1)) ?? levels[0];
        }

        /// <summary>Coloca angulos y rejillas del plan (transaccion o subtransaccion abierta).</summary>
        public static PlaceResult Place(Document doc, HostAnalysis item, AppConfig cfg, GridPlan plan, AngleSymbolInfo angle, Autodesk.Revit.DB.Family gridFamily)
        {
            var res = new PlaceResult();
            BlockFrame f = item.Frame(cfg);
            if (f == null) throw new InvalidOperationException(item.Error ?? "sin geometria");
            if (plan.Error != null) throw new InvalidOperationException(plan.Error);
            GridsCfg g = cfg.Grids;
            Level level = LevelFor(doc, f.ZBottom + f.ZTop - f.ZBottom);
            string hostTag = "host " + item.Host.Id;

            if (g.Angles.Enabled)
            {
                if (angle == null) res.Warnings.Add("sin tipo de angulo: no se colocan angulos (" + plan.Angles.Count + " previstos)");
                else
                {
                    FamilySymbol fs = angle.Symbol;
                    if (!fs.IsActive) fs.Activate();
                    double leg = Mm(g.Angles.LegMm);
                    int n = 0;
                    foreach (AngleBar a in plan.Angles)
                    {
                        try
                        {
                            Pt oa = new Pt(a.A.U + a.Inward.U * 0.5 * leg, a.A.V + a.Inward.V * 0.5 * leg);
                            Pt ob = new Pt(a.B.U + a.Inward.U * 0.5 * leg, a.B.V + a.Inward.V * 0.5 * leg);
                            XYZ pa = f.World(oa.U, oa.V, a.ZTop - 0.5 * leg), pb = f.World(ob.U, ob.V, a.ZTop - 0.5 * leg);
                            FamilyInstance fi = doc.Create.NewFamilyInstance(Line.CreateBound(pa, pb), fs, level, StructuralType.Beam);
                            SetInt(fi, BuiltInParameter.Y_JUSTIFICATION, 2);   // centro
                            SetInt(fi, BuiltInParameter.Z_JUSTIFICATION, 2);
                            try { Parameter rot = fi.get_Parameter(BuiltInParameter.STRUCTURAL_BEND_DIR_ANGLE); if (rot != null && !rot.IsReadOnly) rot.Set(g.Angles.RotationDeg * Math.PI / 180); } catch { }
                            SetString(fi, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, MarkerAngle + " " + hostTag + " " + AngleCategories.Key(a.Category) + " foso " + (a.Recess + 1));
                            res.Angles.Add(fi.Id);
                            n++;
                        }
                        catch (Exception ex) { res.Failed.Add(a.Describe() + ": " + ex.Message); }
                    }
                    res.Lines.Add(n + " angulo(s) " + angle.Display + " (" + angle.KgPerM.ToString("0.00", CultureInfo.InvariantCulture) + " kg/m, " + angle.KgSource + ")");
                }
            }

            if (g.Mode == "model" && plan.Pieces.Count > 0)
            {
                if (gridFamily == null) res.Warnings.Add("familia de rejilla \"" + g.FamilyName + "\" no cargada: no se modelan las " + plan.Pieces.Count + " piezas (solo informe)");
                else
                {
                    FamilySymbol fs = GridSymbolFor(doc, gridFamily, plan.Type, res.Lines);
                    double h = Mm(plan.Type.HeightMm);
                    double baseAngle = Math.Atan2(f.DirU.Y, f.DirU.X);
                    int n = 0;
                    foreach (GridPiece p in plan.Pieces)
                    {
                        try
                        {
                            XYZ loc = f.World(p.Center.U, p.Center.V, p.ZTop - h);
                            FamilyInstance fi = doc.Create.NewFamilyInstance(loc, fs, level, StructuralType.NonStructural);
                            double ang = baseAngle + (p.AlongU ? 0 : Math.PI / 2);
                            if (Math.Abs(ang) > 1e-9) ElementTransformUtils.RotateElement(doc, fi.Id, Line.CreateBound(loc, loc + XYZ.BasisZ), ang);
                            SetLen(fi, "Largo", p.Length); SetLen(fi, "Ancho", p.Width); SetLen(fi, "Espesor", h);
                            if (fi.Location is LocationPoint lp && Math.Abs(lp.Point.Z - loc.Z) > Mm(0.5))
                                ElementTransformUtils.MoveElement(doc, fi.Id, new XYZ(0, 0, loc.Z - lp.Point.Z));
                            SetString(fi, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, MarkerGrid + " " + hostTag + " " + p.Group + " foso " + (p.Recess + 1));
                            try { Parameter mk = fi.get_Parameter(BuiltInParameter.ALL_MODEL_MARK); if (mk != null && !mk.IsReadOnly) mk.Set(p.Group); } catch { }
                            res.Grids.Add(fi.Id);
                            n++;
                        }
                        catch (Exception ex) { res.Failed.Add("rejilla " + p.Group + " en (" + ToMm(p.Center.U) + ", " + ToMm(p.Center.V) + "): " + ex.Message); }
                    }
                    res.Lines.Add(n + " rejilla(s) " + gridFamily.Name + " : " + fs.Name + " (alto " + plan.Type.HeightMm.ToString("0.#", CultureInfo.InvariantCulture) + " mm)");
                }
            }
            else if (g.Mode == "countOnly" && plan.Pieces.Count > 0) res.Lines.Add(plan.Pieces.Count + " rejilla(s) solo contadas (modo informe)");
            return res;
        }

        private static void SetInt(FamilyInstance fi, BuiltInParameter bip, int v)
        {
            try { Parameter p = fi.get_Parameter(bip); if (p != null && !p.IsReadOnly) p.Set(v); } catch { }
        }

        private static void SetString(Element e, BuiltInParameter bip, string v)
        {
            try { Parameter p = e.get_Parameter(bip); if (p != null && !p.IsReadOnly) p.Set(v); } catch { }
        }

        private static void SetLen(FamilyInstance fi, string name, double ft)
        {
            Parameter p = fi.LookupParameter(name);
            if (p == null) throw new InvalidOperationException("la familia no tiene el parametro de instancia \"" + name + "\"");
            if (p.IsReadOnly) throw new InvalidOperationException("el parametro \"" + name + "\" es de solo lectura (debe ser de instancia)");
            p.Set(ft);
        }

        // =================================================================
        // Busqueda y borrado
        // =================================================================
        public static List<ElementId> FindPluginItems(Document doc, Element host)
        {
            var ids = new List<ElementId>();
            string hostTag = "host " + host.Id;
            try
            {
                foreach (FamilyInstance fi in new FilteredElementCollector(doc).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>())
                {
                    string cm = "";
                    try { cm = fi.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? ""; } catch { }
                    if ((cm.StartsWith(MarkerAngle, StringComparison.OrdinalIgnoreCase) || cm.StartsWith(MarkerGrid, StringComparison.OrdinalIgnoreCase)) && cm.Contains(hostTag + " "))
                        ids.Add(fi.Id);
                }
            }
            catch (Exception ex) { Log.Error("FindPluginItems", ex); }
            return ids;
        }

        public static int DeletePluginItems(Document doc, Element host, out int angles, out int grids)
        {
            angles = 0; grids = 0;
            foreach (ElementId id in FindPluginItems(doc, host))
            {
                try
                {
                    string cm = doc.GetElement(id)?.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? "";
                    doc.Delete(id);
                    if (cm.StartsWith(MarkerAngle, StringComparison.OrdinalIgnoreCase)) angles++; else grids++;
                }
                catch (Exception ex) { Log.Error("DeletePluginItems " + id, ex); }
            }
            return angles + grids;
        }
    }
}
