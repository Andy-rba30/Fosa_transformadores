using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Arba.Comun;

namespace BlockRebar
{
    /// <summary>
    /// Resultado del analisis de un bloque seleccionado, antes de armar nada: la geometria
    /// leida (o el motivo del rechazo), la direccion propia elegida en la ventana y el
    /// diagnostico completo para "Analizar sin armar".
    /// </summary>
    public sealed class HostAnalysis
    {
        public Element Host;
        public string Tag;
        public string Mark = "", TypeName = "", FamilyName = "";

        public BlockOutline Outline;
        public string Error;
        public List<string> Diagnostics = new List<string>();

        /// <summary>Direccion propia: "" (la general), "long", "short", "x" o "y".</summary>
        public string DirectionOverride = "";

        /// <summary>
        /// Conjuntos de armadura que el plugin ya creo en este elemento: por "ARBA - Origen" = BLOQUES (contrato) y,
        /// por compatibilidad con modelos no migrados, por el comentario antiguo "BlockRebar F#".
        /// </summary>
        public List<ElementId> PluginRebars = new List<ElementId>();
        /// <summary>Angulos y rejillas que el plugin ya coloco en este elemento (origen BLOQUES + anfitrion, o comentario antiguo).</summary>
        public List<ElementId> PluginGridItems = new List<ElementId>();
        /// <summary>True si el elemento tiene armadura del plugin anterior al contrato (particion "BLQ-..." sin "ARBA - Origen").</summary>
        public bool HasLegacyRebars;
        /// <summary>Tipo de rejilla propio ("" = el por defecto).</summary>
        public string GridTypeOverride = "";

        public bool CanBuild => Error == null && Outline != null && Topology(null) != null && Topology(null).Error == null;

        public string DirectionMode(AppConfig cfg) =>
            AppConfig.NormalizeDirection(string.IsNullOrWhiteSpace(DirectionOverride) ? cfg.Direction.Mode : DirectionOverride);

        private AppConfig _lastCfg;

        /// <summary>El bloque en el sistema local de la direccion efectiva.</summary>
        public BlockFrame Frame(AppConfig cfg)
        {
            if (Outline == null) return null;
            if (cfg != null) _lastCfg = cfg;
            AppConfig c = cfg ?? _lastCfg ?? new AppConfig();
            return Outline.Frame(DirectionMode(c), c.Direction.AngleDeg, BlockOutline.Mm(c.WallMaxWidthMm));
        }

        public BlockTopology Topology(AppConfig cfg) => Frame(cfg)?.Topology;

        public string Kind => Error != null ? "SIN ARMAR" : (Topology(null)?.Error != null ? "RECHAZADO" : "Bloque");

        public string Detail
        {
            get
            {
                if (Error != null) return Error;
                BlockTopology t = Topology(null);
                return t == null ? Outline.Describe() : (t.Error != null ? t.Error : t.Describe());
            }
        }

        /// <summary>
        /// Particion del contrato ARBA para un conjunto: la categoria la deduce del anfitrion (bloques = CIMIENTOS),
        /// el prefijo es BLQ y el codigo es la familia F1...F8 ("CIMIENTOS - BLQ-FT-01-F4" con la plantilla por
        /// defecto). La capa (u/v) no entra en la particion; queda en el nombre del conjunto y en el informe.
        /// </summary>
        public string Partition(AppConfig cfg, string setName, string familyCode, string layer = "")
        {
            return ArbaPartition.BuildFor(Host, ArbaContract.Bloques, cfg.PartitionTemplate, new PartitionName.Source
            {
                Mark = Mark, Id = Host.Id.ToString(), TypeName = TypeName, FamilyName = FamilyName, SetName = setName, Code = familyCode ?? ""
            });
        }

        /// <summary>Rellena PluginRebars, PluginGridItems y HasLegacyRebars (lectura, sin transaccion).</summary>
        public void FindPluginElements(Document doc)
        {
            PluginRebars = RebarGenerator.FindPluginRebars(doc, Host);
            if (PluginRebars.Count > 0) Diagnostics.Add("ya tiene " + PluginRebars.Count + " conjunto(s) de armadura creados por el plugin");
            PluginGridItems = GridGenerator.FindPluginItems(doc, Host);
            if (PluginGridItems.Count > 0) Diagnostics.Add("ya tiene " + PluginGridItems.Count + " angulo(s)/rejilla(s) colocados por el plugin");
            try { HasLegacyRebars = ArbaMigration.HasLegacy(doc, Host, ArbaContract.Bloques); }
            catch (Exception ex) { HasLegacyRebars = false; Log.Error("HasLegacy " + Tag, ex); }
            if (HasLegacyRebars) Diagnostics.Add("su armadura del plugin es anterior al contrato ARBA (particion BLQ-... sin \"ARBA - Origen\"): se puede migrar sin rearmar");
        }

