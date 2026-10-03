using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace BlockRebar
{
    /// <summary>
    /// Vistas de seccion A-A y B-B en Revit sobre las mismas lineas de corte de la lamina:
    /// ViewSection a escala 1:20 con detalle fino, recorte ajustado al bloque mas un margen,
    /// nombre por plantilla ("{marca} - Seccion {letra}"), acero del plugin sin ocultar y con
    /// todas las barras del conjunto, y una etiqueta por conjunto visible si la familia de
    /// etiqueta de armadura esta cargada.
    /// </summary>
    public static class SectionViews
    {
        /// <summary>Un tipo de etiqueta de armadura cargado en el proyecto.</summary>
        public sealed class TagType
        {
            public ElementId Id;
            public string FamilyName;
            public string TypeName;
            public string Display => FamilyName + " : " + TypeName;
        }

        public sealed class Result
        {
            public List<ElementId> Views = new List<ElementId>();
            public List<string> Lines = new List<string>();
            public List<string> Warnings = new List<string>();
            public int Tags;
        }

        private static double Mm(double mm) => BlockPlan.Mm(mm);

        /// <summary>Tipos de etiqueta de armadura (Structural Rebar Tags) cargados, por familia y tipo.</summary>
        public static List<TagType> ReadTagTypes(Document doc)
        {
            var list = new List<TagType>();
            try
            {
                foreach (FamilySymbol fs in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_RebarTags).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>())
                    list.Add(new TagType { Id = fs.Id, FamilyName = fs.Family?.Name ?? fs.FamilyName, TypeName = fs.Name });
            }
            catch (Exception ex) { Log.Error("ReadTagTypes", ex); }
            return list.OrderBy(t => t.FamilyName, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.TypeName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Candidatos para el nombre configurado: exacto "Familia : Tipo" o "Familia"; si no,
        /// fragmento. Varios tipos de la MISMA familia no son ambiguos (se toma el primero).
        /// </summary>
        public static List<TagType> Candidates(IList<TagType> all, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return new List<TagType>();
            List<string> full = NameMatch.Candidates(all.Select(t => t.Display), name);
            if (full.Count == 1) return new List<TagType> { all.First(t => t.Display == full[0]) };
            List<string> fam = NameMatch.Candidates(all.Select(t => t.FamilyName).Distinct(), name);
            if (fam.Count == 1) return new List<TagType> { all.First(t => t.FamilyName == fam[0]) };
            if (fam.Count > 1) return fam.Select(f => all.First(t => t.FamilyName == f)).ToList();
            return full.Select(d => all.First(t => t.Display == d)).ToList();
        }

        public static TagType Find(IList<TagType> all, string name)
        {
            List<TagType> c = Candidates(all, name);
            return c.Count == 1 ? c[0] : null;
        }

        /// <summary>
        /// Crea las dos vistas del elemento (dentro de una transaccion abierta). rebars = conjuntos
        /// del plugin que se muestran sin ocultar y se etiquetan (uno por familia y vista).
        /// </summary>
        public static Result Create(Document doc, HostAnalysis item, AppConfig cfg, double cutA, double cutB, IList<ElementId> rebars, IList<TagType> tagTypes)
        {
            var res = new Result();
            SectionViewsCfg sv = cfg.SectionViews;
            BlockFrame f = item.Frame(cfg);
            if (f == null || f.Topology == null || f.Topology.Bottom == null) { res.Warnings.Add(item.Tag + "sin geometria: no se crean vistas"); return res; }
            BlockTopology t = f.Topology;

            ViewFamilyType vft = FindViewType(doc, sv.ViewTypeName, out string vtWarn);
            if (vft == null) { res.Warnings.Add(item.Tag + vtWarn); return res; }

            TagType tag = null;
            if (!string.IsNullOrWhiteSpace(sv.TagFamilyName))
            {
                List<TagType> c = Candidates(tagTypes, sv.TagFamilyName);
                if (c.Count == 1) tag = c[0];
                else if (c.Count == 0) res.Warnings.Add("familia de etiqueta \"" + sv.TagFamilyName + "\" no cargada: las vistas se crean sin etiquetas");
                else res.Warnings.Add("familia de etiqueta \"" + sv.TagFamilyName + "\" ambigua (" + string.Join(", ", c.Select(x => x.Display)) + "): sin etiquetas; elige una en la ventana");
            }

            double margin = Mm(sv.MarginMm), depth = Math.Max(Mm(50), Mm(sv.DepthMm));
            foreach (bool alongU in new[] { true, false })
            {
                string letter = alongU ? "A" : "B";
                try
                {
                    // sistema de la caja: X = direccion del corte (hacia la derecha en la lamina), Y = vertical, Z = hacia el observador
                    XYZ right = alongU ? f.DirU : f.DirV;
                    XYZ toViewer = right.CrossProduct(XYZ.BasisZ).Normalize();   // A-A: -v (se mira hacia +v); B-B: +u (se mira hacia -u)
                    double s0 = alongU ? t.UMin : t.VMin, s1 = alongU ? t.UMax : t.VMax;
                    double sc = 0.5 * (s0 + s1), half = 0.5 * (s1 - s0);
                    XYZ origin = alongU ? f.World(sc, cutA, 0) : f.World(cutB, sc, 0);
                    var tr = Transform.Identity;
                    tr.Origin = origin; tr.BasisX = right; tr.BasisY = XYZ.BasisZ; tr.BasisZ = toViewer;
                    var box = new BoundingBoxXYZ { Transform = tr };
                    box.Min = new XYZ(-half - margin, -margin, -depth);
                    box.Max = new XYZ(half + margin, f.Thickness + margin, 0);

                    ViewSection view = ViewSection.CreateSection(doc, vft.Id, box);
                    try { view.Scale = Math.Max(1, sv.Scale); } catch (Exception ex) { res.Warnings.Add("escala: " + ex.Message); }
                    try { view.DetailLevel = ViewDetailLevel.Fine; } catch { }
                    try { view.CropBoxActive = true; view.CropBoxVisible = false; } catch { }
                    string name = UniqueName(doc, ViewName(sv.NameTemplate, item, letter));
                    try { view.Name = name; } catch (Exception ex) { res.Warnings.Add("nombre \"" + name + "\": " + ex.Message); name = view.Name; }
                    res.Views.Add(view.Id);

                    doc.Regenerate();
                    int tags = 0, shown = 0;
                    var taggedFamilies = new HashSet<string>();
                    int i = 0;
                    foreach (ElementId id in rebars)
                    {
                        if (!(doc.GetElement(id) is Rebar rb)) continue;
                        try { rb.SetUnobscuredInView(view, true); shown++; } catch { }
                        try { rb.SetPresentationMode(view, RebarPresentationMode.All); } catch { }
                        if (tag == null) continue;
                        string fam = FamilyOf(rb);
                        if (taggedFamilies.Contains(fam)) continue;
                        if (!Intersects(rb, tr, box)) continue;
                        XYZ head = TagHead(rb, tr, box, i++, f.Thickness, margin);
                        if (TryTag(doc, view, rb, tag, head, out string why)) { taggedFamilies.Add(fam); tags++; }
                        else res.Warnings.Add("etiqueta de " + fam + " en " + name + ": " + why);
                    }
                    res.Tags += tags;
                    res.Lines.Add("vista \"" + name + "\" (id " + view.Id + "): corte " + letter + "-" + letter + " a " + (alongU ? "v = " : "u = ") +
                                  ((alongU ? cutA : cutB) * 0.3048).ToString("0.00", CultureInfo.InvariantCulture) + " m, escala 1:" + sv.Scale + ", " + shown +
                                  " conjunto(s) sin ocultar" + (tag != null ? ", " + tags + " etiqueta(s)" : ""));
                }
                catch (Exception ex)
                {
                    res.Warnings.Add("no se pudo crear la seccion " + letter + "-" + letter + ": " + ex.Message);
                    Log.Error("SectionViews " + letter, ex);
                }
            }

            if (sv.ShowSolid)
            {
                // Revit 2027 ya no tiene Rebar.SetSolidInView: en las vistas 3D el acero se ve como solido con nivel de detalle fino
                var v3 = doc.ActiveView as View3D;
                if (v3 == null) res.Warnings.Add("acero como solido: la vista activa no es 3D; no se aplica (en Revit 2027 el acero se ve solido en las vistas 3D con detalle fino)");
                else
                {
                    try
                    {
                        v3.DetailLevel = ViewDetailLevel.Fine;
                        int n = 0;
                        foreach (ElementId id in rebars)
                            if (doc.GetElement(id) is Rebar rb) { try { rb.SetUnobscuredInView(v3, true); n++; } catch { } }
                        res.Lines.Add("vista 3D activa \"" + v3.Name + "\" con detalle fino (acero solido) y " + n + " conjunto(s) sin ocultar");
                    }
                    catch (Exception ex) { res.Warnings.Add("vista 3D activa: " + ex.Message); }
                }
            }
            return res;
        }

        /// <summary>
        /// Etiqueta un conjunto probando, por orden: tipo activado + referencia al elemento;
        /// modo por categoria y cambio de tipo; y la referencia geometrica de una barra del
        /// conjunto en esa vista. Devuelve el motivo exacto de cada intento si todos fallan.
        /// </summary>
        private static bool TryTag(Document doc, View view, Rebar rb, TagType tag, XYZ head, out string why)
        {
            var reasons = new List<string>();
            try
            {
                var fs = doc.GetElement(tag.Id) as FamilySymbol;
                if (fs != null && !fs.IsActive) { fs.Activate(); doc.Regenerate(); }
            }
            catch (Exception ex) { reasons.Add("activar el tipo: " + ex.Message); }
            try
            {
                IndependentTag.Create(doc, tag.Id, view.Id, new Reference(rb), true, TagOrientation.Horizontal, head);
                why = null; return true;
            }
            catch (Exception ex) { reasons.Add("referencia al conjunto: " + ex.Message); }
            try
            {
                IndependentTag t2 = IndependentTag.Create(doc, view.Id, new Reference(rb), true, TagMode.TM_ADDBY_CATEGORY, TagOrientation.Horizontal, head);
                try { if (t2.GetTypeId() != tag.Id) t2.ChangeTypeId(tag.Id); } catch (Exception ex) { reasons.Add("cambiar al tipo elegido: " + ex.Message); }
                why = null; return true;
            }
            catch (Exception ex) { reasons.Add("modo por categoria: " + ex.Message); }
            try
            {
                Reference geo = null;
                var opt = new Options { View = view, ComputeReferences = true, IncludeNonVisibleObjects = false };
                foreach (GeometryObject go in rb.get_Geometry(opt))
                {
                    if (go is Curve cv && cv.Reference != null) { geo = cv.Reference; break; }
                    if (go is Solid sol) foreach (Face fc in sol.Faces) if (fc.Reference != null) { geo = fc.Reference; break; }
                    if (geo != null) break;
                    if (go is GeometryInstance gi)
                        foreach (GeometryObject g2 in gi.GetInstanceGeometry())
                        {
                            if (g2 is Curve c2 && c2.Reference != null) { geo = c2.Reference; break; }
                            if (g2 is Solid s2) foreach (Face fc in s2.Faces) if (fc.Reference != null) { geo = fc.Reference; break; }
                            if (geo != null) break;
                        }
                    if (geo != null) break;
                }
                if (geo == null) reasons.Add("referencia geometrica: el conjunto no tiene geometria con referencia en la vista (no esta visible en ella?)");
                else
                {
                    IndependentTag.Create(doc, tag.Id, view.Id, geo, true, TagOrientation.Horizontal, head);
                    why = null; return true;
                }
            }
            catch (Exception ex) { reasons.Add("referencia geometrica: " + ex.Message); }
            why = string.Join(" | ", reasons);
            return false;
        }

        private static ViewFamilyType FindViewType(Document doc, string name, out string warn)
        {
            warn = null;
            var all = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().Where(v => v.ViewFamily == ViewFamily.Section).ToList();
            if (all.Count == 0) { warn = "el proyecto no tiene ningun tipo de vista de seccion"; return null; }
            if (string.IsNullOrWhiteSpace(name)) return all[0];
            List<string> c = NameMatch.Candidates(all.Select(v => v.Name), name);
            if (c.Count >= 1) return all.First(v => v.Name == c[0]);
            warn = "tipo de vista de seccion \"" + name + "\" no existe; se usa \"" + all[0].Name + "\"";
            return all[0];
        }

        private static string ViewName(string template, HostAnalysis item, string letter)
        {
            string mark = string.IsNullOrWhiteSpace(item.Mark) ? item.Host.Id.ToString() : item.Mark;
            string s = (string.IsNullOrWhiteSpace(template) ? "{marca} - Sección {letra}" : template)
                .Replace("{marca}", mark).Replace("{id}", item.Host.Id.ToString()).Replace("{letra}", letter).Replace("{tipo}", item.TypeName ?? "");
            foreach (char ch in "\\:{}[]|;<>?`~") s = s.Replace(ch, '-');
            return s.Trim();
        }

        private static string UniqueName(Document doc, string name)
        {
            var existing = new HashSet<string>(new FilteredElementCollector(doc).OfClass(typeof(View)).Cast<View>().Select(v => v.Name), StringComparer.OrdinalIgnoreCase);
            if (!existing.Contains(name)) return name;
            for (int i = 2; i < 1000; i++) if (!existing.Contains(name + " (" + i + ")")) return name + " (" + i + ")";
            return name + " " + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        /// <summary>Familia del armado (F1...F8) por el comentario del plugin; vacio si no lo lleva.</summary>
        private static string FamilyOf(Rebar rb)
        {
            try
            {
                string cm = rb.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() ?? "";
                if (cm.StartsWith(RebarGenerator.Marker, StringComparison.OrdinalIgnoreCase)) return cm.Substring(RebarGenerator.Marker.Length).Trim();
            }
            catch { }
            return rb.Id.ToString();
        }

        /// <summary>True si la caja del conjunto corta el volumen de la seccion (recorte y profundidad).</summary>
        private static bool Intersects(Rebar rb, Transform tr, BoundingBoxXYZ box)
        {
            BoundingBoxXYZ bb;
            try { bb = rb.get_BoundingBox(null); } catch { return false; }
            if (bb == null) return false;
            Transform inv = tr.Inverse;
            double xMin = double.MaxValue, xMax = double.MinValue, yMin = double.MaxValue, yMax = double.MinValue, zMin = double.MaxValue, zMax = double.MinValue;
            foreach (double x in new[] { bb.Min.X, bb.Max.X })
                foreach (double y in new[] { bb.Min.Y, bb.Max.Y })
                    foreach (double z in new[] { bb.Min.Z, bb.Max.Z })
                    {
                        XYZ p = inv.OfPoint(new XYZ(x, y, z));
                        xMin = Math.Min(xMin, p.X); xMax = Math.Max(xMax, p.X);
                        yMin = Math.Min(yMin, p.Y); yMax = Math.Max(yMax, p.Y);
                        zMin = Math.Min(zMin, p.Z); zMax = Math.Max(zMax, p.Z);
                    }
            return xMax >= box.Min.X && xMin <= box.Max.X && yMax >= box.Min.Y && yMin <= box.Max.Y && zMax >= box.Min.Z && zMin <= box.Max.Z;
        }

        /// <summary>Cabeza de la etiqueta: sobre el bloque (pares) o bajo la cara inferior (impares), escalonadas, en el plano del corte.</summary>
        private static XYZ TagHead(Rebar rb, Transform tr, BoundingBoxXYZ box, int i, double thickness, double margin)
        {
            BoundingBoxXYZ bb = rb.get_BoundingBox(null);
            XYZ c = tr.Inverse.OfPoint(0.5 * (bb.Min + bb.Max));
            double x = Math.Max(box.Min.X + Mm(100), Math.Min(box.Max.X - Mm(100), c.X));
            double step = Mm(150) * (i / 2);
            double y = i % 2 == 0 ? thickness + 0.6 * margin + step : -0.5 * margin - step;
            return tr.OfPoint(new XYZ(x, y, 0));
        }
    }
}
