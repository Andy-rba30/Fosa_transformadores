using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlockRebar
{
    /// <summary>Familias de armado del bloque. El orden es el de los codigos F1...F8.</summary>
    public enum Family { F1, F2, F3, F4, F5, F6, F7, F8 }

    public static class Families
    {
        public static readonly Family[] All = { Family.F1, Family.F2, Family.F3, Family.F4, Family.F5, Family.F6, Family.F7, Family.F8 };

        public static string Code(Family f) => f.ToString();

        public static string Name(Family f)
        {
            switch (f)
            {
                case Family.F1: return "malla inferior";
                case Family.F2: return "malla bajo foso";
                case Family.F3: return "malla superior de plataforma";
                case Family.F4: return "L en cara de foso (plataforma)";
                case Family.F5: return "horizontales en cara de foso (plataforma)";
                case Family.F6: return "verticales de murete";
                case Family.F7: return "horquillas de murete";
                default: return "horizontales de murete";
            }
        }

        public static Family Parse(string code)
        {
            foreach (Family f in All) if (string.Equals(Code(f), (code ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) return f;
            return Family.F1;
        }
    }

    /// <summary>Direccion de u (las barras "principales" de las mallas: la capa mas baja de F1, la mas alta de F2 y F3).</summary>
    public class DirectionCfg
    {
        /// <summary>
        /// "long" = u paralela al lado largo del contorno inferior (por defecto), "short" = lado
        /// corto, "x" / "y" = ejes del proyecto, "angle" = el angulo AngleDeg desde el eje X.
        /// Cambiable bloque a bloque en la ventana.
        /// </summary>
        [JsonPropertyName("mode")] public string Mode { get; set; } = "long";
        [JsonPropertyName("angleDeg")] public double AngleDeg { get; set; } = 0;
    }

    /// <summary>Una capa de barras paralelas: tipo de barra y separacion maxima.</summary>
    public class LayerCfg
    {
        /// <summary>Tipo de barra (RebarBarType): exacto, o un fragmento que lo identifique ("5/8").</summary>
        [JsonPropertyName("barTypeName")] public string BarTypeName { get; set; } = "";
        /// <summary>Separacion (mm): maxima con n = techo(L / s) huecos iguales ("maxSpacing") o exacta desde el primer extremo ("fromTop").</summary>
        [JsonPropertyName("spacingMm")] public double SpacingMm { get; set; } = 125;
        /// <summary>"maxSpacing" (reparto con barra en los dos extremos) o "fromTop" (separacion exacta desde el extremo inicial, sin forzar la ultima barra).</summary>
        [JsonPropertyName("layoutMode")] public string LayoutMode { get; set; } = "maxSpacing";

        public LayerCfg() { }
        public LayerCfg(string type, double spacing) { BarTypeName = type; SpacingMm = spacing; }
        public LayerCfg Copy() => new LayerCfg(BarTypeName, SpacingMm) { LayoutMode = LayoutMode };
    }

    /// <summary>F1: malla inferior en dos direcciones con patas hacia arriba en los bordes exteriores.</summary>
    public class BottomMeshCfg
    {
        [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
        [JsonPropertyName("u")] public LayerCfg U { get; set; } = new LayerCfg("5/8\"", 125);
        [JsonPropertyName("v")] public LayerCfg V { get; set; } = new LayerCfg("5/8\"", 125);
        /// <summary>Pata vertical hacia arriba en los extremos que dan al borde exterior (mm, exterior).</summary>
        [JsonPropertyName("legUpMm")] public double LegUpMm { get; set; } = 300;
    }

    /// <summary>F2: malla bajo el fondo de foso (a belowRecessFloorMm bajo el mas profundo) con patas hacia abajo.</summary>
    public class RecessMeshCfg
    {
        [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
        /// <summary>"full" = todo el contorno inferior; "recess" = solo bajo los fosos mas el anclaje.</summary>
        [JsonPropertyName("extent")] public string Extent { get; set; } = "full";
        /// <summary>Distancia del fondo del foso mas profundo a la cara superior de la malla (mm).</summary>
        [JsonPropertyName("belowRecessFloorMm")] public double BelowRecessFloorMm { get; set; } = 75;
        /// <summary>En modo "recess", cuanto se prolongan las barras dentro del hormigon contiguo (mm).</summary>
        [JsonPropertyName("anchorageMm")] public double AnchorageMm { get; set; } = 600;
        [JsonPropertyName("u")] public LayerCfg U { get; set; } = new LayerCfg("5/8\"", 125);
        [JsonPropertyName("v")] public LayerCfg V { get; set; } = new LayerCfg("5/8\"", 125);
        [JsonPropertyName("legDownMm")] public double LegDownMm { get; set; } = 220;
    }

    /// <summary>F3: malla superior de cada plataforma con patas hacia abajo en todos sus bordes.</summary>
    public class TopMeshCfg
    {
        [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
        [JsonPropertyName("u")] public LayerCfg U { get; set; } = new LayerCfg("5/8\"", 125);
        [JsonPropertyName("v")] public LayerCfg V { get; set; } = new LayerCfg("5/8\"", 125);
        [JsonPropertyName("legDownMm")] public double LegDownMm { get; set; } = 340;
    }

    /// <summary>F4: barra en L en las caras de foso del lado plataforma (vertical + pie bajo el foso).</summary>
    public class RecessFaceLCfg
    {
        [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
        [JsonPropertyName("barTypeName")] public string BarTypeName { get; set; } = "5/8\"";
        [JsonPropertyName("spacingMm")] public double SpacingMm { get; set; } = 125;
        [JsonPropertyName("layoutMode")] public string LayoutMode { get; set; } = "maxSpacing";
        /// <summary>Longitud del tramo vertical desde el tope (recubrimiento) hacia abajo (mm).</summary>
        [JsonPropertyName("verticalMm")] public double VerticalMm { get; set; } = 1000;
        /// <summary>Longitud del pie horizontal que apunta bajo el foso (mm).</summary>
        [JsonPropertyName("footMm")] public double FootMm { get; set; } = 370;
    }

    /// <summary>F5: horizontales en las caras de plataforma hacia el foso, por dentro de F4 y de las patas de F3.</summary>
    public class RecessFaceHCfg
    {
        [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
        [JsonPropertyName("barTypeName")] public string BarTypeName { get; set; } = "3/8\"";
        /// <summary>Separacion vertical (mm).</summary>
        [JsonPropertyName("spacingMm")] public double SpacingMm { get; set; } = 200;
        /// <summary>"fromTop" (por defecto: separacion exacta desde el nivel superior, el resto queda abajo, como en el plano) o "maxSpacing".</summary>
        [JsonPropertyName("layoutMode")] public string LayoutMode { get; set; } = "fromTop";
        /// <summary>"segments" = tramos rectos por cara prolongados hasta la esquina; "ring" = anillo cerrado por traslape.</summary>
        [JsonPropertyName("shape")] public string Shape { get; set; } = "segments";
        /// <summary>Prolongacion mas alla del cruce con la barra de la cara contigua, o traslape del anillo (mm).</summary>
        [JsonPropertyName("lapMm")] public double LapMm { get; set; } = 400;
    }

    /// <summary>F6: verticales en la cara exterior del murete, altura completa, traslapadas con F1.</summary>
    public class WallVerticalCfg
    {
        [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
        [JsonPropertyName("barTypeName")] public string BarTypeName { get; set; } = "3/8\"";
        [JsonPropertyName("spacingMm")] public double SpacingMm { get; set; } = 125;
        [JsonPropertyName("layoutMode")] public string LayoutMode { get; set; } = "maxSpacing";
    }

    /// <summary>F7: horquilla en U invertida sobre la corona del murete.</summary>
    public class WallHairpinCfg
    {
        [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
        [JsonPropertyName("barTypeName")] public string BarTypeName { get; set; } = "3/8\"";
        [JsonPropertyName("spacingMm")] public double SpacingMm { get; set; } = 125;
        [JsonPropertyName("layoutMode")] public string LayoutMode { get; set; } = "maxSpacing";
        /// <summary>Patas hacia abajo (mm, exterior).</summary>
        [JsonPropertyName("legMm")] public double LegMm { get; set; } = 350;
        /// <summary>"aligned" = en las mismas posiciones que F6 (traslape por contacto); "staggered" = desplazada media separacion.</summary>
        [JsonPropertyName("placement")] public string Placement { get; set; } = "aligned";
    }

    /// <summary>F8: horizontales del murete, de zBase al tope, cerradas por tramos.</summary>
    public class WallHorizCfg
    {
        [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
        [JsonPropertyName("barTypeName")] public string BarTypeName { get; set; } = "3/8\"";
        [JsonPropertyName("spacingMm")] public double SpacingMm { get; set; } = 200;
        /// <summary>"fromTop" (por defecto: separacion exacta desde la corona, el resto queda abajo) o "maxSpacing".</summary>
        [JsonPropertyName("layoutMode")] public string LayoutMode { get; set; } = "fromTop";
        /// <summary>1 = una sola capa entre las patas de F7 (sigue por la cara exterior hasta zBase); 2 = ademas una capa en la cara del foso.</summary>
        [JsonPropertyName("layers")] public int Layers { get; set; } = 1;
        [JsonPropertyName("shape")] public string Shape { get; set; } = "segments";
        [JsonPropertyName("lapMm")] public double LapMm { get; set; } = 400;
    }

    /// <summary>Interruptores iniciales de la lamina.</summary>
    public class PreviewCfg
    {
        [JsonPropertyName("showDims")] public bool ShowDims { get; set; } = true;
        [JsonPropertyName("showLabels")] public bool ShowLabels { get; set; } = true;
        [JsonPropertyName("showCovers")] public bool ShowCovers { get; set; } = false;
        /// <summary>Capa de barras que se muestra en la planta: "F1", "F2" o "F3".</summary>
        [JsonPropertyName("planLayer")] public string PlanLayer { get; set; } = "F1";
    }

    /// <summary>Vistas de seccion A y B en Revit (fase 2b, opcional).</summary>
    public class SectionViewsCfg
    {
        [JsonPropertyName("enabled")] public bool Enabled { get; set; } = false;
        [JsonPropertyName("scale")] public int Scale { get; set; } = 20;
        [JsonPropertyName("marginMm")] public double MarginMm { get; set; } = 500;
        /// <summary>Profundidad de vista mas alla del plano de corte (mm).</summary>
        [JsonPropertyName("depthMm")] public double DepthMm { get; set; } = 500;
        /// <summary>Tipo de vista de seccion (ViewFamilyType) exacto o fragmento; vacio = el primero del proyecto.</summary>
        [JsonPropertyName("viewTypeName")] public string ViewTypeName { get; set; } = "";
        /// <summary>Comodines: {marca}, {id}, {letra} (A o B).</summary>
        [JsonPropertyName("nameTemplate")] public string NameTemplate { get; set; } = "{marca} - Sección {letra}";
        /// <summary>Familia (o fragmento) de etiqueta de barra; vacio = sin etiquetas.</summary>
        [JsonPropertyName("tagFamilyName")] public string TagFamilyName { get; set; } = "";
        /// <summary>Vista 3D activa en detalle fino (acero como solido en Revit 2027) con el acero del plugin sin ocultar.</summary>
        [JsonPropertyName("showSolid")] public bool ShowSolid { get; set; } = false;
    }

    /// <summary>
    /// Configuracion del add-in (config.json junto a la DLL). Los valores por defecto son los
    /// del plano IG-01-260275-104-0004-CV-DWG-0003 (fundaciones de transformadores, sala
    /// electrica N°1). Las claves del JSON se escriben exactamente como aqui (F1_bottomMesh...).
    /// </summary>
    public class AppConfig
    {
        /// <summary>Recubrimiento desde la cara inferior (contra el terreno) a la cara de la barra (mm).</summary>
        [JsonPropertyName("coverBottomMm")] public double CoverBottomMm { get; set; } = 75;
        /// <summary>Recubrimiento desde el tope (mm).</summary>
        [JsonPropertyName("coverTopMm")] public double CoverTopMm { get; set; } = 50;
        /// <summary>Recubrimiento en las caras exteriores del cuerpo macizo y en los huecos pasantes (mm).</summary>
        [JsonPropertyName("coverEdgeMm")] public double CoverEdgeMm { get; set; } = 75;
        /// <summary>Recubrimiento en las caras de foso y en las dos caras de los muretes (mm).</summary>
        [JsonPropertyName("coverWallMm")] public double CoverWallMm { get; set; } = 40;
        /// <summary>Ancho maximo de una region del tope para considerarla murete (mm); mas ancha = plataforma.</summary>
        [JsonPropertyName("wallMaxWidthMm")] public double WallMaxWidthMm { get; set; } = 300;

        [JsonPropertyName("direction")] public DirectionCfg Direction { get; set; } = new DirectionCfg();

        [JsonPropertyName("F1_bottomMesh")] public BottomMeshCfg F1 { get; set; } = new BottomMeshCfg();
        [JsonPropertyName("F2_recessMesh")] public RecessMeshCfg F2 { get; set; } = new RecessMeshCfg();
        [JsonPropertyName("F3_topMesh")] public TopMeshCfg F3 { get; set; } = new TopMeshCfg();
        [JsonPropertyName("F4_recessFaceL")] public RecessFaceLCfg F4 { get; set; } = new RecessFaceLCfg();
        [JsonPropertyName("F5_recessFaceH")] public RecessFaceHCfg F5 { get; set; } = new RecessFaceHCfg();
        [JsonPropertyName("F6_wallVertical")] public WallVerticalCfg F6 { get; set; } = new WallVerticalCfg();
        [JsonPropertyName("F7_wallHairpin")] public WallHairpinCfg F7 { get; set; } = new WallHairpinCfg();
        [JsonPropertyName("F8_wallHoriz")] public WallHorizCfg F8 { get; set; } = new WallHorizCfg();

        /// <summary>Plantilla del parametro Particion: {marca}, {id}, {tipo}, {familia} (F1...F8), {conjunto}, {capa}.</summary>
        [JsonPropertyName("partitionTemplate")] public string PartitionTemplate { get; set; } = "BLQ-{marca}-{familia}";
        /// <summary>Tolerancia geometrica (mm).</summary>
        [JsonPropertyName("toleranceMm")] public double ToleranceMm { get; set; } = 2;
        /// <summary>Longitud minima de un tramo recto para colocarlo (mm); los mas cortos se omiten con aviso.</summary>
        [JsonPropertyName("minBarLengthMm")] public double MinBarLengthMm { get; set; } = 300;

        /// <summary>Referencia de los niveles de la lamina: "shared" (coordenadas compartidas), "project" (punto base) o "internal".</summary>
        [JsonPropertyName("levelReference")] public string LevelReference { get; set; } = "shared";
        [JsonPropertyName("preview")] public PreviewCfg Preview { get; set; } = new PreviewCfg();
        [JsonPropertyName("sectionViews")] public SectionViewsCfg SectionViews { get; set; } = new SectionViewsCfg();

        // -----------------------------------------------------------------
        // Acceso uniforme por familia
        // -----------------------------------------------------------------

        public bool Enabled(Family f)
        {
            switch (f)
            {
                case Family.F1: return F1.Enabled;
                case Family.F2: return F2.Enabled;
                case Family.F3: return F3.Enabled;
                case Family.F4: return F4.Enabled;
                case Family.F5: return F5.Enabled;
                case Family.F6: return F6.Enabled;
                case Family.F7: return F7.Enabled;
                default: return F8.Enabled;
            }
        }

        public void SetEnabled(Family f, bool on)
        {
            switch (f)
            {
                case Family.F1: F1.Enabled = on; break;
                case Family.F2: F2.Enabled = on; break;
                case Family.F3: F3.Enabled = on; break;
                case Family.F4: F4.Enabled = on; break;
                case Family.F5: F5.Enabled = on; break;
                case Family.F6: F6.Enabled = on; break;
                case Family.F7: F7.Enabled = on; break;
                default: F8.Enabled = on; break;
            }
        }

        /// <summary>
        /// Tipos de barra que necesita cada familia activa: (familia, capa "u" / "v" / "",
        /// nombre del tipo). Las mallas llevan dos (u y v); el resto uno.
        /// </summary>
        public List<(Family family, string layer, string barTypeName)> BarTypesNeeded()
        {
            var list = new List<(Family, string, string)>();
            if (F1.Enabled) { list.Add((Family.F1, "u", F1.U.BarTypeName)); list.Add((Family.F1, "v", F1.V.BarTypeName)); }
            if (F2.Enabled) { list.Add((Family.F2, "u", F2.U.BarTypeName)); list.Add((Family.F2, "v", F2.V.BarTypeName)); }
            if (F3.Enabled) { list.Add((Family.F3, "u", F3.U.BarTypeName)); list.Add((Family.F3, "v", F3.V.BarTypeName)); }
            if (F4.Enabled) list.Add((Family.F4, "", F4.BarTypeName));
            if (F5.Enabled) list.Add((Family.F5, "", F5.BarTypeName));
            if (F6.Enabled) list.Add((Family.F6, "", F6.BarTypeName));
            if (F7.Enabled) list.Add((Family.F7, "", F7.BarTypeName));
            if (F8.Enabled) list.Add((Family.F8, "", F8.BarTypeName));
            return list;
        }

        /// <summary>Deja la configuracion en un estado coherente.</summary>
        public void Normalize()
        {
            if (Direction == null) Direction = new DirectionCfg();
            if (F1 == null) F1 = new BottomMeshCfg();
            if (F2 == null) F2 = new RecessMeshCfg();
            if (F3 == null) F3 = new TopMeshCfg();
            if (F4 == null) F4 = new RecessFaceLCfg();
            if (F5 == null) F5 = new RecessFaceHCfg();
            if (F6 == null) F6 = new WallVerticalCfg();
            if (F7 == null) F7 = new WallHairpinCfg();
            if (F8 == null) F8 = new WallHorizCfg();
            if (Preview == null) Preview = new PreviewCfg();
            if (SectionViews == null) SectionViews = new SectionViewsCfg();
            if (F1.U == null) F1.U = new LayerCfg(); if (F1.V == null) F1.V = new LayerCfg();
            if (F2.U == null) F2.U = new LayerCfg(); if (F2.V == null) F2.V = new LayerCfg();
            if (F3.U == null) F3.U = new LayerCfg(); if (F3.V == null) F3.V = new LayerCfg();

            Direction.Mode = NormalizeDirection(Direction.Mode);
            foreach (LayerCfg l in new[] { F1.U, F1.V, F2.U, F2.V, F3.U, F3.V })
            {
                if (l.BarTypeName == null) l.BarTypeName = "";
                if (l.SpacingMm <= 0) l.SpacingMm = 125;
                l.LayoutMode = NormalizeLayout(l.LayoutMode, "maxSpacing");
            }
            F4.LayoutMode = NormalizeLayout(F4.LayoutMode, "maxSpacing");
            F5.LayoutMode = NormalizeLayout(F5.LayoutMode, "fromTop");
            F6.LayoutMode = NormalizeLayout(F6.LayoutMode, "maxSpacing");
            F7.LayoutMode = NormalizeLayout(F7.LayoutMode, "maxSpacing");
            F8.LayoutMode = NormalizeLayout(F8.LayoutMode, "fromTop");
            if (F4.BarTypeName == null) F4.BarTypeName = ""; if (F4.SpacingMm <= 0) F4.SpacingMm = 125;
            if (F5.BarTypeName == null) F5.BarTypeName = ""; if (F5.SpacingMm <= 0) F5.SpacingMm = 200;
            if (F6.BarTypeName == null) F6.BarTypeName = ""; if (F6.SpacingMm <= 0) F6.SpacingMm = 125;
            if (F7.BarTypeName == null) F7.BarTypeName = ""; if (F7.SpacingMm <= 0) F7.SpacingMm = 125;
            if (F8.BarTypeName == null) F8.BarTypeName = ""; if (F8.SpacingMm <= 0) F8.SpacingMm = 200;
            if (F1.LegUpMm < 0) F1.LegUpMm = 0;
            if (F2.LegDownMm < 0) F2.LegDownMm = 0;
            if (F2.BelowRecessFloorMm < 0) F2.BelowRecessFloorMm = 0;
            if (F2.AnchorageMm < 0) F2.AnchorageMm = 0;
            F2.Extent = NormalizeExtent(F2.Extent);
            if (F3.LegDownMm < 0) F3.LegDownMm = 0;
            if (F4.VerticalMm < 0) F4.VerticalMm = 0;
            if (F4.FootMm < 0) F4.FootMm = 0;
            F5.Shape = NormalizeShape(F5.Shape); if (F5.LapMm < 0) F5.LapMm = 0;
            if (F7.LegMm < 0) F7.LegMm = 0;
            F7.Placement = NormalizePlacement(F7.Placement);
            if (F8.Layers != 2) F8.Layers = 1;
            F8.Shape = NormalizeShape(F8.Shape); if (F8.LapMm < 0) F8.LapMm = 0;

            if (CoverBottomMm < 0) CoverBottomMm = 0;
            if (CoverTopMm < 0) CoverTopMm = 0;
            if (CoverEdgeMm < 0) CoverEdgeMm = 0;
            if (CoverWallMm < 0) CoverWallMm = 0;
            if (WallMaxWidthMm <= 0) WallMaxWidthMm = 300;
            if (ToleranceMm <= 0) ToleranceMm = 2;
            if (MinBarLengthMm < 0) MinBarLengthMm = 0;
            if (string.IsNullOrWhiteSpace(PartitionTemplate)) PartitionTemplate = "BLQ-{marca}-{familia}";
            LevelReference = NormalizeLevelReference(LevelReference);
            Preview.PlanLayer = Families.Code(Families.Parse(Preview.PlanLayer));
            if (Preview.PlanLayer != "F1" && Preview.PlanLayer != "F2" && Preview.PlanLayer != "F3") Preview.PlanLayer = "F1";
            if (SectionViews.Scale <= 0) SectionViews.Scale = 20;
            if (SectionViews.MarginMm < 0) SectionViews.MarginMm = 0;
            if (SectionViews.DepthMm <= 0) SectionViews.DepthMm = 500;
            if (SectionViews.ViewTypeName == null) SectionViews.ViewTypeName = "";
            if (string.IsNullOrWhiteSpace(SectionViews.NameTemplate)) SectionViews.NameTemplate = "{marca} - Sección {letra}";
            if (SectionViews.TagFamilyName == null) SectionViews.TagFamilyName = "";
        }

        /// <summary>"long", "short", "x", "y" o "angle"; cualquier otra cosa es "long".</summary>
        public static string NormalizeDirection(string mode)
        {
            string m = (mode ?? "").Trim().ToLowerInvariant();
            switch (m)
            {
                case "short": case "corto": return "short";
                case "x": return "x";
                case "y": return "y";
                case "angle": case "angulo": return "angle";
                default: return "long";
            }
        }

        /// <summary>"maxSpacing" o "fromTop"; vacio o desconocido = el valor por defecto de la familia.</summary>
        public static string NormalizeLayout(string m, string def)
        {
            switch ((m ?? "").Trim().ToLowerInvariant())
            {
                case "maxspacing": case "max": return "maxSpacing";
                case "fromtop": case "exact": case "top": return "fromTop";
                default: return def;
            }
        }

        public static string NormalizeExtent(string e) =>
            (e ?? "").Trim().ToLowerInvariant() == "recess" ? "recess" : "full";

        public static string NormalizeShape(string s) =>
            (s ?? "").Trim().ToLowerInvariant() == "ring" ? "ring" : "segments";

        public static string NormalizePlacement(string p) =>
            (p ?? "").Trim().ToLowerInvariant() == "staggered" ? "staggered" : "aligned";

        public static string NormalizeLevelReference(string r)
        {
            switch ((r ?? "").Trim().ToLowerInvariant())
            {
                case "project": case "base": return "project";
                case "internal": case "interna": return "internal";
                default: return "shared";
            }
        }

        public static string LevelReferenceName(string r)
        {
            switch (NormalizeLevelReference(r))
            {
                case "project": return "punto base del proyecto";
                case "internal": return "cota interna del modelo";
                default: return "coordenadas compartidas (punto de reconocimiento)";
            }
        }

        public static string ConfigPath()
        {
            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            return Path.Combine(dir ?? "", "config.json");
        }

        private static JsonSerializerOptions ReadOptions() => new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private static JsonSerializerOptions WriteOptions() => new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static AppConfig Load() => Load(ConfigPath());

        public static AppConfig Load(string path)
        {
            AppConfig cfg = File.Exists(path)
                ? JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), ReadOptions()) ?? new AppConfig()
                : new AppConfig();
            cfg.Normalize();
            return cfg;
        }

        /// <summary>Guarda esta configuracion como config.json junto a la DLL (valores por defecto de la interfaz).</summary>
        public void Save(string path = null)
        {
            File.WriteAllText(path ?? ConfigPath(), JsonSerializer.Serialize(this, WriteOptions()));
        }

        public string ToJson() => JsonSerializer.Serialize(this, WriteOptions());

        /// <summary>Copia independiente, para que la interfaz edite sin tocar la configuracion cargada.</summary>
        public AppConfig Clone()
        {
            AppConfig c = JsonSerializer.Deserialize<AppConfig>(JsonSerializer.Serialize(this, WriteOptions()), ReadOptions())
                          ?? new AppConfig();
            c.Normalize();
            return c;
        }
    }
}