        public static HostAnalysis Analyze(Document doc, Element host, AppConfig cfg)
        {
            var a = new HostAnalysis { Host = host, Tag = "[" + host.Id + " " + host.Name + "] " };
            try
            {
                a.Mark = host.get_Parameter(BuiltInParameter.ALL_MODEL_MARK)?.AsString() ?? "";
                a.TypeName = BlockOutline.TypeNameOf(doc, host) ?? "";
                a.FamilyName = BlockOutline.FamilyNameOf(doc, host) ?? "";
                a.Diagnostics.Add("elemento " + a.Tag + "familia \"" + a.FamilyName + "\", tipo \"" + a.TypeName + "\", marca \"" + a.Mark + "\"");

                RebarHostData hd = RebarHostData.GetRebarHostData(host);
                if (hd == null || !hd.IsValidHost())
                {
                    a.Error = "no admite armadura. Revisa que la cimentacion sea estructural y que su material sea hormigon.";
                    a.Diagnostics.Add("RECHAZADO: " + a.Error);
                    return a;
                }

                a.Outline = BlockOutline.Probe(doc, host, cfg, a.Diagnostics);
                if (a.Outline == null)
                {
                    a.Error = "RECHAZADO, " + (BlockOutline.LastError ?? "no se pudo deducir la geometria (motivo desconocido)") + ". No se ha creado ninguna barra.";
                    a.Diagnostics.Add("MOTIVO DE RECHAZO: " + a.Error);
                }
                else
                {
                    a._lastCfg = cfg;
                    BlockTopology t = a.Topology(cfg);
                    a.Diagnostics.Add("sistema local: " + a.Frame(cfg).Describe());
                    a.Diagnostics.AddRange(t.DescribeDetailed().Split(new[] { Environment.NewLine }, StringSplitOptions.None).Select(l => "  " + l));
                }
            }
            catch (Exception ex)
            {
                a.Error = "ERROR: " + ex.Message;
                a.Diagnostics.Add("ERROR: " + ex);
                Log.Error("Analyze " + a.Tag, ex);
            }
            return a;
        }

        /// <summary>
        /// Informe del modo "Analizar sin armar": caras y cotas (internas y en la referencia
        /// elegida), fondos de foso, regiones del tope con su ancho, motivo de rechazo o
        /// resumen del armado previsto con sus avisos.
        /// </summary>
        public string Report(AppConfig cfg, double elevationOffset, BlockPlan plan)
        {
            var lines = new List<string>();
            lines.Add("== " + Tag.Trim() + " ==");
            string refName = AppConfig.LevelReferenceName(cfg.LevelReference);
            if (Outline != null)
            {
                lines.Add("cotas: zBase interna " + BlockOutline.ToMm(Outline.ZBottom) + " mm = " + Elev(Outline.ZBottom + elevationOffset) + " m (" + refName + "); zTope " +
                          Elev(Outline.ZTop + elevationOffset) + " m; canto " + BlockOutline.ToMm(Outline.Thickness) + " mm");
                foreach ((List<List<Pt>> rings, double z) in Outline.Floors)
                    lines.Add("fondo de foso a z interna " + BlockOutline.ToMm(z) + " mm = " + Elev(z + elevationOffset) + " m, profundidad " + BlockOutline.ToMm(Outline.ZTop - z) + " mm, " + rings.Count + " anillo(s)");
            }
            lines.AddRange(Diagnostics);
            if (plan != null)
            {
                lines.Add(plan.Error != null ? "ARMADO: SIN ARMAR, " + plan.Error : "ARMADO PREVISTO: " + plan.Describe());
                foreach (string w in plan.Warnings) lines.Add("  aviso: " + w);
                if (plan.Error == null)
                {
                    lines.Add(plan.QuantityTable());
                    ClashReport cr = ClashCheck.Check(plan, BlockPlan.Mm(1));
                    lines.Add(cr.Describe(10));
                    lines.Add(plan.CheckSpacing(5).Describe());
                }
            }
            return string.Join(Environment.NewLine, lines);
        }

        private static string Elev(double ft) => (ft * 0.3048).ToString("0.000", CultureInfo.InvariantCulture);
    }
}
