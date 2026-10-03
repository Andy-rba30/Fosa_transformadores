using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace BlockRebar
{
    /// <summary>
    /// Ventana previa al armado, con la lamina del plano: lista de bloques seleccionados (lo
    /// detectado o el motivo de rechazo, direccion propia), panel por familia F1...F8,
    /// recubrimientos, ancho maximo de murete, referencia de niveles y particion; a la
    /// derecha la planta con las lineas de corte A-A y B-B arrastrables y las dos secciones
    /// con el armado previsto. "Analizar sin armar" abre el informe de caras y motivos.
    /// Construida en codigo (sin XAML). En la entrega 2a el boton Armar esta desactivado.
    /// </summary>
    public sealed class RebarOptionsWindow : Window
    {
        private readonly AppConfig _cfg;
        private readonly IList<BarTypes.Info> _barTypes;
        private readonly IList<HostAnalysis> _items;
        private readonly Func<string, double> _elevationOffset;
        private readonly PreviewState _state = new PreviewState();

        /// <summary>Configuracion final si el usuario pulso "Armar"; null si cancelo.</summary>
        public AppConfig Result { get; private set; }
        /// <summary>True cuando el armado en Revit esta disponible (entrega 2b); en 2a el boton Armar esta desactivado.</summary>
        public static bool BuildAvailable = false;

        // controles
        private ComboBox _dir;
        private TextBox _angle;
        private readonly Dictionary<Family, CheckBox> _on = new Dictionary<Family, CheckBox>();
        private readonly Dictionary<Family, FrameworkElement> _panels = new Dictionary<Family, FrameworkElement>();
        private readonly Dictionary<string, ComboBox> _types = new Dictionary<string, ComboBox>();
        private readonly Dictionary<string, TextBox> _nums = new Dictionary<string, TextBox>();
        private readonly Dictionary<string, ComboBox> _choices = new Dictionary<string, ComboBox>();
        private readonly Dictionary<string, string[]> _choiceValues = new Dictionary<string, string[]>();
        private TextBox _coverB, _coverT, _coverE, _coverW, _wallMax, _partition;
        private ComboBox _levelRef, _planLayer;
        private CheckBox _showDims, _showLabels, _showCovers;
        private TextBlock _message, _partitionPreview, _caption, _status;
        private Button _buildButton, _analyzeButton;
        private PlanPreview _plan;
        private SectionPreview _secA, _secB;
        private readonly Dictionary<Family, Border> _legend = new Dictionary<Family, Border>();

        private readonly Dictionary<HostAnalysis, (Run kind, Run detail)> _itemRuns = new Dictionary<HostAnalysis, (Run, Run)>();
        private readonly Dictionary<HostAnalysis, Border> _itemRows = new Dictionary<HostAnalysis, Border>();
        private HostAnalysis _selected;
        private bool _building = true, _refreshing;
        private bool _strictTypes;

        // ultimo calculo del elemento seleccionado
        private BlockFrame _lastFrame;
        private BlockPlan _lastPlan;
        private AppConfig _lastCfg;
        private double _lastCutA = double.NaN, _lastCutB = double.NaN;
        private SectionCut _cutA, _cutB;

        private static readonly string[] DirModes = { "long", "short", "x", "y", "angle" };
        private static readonly string[] DirLabels = { "lado largo (lo normal)", "lado corto", "X del proyecto", "Y del proyecto", "angulo (grados)" };
        private static readonly string[] LayoutValues = { "maxSpacing", "fromTop" };
        private static readonly string[] LayoutLabels = { "max. (reparto)", "exacta desde el inicio" };
        private static readonly Thickness Pad = new Thickness(4, 2, 4, 2);
        private static readonly Brush SelectedBrush = RevitTheme.Selection;

        public RebarOptionsWindow(AppConfig cfg, IList<BarTypes.Info> barTypes, IList<HostAnalysis> items, Func<string, double> elevationOffset)
        {
            _cfg = cfg;
            _cfg.Normalize();
            _barTypes = barTypes ?? new List<BarTypes.Info>();
            _items = items;
            _elevationOffset = elevationOffset ?? (r => 0);

            Title = "Armar bloques con foso";
            Width = 1560; Height = 940;
            MinWidth = 1100; MinHeight = 680;
            try
            {
                Rect wa = SystemParameters.WorkArea;
                Width = Math.Min(Width, wa.Width - 20);
                Height = Math.Min(Height, wa.Height - 20);
            }
            catch { }
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            FontSize = 12;

            _state.PlanLayer = Families.Parse(_cfg.Preview.PlanLayer);
            _state.ShowDims = _cfg.Preview.ShowDims;
            _state.ShowLabels = _cfg.Preview.ShowLabels;
            _state.ShowCovers = _cfg.Preview.ShowCovers;

            RevitTheme.Apply(this);
            Content = BuildRoot();
            _selected = _items.FirstOrDefault(i => i.CanBuild) ?? _items.FirstOrDefault(i => i.Outline != null);
            if (_selected != null) SelectItem(_selected);
            _building = false;
            _state.Changed += OnStateChanged;
            Refresh();
        }

        // ------------------------------------------------------------------
        // Construccion de la interfaz
        // ------------------------------------------------------------------
        private UIElement BuildRoot()
        {
            var root = new Grid { Margin = new Thickness(10) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            UIElement elements = BuildElements();
            Grid.SetRow(elements, 0);
            root.Children.Add(elements);

            var body = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(400) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new StackPanel();
            left.Children.Add(BuildDirection());
            left.Children.Add(BuildF1());
            left.Children.Add(BuildF2());
            left.Children.Add(BuildF3());
            left.Children.Add(BuildF4());
            left.Children.Add(BuildF5());
            left.Children.Add(BuildF6());
            left.Children.Add(BuildF7());
            left.Children.Add(BuildF8());
            left.Children.Add(BuildGeneral());
            var scroll = new ScrollViewer
            {
                Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(scroll, 0);
            body.Children.Add(scroll);

            UIElement lamina = BuildLamina();
            Grid.SetColumn(lamina, 1);
            body.Children.Add(lamina);

            Grid.SetRow(body, 1);
            root.Children.Add(body);

            UIElement buttons = BuildButtons();
            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);
            return root;
        }

        private UIElement BuildElements()
        {
            int ok = _items.Count(i => i.CanBuild);
            var group = new GroupBox
            {
                Header = "Bloques seleccionados: " + _items.Count + " (" + ok + " armables). Haz clic en uno para verlo en la lamina. " +
                         "A la derecha, la direccion propia de u (barras principales de las mallas) de cada uno (general = la elegida abajo).",
                Padding = new Thickness(4)
            };
            var panel = new StackPanel();
            foreach (HostAnalysis item in _items)
            {
                var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var text = new TextBlock { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
                text.Inlines.Add(new Run(item.Tag) { FontWeight = FontWeights.Bold });
                var kindRun = new Run(item.Kind + ": ") { FontWeight = FontWeights.SemiBold, Foreground = item.CanBuild ? RevitTheme.Ok : RevitTheme.Error };
                var detailRun = new Run(item.Detail);
                text.Inlines.Add(kindRun);
                text.Inlines.Add(detailRun);
                _itemRuns[item] = (kindRun, detailRun);
                Grid.SetColumn(text, 0);
                row.Children.Add(text);

                if (item.Outline != null)
                {
                    var side = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                    HostAnalysis captured = item;
                    side.Children.Add(new TextBlock { Text = "Direccion:", Margin = new Thickness(10, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
                    var dir = new ComboBox { Width = 170, ToolTip = "Direccion de u (barras principales de las mallas) de este bloque. General = la elegida abajo." };
                    dir.Items.Add("(general)");
                    for (int i = 0; i < 4; i++) dir.Items.Add(DirLabels[i]);
                    int di = Array.IndexOf(DirModes, item.DirectionOverride);
                    dir.SelectedIndex = di >= 0 && di < 4 ? di + 1 : 0;
                    dir.SelectionChanged += (s, e) => { captured.DirectionOverride = dir.SelectedIndex <= 0 ? "" : DirModes[dir.SelectedIndex - 1]; Refresh(); };
                    side.Children.Add(dir);
                    Grid.SetColumn(side, 1);
                    row.Children.Add(side);
                }

                var border = new Border { Child = row, Padding = new Thickness(4, 2, 4, 2), CornerRadius = new CornerRadius(3) };
                if (item.Outline != null)
                {
                    border.Cursor = Cursors.Hand;
                    HostAnalysis captured = item;
                    border.MouseLeftButtonDown += (s, e) => { SelectItem(captured); Refresh(); };
                }
                _itemRows[item] = border;
                panel.Children.Add(border);
            }
            group.Content = new ScrollViewer
            {
                Content = panel, MaxHeight = 140,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            return group;
        }

        private void SelectItem(HostAnalysis item)
        {
            _selected = item;
            foreach (var kv in _itemRows)
                kv.Value.Background = kv.Key == item ? SelectedBrush : Brushes.Transparent;
        }

        private UIElement BuildDirection()
        {
            var group = new GroupBox { Header = "Direccion de las mallas", Padding = new Thickness(4) };
            var grid = FormGrid();
            int r = 0;
            _dir = new ComboBox { Margin = Pad };
            foreach (string l in DirLabels) _dir.Items.Add(l);
            _dir.SelectedIndex = Math.Max(0, Array.IndexOf(DirModes, _cfg.Direction.Mode));
            AddRow(grid, r++, "Direccion (u):", _dir,
                   "Direccion de las barras principales de las mallas: la capa mas baja de F1 y la mas alta de F2 y F3 van a lo largo de u. " +
                   "Paralela al lado largo del contorno (lo normal), al lado corto, a los ejes X o Y del proyecto, o un angulo. Cambiable bloque a bloque en la lista de arriba.");
            _angle = NumBox(_cfg.Direction.AngleDeg);
            AddRow(grid, r++, "Angulo (grados):", _angle, "Angulo de u respecto al eje X del proyecto, solo con la opcion \"angulo\".");
            group.Content = grid;
            return group;
        }

        private GroupBox FamilyGroup(Family f, Grid grid)
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new System.Windows.Shapes.Rectangle { Width = 12, Height = 12, Fill = PlanColors.Of(f), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            var on = new CheckBox { Content = Families.Code(f) + "  " + Families.Name(f), IsChecked = _cfg.Enabled(f), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            Hook(on);
            _on[f] = on;
            header.Children.Add(on);
            _panels[f] = grid;
            return new GroupBox { Header = header, Content = grid, Padding = new Thickness(4) };
        }

        private UIElement BuildF1()
        {
            var grid = FormGrid(); int r = 0;
            AddRow(grid, r++, "Capa u (abajo):", LayerRow("F1:u", _cfg.F1.U), "Barras a lo largo de u, la capa mas baja, a recubrimiento inferior + medio diametro de la cara inferior.");
            AddRow(grid, r++, "Capa v (encima):", LayerRow("F1:v", _cfg.F1.V), "Barras a lo largo de v, apoyadas sobre la capa u.");
            AddRow(grid, r++, "Pata arriba (mm):", Num("F1:leg", _cfg.F1.LegUpMm), "Pata vertical hacia arriba en los extremos que dan al borde exterior (en los bordes de hueco la barra va recta).");
            return FamilyGroup(Family.F1, grid);
        }

        private UIElement BuildF2()
        {
            var grid = FormGrid(); int r = 0;
            var ext = new StackPanel { Orientation = Orientation.Horizontal };
            ext.Children.Add(Choice("F2:extent", new[] { "full", "recess" }, new[] { "todo el contorno", "solo bajo los fosos" }, _cfg.F2.Extent, 150));
            ext.Children.Add(new TextBlock { Text = "anclaje (mm):", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            ext.Children.Add(Num("F2:anch", _cfg.F2.AnchorageMm));
            AddRow(grid, r++, "Extension:", ext, "Todo el contorno inferior, o solo bajo los fosos prolongando las barras el anclaje dentro del hormigon contiguo.");
            AddRow(grid, r++, "Bajo el fondo (mm):", Num("F2:below", _cfg.F2.BelowRecessFloorMm), "Distancia del fondo del foso mas profundo a la cara superior de la malla (75 mm en el plano).");
            AddRow(grid, r++, "Capa u (arriba):", LayerRow("F2:u", _cfg.F2.U), "Barras a lo largo de u, la capa mas alta de la malla bajo foso.");
            AddRow(grid, r++, "Capa v (debajo):", LayerRow("F2:v", _cfg.F2.V), "Barras a lo largo de v, colgadas bajo la capa u.");
            AddRow(grid, r++, "Pata abajo (mm):", Num("F2:leg", _cfg.F2.LegDownMm), "Pata vertical hacia abajo en los extremos exteriores.");
            return FamilyGroup(Family.F2, grid);
        }

        private UIElement BuildF3()
        {
            var grid = FormGrid(); int r = 0;
            AddRow(grid, r++, "Capa u (arriba):", LayerRow("F3:u", _cfg.F3.U), "Barras a lo largo de u, la capa mas alta de la malla superior de cada plataforma, a recubrimiento superior + medio diametro del tope.");
            AddRow(grid, r++, "Capa v (debajo):", LayerRow("F3:v", _cfg.F3.V), "Barras a lo largo de v, colgadas bajo la capa u.");
            AddRow(grid, r++, "Pata abajo (mm):", Num("F3:leg", _cfg.F3.LegDownMm), "Pata vertical hacia abajo en todos los bordes de la plataforma (exteriores y de foso).");
            return FamilyGroup(Family.F3, grid);
        }

        private UIElement BuildF4()
        {
            var grid = FormGrid(); int r = 0;
            AddRow(grid, r++, "Barra:", BarRow("F4", _cfg.F4.BarTypeName, _cfg.F4.SpacingMm, _cfg.F4.LayoutMode), "Tipo, separacion a lo largo de la cara y modo de reparto de las L en las caras de foso del lado plataforma.");
            var dims = new StackPanel { Orientation = Orientation.Horizontal };
            dims.Children.Add(Num("F4:vert", _cfg.F4.VerticalMm));
            dims.Children.Add(new TextBlock { Text = "vertical, pie (mm):", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            dims.Children.Add(Num("F4:foot", _cfg.F4.FootMm));
            AddRow(grid, r++, "Tramos (mm):", dims, "Longitud del tramo vertical desde el tope (recubrimiento) hacia abajo y del pie horizontal que apunta bajo el foso.");
            return FamilyGroup(Family.F4, grid);
        }

        private UIElement BuildF5()
        {
            var grid = FormGrid(); int r = 0;
            AddRow(grid, r++, "Barra:", BarRow("F5", _cfg.F5.BarTypeName, _cfg.F5.SpacingMm, _cfg.F5.LayoutMode), "Tipo, separacion vertical y reparto (exacta desde el tope, como en el plano) de las horizontales en las caras de foso del lado plataforma.");
            var sh = new StackPanel { Orientation = Orientation.Horizontal };
            sh.Children.Add(Choice("F5:shape", new[] { "segments", "ring" }, new[] { "tramos por cara", "anillo cerrado" }, _cfg.F5.Shape, 150));
            sh.Children.Add(new TextBlock { Text = "prolongacion / traslape (mm):", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            sh.Children.Add(Num("F5:lap", _cfg.F5.LapMm));
            AddRow(grid, r++, "Forma:", sh, "Tramos rectos por cara prolongados mas alla del cruce con la cara contigua, o anillo cerrado por traslape.");
            return FamilyGroup(Family.F5, grid);
        }

        private UIElement BuildF6()
        {
            var grid = FormGrid(); int r = 0;
            AddRow(grid, r++, "Barra:", BarRow("F6", _cfg.F6.BarTypeName, _cfg.F6.SpacingMm, _cfg.F6.LayoutMode), "Verticales en la cara exterior del murete, de altura completa, traslapadas con F1.");
            return FamilyGroup(Family.F6, grid);
        }

        private UIElement BuildF7()
        {
            var grid = FormGrid(); int r = 0;
            AddRow(grid, r++, "Barra:", BarRow("F7", _cfg.F7.BarTypeName, _cfg.F7.SpacingMm, _cfg.F7.LayoutMode), "Horquillas en U invertida sobre la corona del murete (doblado de estribo del tipo de barra).");
            var pl = new StackPanel { Orientation = Orientation.Horizontal };
            pl.Children.Add(Num("F7:leg", _cfg.F7.LegMm));
            pl.Children.Add(new TextBlock { Text = "patas (mm), posicion:", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            pl.Children.Add(Choice("F7:place", new[] { "aligned", "staggered" }, new[] { "alineada con F6", "al tresbolillo" }, _cfg.F7.Placement, 130));
            AddRow(grid, r++, "Patas:", pl, "Longitud de las patas hacia abajo y si las horquillas van en las mismas posiciones que F6 (traslape por contacto) o desplazadas media separacion.");
            return FamilyGroup(Family.F7, grid);
        }

        private UIElement BuildF8()
        {
            var grid = FormGrid(); int r = 0;
            AddRow(grid, r++, "Barra:", BarRow("F8", _cfg.F8.BarTypeName, _cfg.F8.SpacingMm, _cfg.F8.LayoutMode), "Horizontales del murete, de la corona a la base (exacta desde la corona, como en el plano).");
            var ly = new StackPanel { Orientation = Orientation.Horizontal };
            ly.Children.Add(Choice("F8:layers", new[] { "1", "2" }, new[] { "1 capa (exterior)", "2 capas (y cara de foso)" }, _cfg.F8.Layers.ToString(), 150));
            ly.Children.Add(Choice("F8:shape", new[] { "segments", "ring" }, new[] { "tramos por cara", "anillo cerrado" }, _cfg.F8.Shape, 120));
            AddRow(grid, r++, "Capas y forma:", ly, "Una capa entre las patas de F7 (sigue por la cara exterior hasta la base) o ademas otra en la cara del foso; tramos por cara o anillo.");
            AddRow(grid, r++, "Prolongacion (mm):", Num("F8:lap", _cfg.F8.LapMm), "Prolongacion mas alla del cruce con la cara contigua, o traslape del anillo.");
            return FamilyGroup(Family.F8, grid);
        }

        private UIElement BuildGeneral()
        {
            var group = new GroupBox { Header = "Recubrimientos, muretes, niveles y particion", Padding = new Thickness(4) };
            var grid = FormGrid();
            int r = 0;
            var cov = new StackPanel { Orientation = Orientation.Horizontal };
            _coverB = NumBox(_cfg.CoverBottomMm); _coverT = NumBox(_cfg.CoverTopMm);
            Hook(_coverB); Hook(_coverT);
            cov.Children.Add(_coverB);
            cov.Children.Add(new TextBlock { Text = "inferior,", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            cov.Children.Add(_coverT);
            cov.Children.Add(new TextBlock { Text = "superior", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            AddRow(grid, r++, "Recubrimientos (mm):", cov, "Distancia de la cara inferior (contra el terreno, 75 mm) y del tope (50 mm) a la cara de la barra mas proxima.");
            var cov2 = new StackPanel { Orientation = Orientation.Horizontal };
            _coverE = NumBox(_cfg.CoverEdgeMm); _coverW = NumBox(_cfg.CoverWallMm);
            Hook(_coverE); Hook(_coverW);
            cov2.Children.Add(_coverE);
            cov2.Children.Add(new TextBlock { Text = "borde exterior,", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            cov2.Children.Add(_coverW);
            cov2.Children.Add(new TextBlock { Text = "foso y murete", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            AddRow(grid, r++, "", cov2, "Recubrimiento en las caras exteriores del cuerpo macizo y huecos pasantes (75 mm) y en las caras de foso y las dos caras de los muretes (40 mm).");
            _wallMax = NumBox(_cfg.WallMaxWidthMm); Hook(_wallMax);
            AddRow(grid, r++, "Murete hasta (mm):", _wallMax, "Ancho maximo de una region del tope para considerarla murete (F6, F7, F8); mas ancha es plataforma (F3, F4, F5). 300 mm en el plano.");
            _levelRef = new ComboBox { Margin = Pad };
            foreach (string l in new[] { "coordenadas compartidas (punto de reconocimiento)", "punto base del proyecto", "cota interna del modelo" }) _levelRef.Items.Add(l);
            _levelRef.SelectedIndex = Array.IndexOf(new[] { "shared", "project", "internal" }, AppConfig.NormalizeLevelReference(_cfg.LevelReference));
            AddRow(grid, r++, "Niveles en:", _levelRef, "Referencia de las cotas de los niveles de las secciones (tope, fondos de foso, cara inferior). Los planos vienen en cotas absolutas: coordenadas compartidas.");
            _partition = new TextBox { Text = _cfg.PartitionTemplate, Margin = Pad };
            AddRow(grid, r++, "Particion:", _partition, "Plantilla del parametro Particion de cada barra. Comodines: " + PartitionName.Help);
            _partitionPreview = new TextBlock { Foreground = RevitTheme.Muted, Margin = Pad, TextWrapping = TextWrapping.Wrap };
            AddRow(grid, r++, "", _partitionPreview, null);
            group.Content = grid;
            return group;
        }

        private UIElement BuildLamina()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });

            // --- planta ---
            var planGroup = new GroupBox { Header = "PLANTA (arrastra las lineas A-A y B-B; rueda: zoom, arrastrar: mover, doble clic: encajar)", Padding = new Thickness(4) };
            var planPanel = new DockPanel();
            var head = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            head.Children.Add(new TextBlock { Text = "Capa en planta:", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            _planLayer = new ComboBox { Width = 190, Margin = Pad };
            foreach (Family f in new[] { Family.F1, Family.F2, Family.F3 }) _planLayer.Items.Add(Families.Code(f) + " " + Families.Name(f));
            _planLayer.SelectedIndex = Math.Max(0, Math.Min(2, (int)_state.PlanLayer));
            _planLayer.SelectionChanged += (s, e) => _state.PlanLayer = (Family)Math.Max(0, _planLayer.SelectedIndex);
            head.Children.Add(_planLayer);
            _showDims = new CheckBox { Content = "Cotas", IsChecked = _state.ShowDims, Margin = new Thickness(10, 2, 4, 2), VerticalAlignment = VerticalAlignment.Center };
            _showLabels = new CheckBox { Content = "Etiquetas", IsChecked = _state.ShowLabels, Margin = Pad, VerticalAlignment = VerticalAlignment.Center };
            _showCovers = new CheckBox { Content = "Recubrimientos", IsChecked = _state.ShowCovers, Margin = Pad, VerticalAlignment = VerticalAlignment.Center };
            _showDims.Checked += (s, e) => _state.ShowDims = true; _showDims.Unchecked += (s, e) => _state.ShowDims = false;
            _showLabels.Checked += (s, e) => _state.ShowLabels = true; _showLabels.Unchecked += (s, e) => _state.ShowLabels = false;
            _showCovers.Checked += (s, e) => _state.ShowCovers = true; _showCovers.Unchecked += (s, e) => _state.ShowCovers = false;
            head.Children.Add(_showDims); head.Children.Add(_showLabels); head.Children.Add(_showCovers);
            DockPanel.SetDock(head, Dock.Top);
            planPanel.Children.Add(head);
            _caption = new TextBlock { Foreground = RevitTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(_caption, Dock.Top);
            planPanel.Children.Add(_caption);

            var legend = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            foreach (Family f in Families.All)
            {
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new System.Windows.Shapes.Rectangle { Width = 12, Height = 12, Fill = PlanColors.Of(f), Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
                sp.Children.Add(new TextBlock { Text = Families.Code(f) + " " + Families.Name(f), VerticalAlignment = VerticalAlignment.Center });
                var b = new Border { Child = sp, Padding = new Thickness(4, 1, 4, 1), Margin = new Thickness(0, 0, 6, 2), CornerRadius = new CornerRadius(3), Cursor = Cursors.Hand, Background = Brushes.Transparent };
                b.ToolTip = "Clic: aislar " + Families.Code(f) + " en las tres vistas (otro clic: todas)";
                Family captured = f;
                b.MouseLeftButtonDown += (s, e) => { _state.Isolated = _state.Isolated == captured ? (Family?)null : captured; e.Handled = true; };
                _legend[f] = b;
                legend.Children.Add(b);
            }
            _status = new TextBlock { Foreground = RevitTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
            var bottom = new StackPanel();
            bottom.Children.Add(legend);
            bottom.Children.Add(_status);
            DockPanel.SetDock(bottom, Dock.Bottom);
            planPanel.Children.Add(bottom);

            _plan = new PlanPreview(_state) { MinHeight = 260 };
            planPanel.Children.Add(new Border { BorderBrush = RevitTheme.Border, BorderThickness = new Thickness(1), Child = _plan });
            planGroup.Content = planPanel;
            Grid.SetColumn(planGroup, 0);
            grid.Children.Add(planGroup);

            // --- secciones apiladas ---
            var secGrid = new Grid { Margin = new Thickness(6, 0, 0, 0) };
            secGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            secGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var gA = new GroupBox { Header = "SECCION A-A (a lo largo de u; rueda: zoom, arrastrar: mover, doble clic: encajar)", Padding = new Thickness(4) };
            _secA = new SectionPreview(_state) { MinHeight = 160 };
            gA.Content = new Border { BorderBrush = RevitTheme.Border, BorderThickness = new Thickness(1), Child = _secA };
            Grid.SetRow(gA, 0);
            secGrid.Children.Add(gA);
            var gB = new GroupBox { Header = "SECCION B-B (a lo largo de v)", Padding = new Thickness(4), Margin = new Thickness(0, 6, 0, 0) };
            _secB = new SectionPreview(_state) { MinHeight = 160 };
            gB.Content = new Border { BorderBrush = RevitTheme.Border, BorderThickness = new Thickness(1), Child = _secB };
            Grid.SetRow(gB, 1);
            secGrid.Children.Add(gB);
            Grid.SetColumn(secGrid, 1);
            grid.Children.Add(secGrid);
            return grid;
        }

        private UIElement BuildButtons()
        {
            var panel = new DockPanel();
            _message = new TextBlock { Foreground = RevitTheme.Error, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(buttons, Dock.Right);

            _analyzeButton = new Button { Content = "Analizar sin armar", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(4, 0, 4, 0) };
            _analyzeButton.ToolTip = "Informe de texto (copiable) con los solidos y caras leidos de cada bloque, sus cotas, los fondos de foso, las regiones del tope, " +
                                     "el motivo exacto de rechazo o el armado previsto con sus avisos, choques y separaciones. Se guarda tambien en " + Log.Path;
            _analyzeButton.Click += (s, e) => OnAnalyze();
            buttons.Children.Add(_analyzeButton);

            var save = new Button { Content = "Guardar como valores por defecto", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(4, 0, 4, 0) };
            save.ToolTip = "Guarda lo elegido en config.json (" + AppConfig.ConfigPath() + ") para las proximas veces.";
            save.Click += (s, e) =>
            {
                AppConfig c = ReadConfig(out string err);
                if (err != null) { _message.Foreground = RevitTheme.Error; _message.Text = err; return; }
                try { c.Save(); _message.Foreground = RevitTheme.Ok; _message.Text = "Guardado en " + AppConfig.ConfigPath(); }
                catch (Exception ex) { _message.Foreground = RevitTheme.Error; _message.Text = "No se pudo guardar: " + ex.Message; }
            };
            buttons.Children.Add(save);

            _buildButton = new Button { Content = "Armar", Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(4, 0, 4, 0), FontWeight = FontWeights.SemiBold, IsDefault = BuildAvailable };
            _buildButton.Click += (s, e) => OnBuild();
            ToolTipService.SetShowOnDisabled(_buildButton, true);
            buttons.Children.Add(_buildButton);

            var cancel = new Button { Content = "Cancelar", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(4, 0, 0, 0), IsCancel = true };
            buttons.Children.Add(cancel);

            panel.Children.Add(buttons);
            panel.Children.Add(_message);
            return panel;
        }

        // ------------------------------------------------------------------
        // Controles auxiliares
        // ------------------------------------------------------------------
        private static Grid FormGrid()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            return grid;
        }

        private void AddRow(Grid grid, int row, string label, FrameworkElement control, string tip)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var lb = new TextBlock { Text = label, Margin = Pad, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(lb, row); Grid.SetColumn(lb, 0);
            grid.Children.Add(lb);
            if (tip != null) { control.ToolTip = tip; lb.ToolTip = tip; }
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(control, row); Grid.SetColumn(control, 1);
            grid.Children.Add(control);
            Hook(control);
        }

        private readonly HashSet<FrameworkElement> _hooked = new HashSet<FrameworkElement>();

        private void Hook(FrameworkElement c)
        {
            if (!_hooked.Add(c)) return;
            if (c is TextBox tb) tb.TextChanged += (s, e) => Refresh();
            else if (c is ComboBox cb) cb.SelectionChanged += (s, e) => Refresh();
            else if (c is CheckBox ck) { ck.Checked += (s, e) => Refresh(); ck.Unchecked += (s, e) => Refresh(); }
        }

        private static string NumText(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

        private static TextBox NumBox(double v) => new TextBox { Text = NumText(v), Width = 62, HorizontalAlignment = HorizontalAlignment.Left, Margin = Pad };

        private TextBox Num(string key, double v)
        {
            TextBox tb = NumBox(v);
            _nums[key] = tb;
            Hook(tb);
            return tb;
        }

        private ComboBox TypeCombo(string key, string current, double width)
        {
            var cb = new ComboBox { Margin = Pad, Width = width };
            foreach (BarTypes.Info i in _barTypes) cb.Items.Add(i.Display);
            BarTypes.Info match = BarTypes.Find(_barTypes, current);
            cb.SelectedIndex = match == null ? -1 : _barTypes.IndexOf(match);
            cb.ToolTip = "Tipo de barra (RebarBarType) cargado en el proyecto. El diametro real sale del tipo.";
            _types[key] = cb;
            Hook(cb);
            return cb;
        }

        private string TypeOf(string key)
        {
            ComboBox cb = _types[key];
            return cb.SelectedIndex >= 0 && cb.SelectedIndex < _barTypes.Count ? _barTypes[cb.SelectedIndex].Name : "";
        }

        private ComboBox Choice(string key, string[] values, string[] labels, string current, double width)
        {
            var cb = new ComboBox { Margin = Pad, Width = width };
            foreach (string l in labels) cb.Items.Add(l);
            int idx = Array.FindIndex(values, v => string.Equals(v, current, StringComparison.OrdinalIgnoreCase));
            cb.SelectedIndex = Math.Max(0, idx);
            _choices[key] = cb;
            _choiceValues[key] = values;
            Hook(cb);
            return cb;
        }

        private string ChoiceOf(string key) => _choiceValues[key][Math.Max(0, _choices[key].SelectedIndex)];

        /// <summary>Fila "tipo | separacion | reparto" de una familia de cara.</summary>
        private FrameworkElement BarRow(string key, string type, double spacing, string layout)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(TypeCombo(key, type, 120));
            sp.Children.Add(new TextBlock { Text = "@", Margin = Pad, VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(Num(key + ":sp", spacing));
            sp.Children.Add(Choice(key + ":layout", LayoutValues, LayoutLabels, layout, 118));
            return sp;
        }

        /// <summary>Fila de una capa de malla: tipo, separacion y reparto.</summary>
        private FrameworkElement LayerRow(string key, LayerCfg lc) => BarRow(key, lc.BarTypeName, lc.SpacingMm, lc.LayoutMode);

        // ------------------------------------------------------------------
        // Lectura de la configuracion desde los controles
        // ------------------------------------------------------------------
        private AppConfig ReadConfig(out string error)
        {
            var errors = new List<string>();
            AppConfig c = _cfg.Clone();

            c.Direction.Mode = DirModes[Math.Max(0, _dir.SelectedIndex)];
            c.Direction.AngleDeg = ReadNum(_angle, "angulo", -360, errors);

            foreach (Family f in Families.All) c.SetEnabled(f, _on[f].IsChecked == true);

            ReadLayer(c.F1.U, "F1:u", errors); ReadLayer(c.F1.V, "F1:v", errors);
            c.F1.LegUpMm = NumOf("F1:leg", "F1 pata", 0, errors);
            c.F2.Extent = ChoiceOf("F2:extent");
            c.F2.AnchorageMm = NumOf("F2:anch", "F2 anclaje", 0, errors);
            c.F2.BelowRecessFloorMm = NumOf("F2:below", "F2 bajo el fondo", 0, errors);
            ReadLayer(c.F2.U, "F2:u", errors); ReadLayer(c.F2.V, "F2:v", errors);
            c.F2.LegDownMm = NumOf("F2:leg", "F2 pata", 0, errors);
            ReadLayer(c.F3.U, "F3:u", errors); ReadLayer(c.F3.V, "F3:v", errors);
            c.F3.LegDownMm = NumOf("F3:leg", "F3 pata", 0, errors);
            c.F4.BarTypeName = TypeOf("F4"); c.F4.SpacingMm = NumOf("F4:sp", "F4 separacion", 1, errors); c.F4.LayoutMode = ChoiceOf("F4:layout");
            c.F4.VerticalMm = NumOf("F4:vert", "F4 vertical", 0, errors); c.F4.FootMm = NumOf("F4:foot", "F4 pie", 0, errors);
            c.F5.BarTypeName = TypeOf("F5"); c.F5.SpacingMm = NumOf("F5:sp", "F5 separacion", 1, errors); c.F5.LayoutMode = ChoiceOf("F5:layout");
            c.F5.Shape = ChoiceOf("F5:shape"); c.F5.LapMm = NumOf("F5:lap", "F5 prolongacion", 0, errors);
            c.F6.BarTypeName = TypeOf("F6"); c.F6.SpacingMm = NumOf("F6:sp", "F6 separacion", 1, errors); c.F6.LayoutMode = ChoiceOf("F6:layout");
            c.F7.BarTypeName = TypeOf("F7"); c.F7.SpacingMm = NumOf("F7:sp", "F7 separacion", 1, errors); c.F7.LayoutMode = ChoiceOf("F7:layout");
            c.F7.LegMm = NumOf("F7:leg", "F7 patas", 0, errors); c.F7.Placement = ChoiceOf("F7:place");
            c.F8.BarTypeName = TypeOf("F8"); c.F8.SpacingMm = NumOf("F8:sp", "F8 separacion", 1, errors); c.F8.LayoutMode = ChoiceOf("F8:layout");
            c.F8.Layers = ChoiceOf("F8:layers") == "2" ? 2 : 1; c.F8.Shape = ChoiceOf("F8:shape"); c.F8.LapMm = NumOf("F8:lap", "F8 prolongacion", 0, errors);

            c.CoverBottomMm = ReadNum(_coverB, "recubrimiento inferior", 0, errors);
            c.CoverTopMm = ReadNum(_coverT, "recubrimiento superior", 0, errors);
            c.CoverEdgeMm = ReadNum(_coverE, "recubrimiento de borde", 0, errors);
            c.CoverWallMm = ReadNum(_coverW, "recubrimiento de foso y murete", 0, errors);
            c.WallMaxWidthMm = ReadNum(_wallMax, "ancho maximo de murete", 1, errors);
            c.LevelReference = new[] { "shared", "project", "internal" }[Math.Max(0, _levelRef.SelectedIndex)];
            c.PartitionTemplate = _partition.Text.Trim();
            c.Preview.PlanLayer = Families.Code(_state.PlanLayer);
            c.Preview.ShowDims = _state.ShowDims; c.Preview.ShowLabels = _state.ShowLabels; c.Preview.ShowCovers = _state.ShowCovers;
            c.Normalize();

            error = errors.Count == 0 ? null : string.Join(" | ", errors);
            return c;
        }

        private void ReadLayer(LayerCfg lc, string key, List<string> errors)
        {
            lc.BarTypeName = TypeOf(key);
            lc.SpacingMm = NumOf(key + ":sp", key.Replace(':', ' ') + " separacion", 1, errors);
            lc.LayoutMode = ChoiceOf(key + ":layout");
        }

        private double NumOf(string key, string label, double min, List<string> errors) => ReadNum(_nums[key], label, min, errors);

        private static bool TryNumber(string s, out double v)
        {
            s = (s ?? "").Trim().Replace(',', '.');
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        private static double ReadNum(TextBox tb, string label, double min, List<string> errors)
        {
            if (!TryNumber(tb.Text, out double v) || v < min)
            {
                errors.Add(label + ": numero no valido" + (min > 0 ? " (minimo " + NumText(min) + ")" : ""));
                tb.BorderBrush = RevitTheme.Error;
                return Math.Max(min, 0);
            }
            tb.ClearValue(Control.BorderBrushProperty);
            return v;
        }

        private void MarkTypes(AppConfig c)
        {
            var needed = new HashSet<string>();
            foreach ((Family f, string layer, string name) in c.BarTypesNeeded()) needed.Add(Families.Code(f) + (layer != "" ? ":" + layer : ""));
            foreach (var kv in _types)
            {
                bool bad = _strictTypes && needed.Contains(kv.Key) && kv.Value.SelectedIndex < 0;
                if (bad) { kv.Value.BorderBrush = RevitTheme.Error; kv.Value.BorderThickness = new Thickness(2); }
                else { kv.Value.ClearValue(Control.BorderBrushProperty); kv.Value.ClearValue(Control.BorderThicknessProperty); }
            }
        }

        // ------------------------------------------------------------------
        // Actualizacion
        // ------------------------------------------------------------------
        private void Refresh()
        {
            if (_building) return;
            _refreshing = true;
            try
            {
                AppConfig scratch = ReadConfig(out string error);
                PlanDiameters d = BarTypes.Diameters(_barTypes, scratch, out List<string> missing);
                MarkTypes(scratch);

                _angle.IsEnabled = scratch.Direction.Mode == "angle";
                foreach (Family f in Families.All) _panels[f].IsEnabled = scratch.Enabled(f);

                int ok = 0;
                foreach (HostAnalysis item in _items)
                {
                    bool good = ItemStatus(item, scratch, d, out string text, out _);
                    if (good) ok++;
                    if (_itemRuns.TryGetValue(item, out var runs))
                    {
                        runs.kind.Text = (good ? "Bloque" : (item.Outline == null ? "SIN ARMAR" : "RECHAZADO")) + ": ";
                        runs.kind.Foreground = good ? RevitTheme.Ok : RevitTheme.Error;
                        runs.detail.Text = text;
                    }
                }
                _buildButton.Content = "Armar " + ok + " elemento(s)";
                _buildButton.IsEnabled = BuildAvailable && ok > 0 && error == null;
                _buildButton.ToolTip = BuildAvailable
                    ? "Crea las barras en Revit (cada bloque en su subtransaccion: o se arma entero y bien, o no se arma)."
                    : "Entrega 2a (solo lectura): la creacion de barras en Revit llega en la entrega 2b. Usa \"Analizar sin armar\" y la lamina.";
                _analyzeButton.IsEnabled = _items.Count > 0;

                bool newElement = false;
                if (_selected != null && _selected.Outline != null)
                {
                    ItemStatus(_selected, scratch, d, out string text, out BlockPlan plan);
                    BlockFrame frame = _selected.Frame(scratch);
                    newElement = !ReferenceEquals(frame, _lastFrame);
                    _lastFrame = frame; _lastPlan = plan; _lastCfg = scratch;
                    if (newElement && frame != null)
                    {
                        BlockTopology t = frame.Topology;
                        _state.SetCuts(0.5 * (t.VMin + t.VMax), 0.5 * (t.UMin + t.UMax));
                        _lastCutA = double.NaN; _lastCutB = double.NaN;
                    }
                    _caption.Text = _selected.Tag + (frame != null ? frame.Describe() + ", " : "") + _selected.Outline.Describe() +
                                    (missing.Count > 0 ? "  (sin tipo de barra: " + string.Join(", ", missing) + ")" : "") +
                                    (plan == null ? "  -> " + text : "");
                    _partitionPreview.Text = "Ejemplo: " + _selected.Partition(scratch, "F1 u", Families.Code(Family.F1));
                }
                else
                {
                    _lastFrame = null; _lastPlan = null; _lastCfg = scratch;
                    _caption.Text = _selected != null ? _selected.Tag + _selected.Detail : "";
                    _partitionPreview.Text = "";
                }
                ShowViews(newElement);

                if (error != null) { _message.Foreground = RevitTheme.Error; _message.Text = error; }
                else if (ReferenceEquals(_message.Foreground, RevitTheme.Error)) _message.Text = "";
            }
            catch (Exception ex)
            {
                _message.Foreground = RevitTheme.Error;
                _message.Text = "ERROR al actualizar la lamina: " + ex.Message;
                Log.Error("RebarOptionsWindow.Refresh", ex);
            }
            finally { _refreshing = false; }
        }

        /// <summary>Recalcula las dos secciones en los cortes actuales y redibuja las tres vistas.</summary>
        private void ShowViews(bool newElement)
        {
            if (_lastFrame == null)
            {
                _plan.Clear(_selected == null ? "Sin elemento" : "Sin geometria legible: ver \"Analizar sin armar\"");
                _secA.Clear(""); _secB.Clear("");
                _cutA = _cutB = null;
                return;
            }
            _plan.Show(_lastFrame, _lastPlan);
            UpdateCuts(true);
            string refName = AppConfig.LevelReferenceName(_lastCfg.LevelReference);
            _secA.Show(_cutA, _lastPlan, refName, newElement);
            _secB.Show(_cutB, _lastPlan, refName, newElement);
        }

        private bool UpdateCuts(bool force)
        {
            if (_lastFrame == null) return false;
            bool changedA = force || Math.Abs(_state.CutA - _lastCutA) > 1e-12 || double.IsNaN(_lastCutA);
            bool changedB = force || Math.Abs(_state.CutB - _lastCutB) > 1e-12 || double.IsNaN(_lastCutB);
            if (!changedA && !changedB) return false;
            double tol = BlockPlan.Mm(_lastCfg.ToleranceMm);
            double zBase = _selected.Outline.ZBottom + SafeOffset(_lastCfg.LevelReference);
            BlockFrame frame = _lastFrame;
            if (changedA)
            {
                var line = new SectionLine(true, _state.CutA);
                _cutA = BlockSection.Cut(_lastPlan, frame.Topology, line, tol, zBase, s => frame.SampledTop(line, s));
                _lastCutA = _state.CutA;
            }
            if (changedB)
            {
                var line = new SectionLine(false, _state.CutB);
                _cutB = BlockSection.Cut(_lastPlan, frame.Topology, line, tol, zBase, s => frame.SampledTop(line, s));
                _lastCutB = _state.CutB;
            }
            return true;
        }

        private double SafeOffset(string reference)
        {
            try { return _elevationOffset(reference); } catch { return 0; }
        }

        private void OnStateChanged()
        {
            if (_refreshing || _building) return;
            try
            {
                if (UpdateCuts(false) && _lastFrame != null)
                {
                    string refName = AppConfig.LevelReferenceName(_lastCfg.LevelReference);
                    _secA.Show(_cutA, _lastPlan, refName, false);
                    _secB.Show(_cutB, _lastPlan, refName, false);
                }
                else { _secA.Refresh(); _secB.Refresh(); }
                _plan.Refresh();
                foreach (var kv in _legend) kv.Value.Background = _state.Isolated == kv.Key ? SelectedBrush : Brushes.Transparent;
                _status.Text = _state.Hover != null ? PlanPreview.HoverText(_state.Hover, _lastPlan)
                    : (_state.Isolated != null ? "Aislada " + Families.Code(_state.Isolated.Value) + " (clic de nuevo en la leyenda para ver todas)" : "");
            }
            catch (Exception ex)
            {
                _status.Text = "ERROR: " + ex.Message;
                Log.Error("RebarOptionsWindow.OnStateChanged", ex);
            }
        }

        /// <summary>Estado de un bloque con la configuracion dada: true si se puede armar, y el texto para su fila.</summary>
        private static bool ItemStatus(HostAnalysis item, AppConfig cfg, PlanDiameters d, out string text, out BlockPlan plan)
        {
            plan = null;
            if (item.Outline == null) { text = item.Error ?? "sin geometria"; return false; }
            try
            {
                BlockFrame frame = item.Frame(cfg);
                BlockTopology topo = frame.Topology;
                if (topo.Error != null) { text = frame.Describe() + " -> RECHAZADO: " + topo.Error; return false; }
                plan = BlockPlan.Build(topo, cfg, d);
                string head = frame.Describe() + ", " + topo.Describe();
                if (plan.Error != null) { text = head + " -> SIN ARMAR: " + plan.Error; return false; }
                text = head + "; " + plan.Describe() + (plan.Warnings.Count > 0 ? " (" + string.Join("; ", plan.Warnings) + ")" : "");
                return plan.Bars.Count > 0;
            }
            catch (Exception ex)
            {
                text = item.Outline.Describe() + " -> ERROR: " + ex.Message;
                Log.Error("ItemStatus " + item.Tag, ex);
                return false;
            }
        }

        // ------------------------------------------------------------------
        // Analizar sin armar
        // ------------------------------------------------------------------
        private void OnAnalyze()
        {
            AppConfig scratch = ReadConfig(out string error);
            PlanDiameters d = BarTypes.Diameters(_barTypes, scratch, out List<string> missing);
            var sb = new StringBuilder();
            sb.AppendLine("ANALIZAR SIN ARMAR - Bloques con foso - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("No se ha creado ni modificado nada en el modelo.");
            sb.AppendLine("configuracion: direccion " + scratch.Direction.Mode + (scratch.Direction.Mode == "angle" ? " " + NumText(scratch.Direction.AngleDeg) + " grados" : "") +
                          "; recubrimientos inf " + NumText(scratch.CoverBottomMm) + ", sup " + NumText(scratch.CoverTopMm) + ", borde " + NumText(scratch.CoverEdgeMm) +
                          ", foso/murete " + NumText(scratch.CoverWallMm) + " mm; murete hasta " + NumText(scratch.WallMaxWidthMm) + " mm; tolerancia " + NumText(scratch.ToleranceMm) + " mm");
            sb.AppendLine("familias activas: " + string.Join(", ", Families.All.Where(scratch.Enabled).Select(Families.Code)) +
                          (missing.Count > 0 ? "; SIN TIPO DE BARRA: " + string.Join(", ", missing) : ""));
            sb.AppendLine("tipos de barra del proyecto: " + (_barTypes.Count == 0 ? "NINGUNO (carga una familia de armadura)" : string.Join(", ", _barTypes.Select(b => b.Display))));
            sb.AppendLine("niveles en: " + AppConfig.LevelReferenceName(scratch.LevelReference) + " (desfase " + NumText(SafeOffset(scratch.LevelReference) * 304.8) + " mm sobre la cota interna)");
            if (error != null) sb.AppendLine("AVISO: valores no validos en la ventana: " + error);
            sb.AppendLine("log: " + Log.Path);
            sb.AppendLine();
            double tol = BlockPlan.Mm(scratch.ToleranceMm);
            foreach (HostAnalysis item in _items)
            {
                BlockPlan plan = null;
                try { ItemStatus(item, scratch, d, out _, out plan); } catch { }
                try { sb.AppendLine(item.Report(scratch, SafeOffset(scratch.LevelReference), plan)); }
                catch (Exception ex) { sb.AppendLine(item.Tag + "ERROR en el informe: " + ex); }
                if (plan != null && plan.Error == null && item.Outline != null)
                {
                    try
                    {
                        BlockFrame frame = item.Frame(scratch);
                        BlockTopology t = frame.Topology;
                        bool sel = ReferenceEquals(item, _selected);
                        double zBase = item.Outline.ZBottom + SafeOffset(scratch.LevelReference);
                        foreach (SectionLine line in new[]
                        {
                            new SectionLine(true, sel ? _state.CutA : 0.5 * (t.VMin + t.VMax)),
                            new SectionLine(false, sel ? _state.CutB : 0.5 * (t.UMin + t.UMax))
                        })
                        {
                            SectionCut cut = BlockSection.Cut(plan, t, line, tol, zBase, s => frame.SampledTop(line, s));
                            sb.AppendLine("lamina: " + cut.Describe() + "; niveles " + string.Join(", ", cut.Levels.Select(l => l.Name + " " + (l.Elevation * 0.3048).ToString("0.000", CultureInfo.InvariantCulture))));
                            foreach (string w in cut.Warnings) sb.AppendLine("  aviso: " + w);
                        }
                    }
                    catch (Exception ex) { sb.AppendLine("  ERROR en las secciones: " + ex.Message); }
                }
                sb.AppendLine();
            }
            string text = sb.ToString();
            Log.Block("Analizar sin armar", text);
            var win = new ReportWindow("Analizar sin armar", text) { Owner = this };
            win.ShowDialog();
        }

        private void OnBuild()
        {
            AppConfig c = ReadConfig(out string error);
            if (error != null) { _message.Foreground = RevitTheme.Error; _message.Text = error; return; }
            BarTypes.Diameters(_barTypes, c, out List<string> missing);
            if (missing.Count > 0)
            {
                _strictTypes = true;
                MarkTypes(c);
                _message.Foreground = RevitTheme.Error;
                _message.Text = "Elige el tipo de barra de: " + string.Join(", ", missing) + ".";
                return;
            }
            Result = c;
            DialogResult = true;
            Close();
        }
    }

    /// <summary>Informe de texto copiable (modo "Analizar sin armar").</summary>
    public sealed class ReportWindow : Window
    {
        public ReportWindow(string title, string text)
        {
            Title = title;
            Width = 1000; Height = 700; MinWidth = 600; MinHeight = 400;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            FontSize = 12;
            RevitTheme.Apply(this);

            var root = new Grid { Margin = new Thickness(10) };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var box = new TextBox
            {
                Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
                FontFamily = new FontFamily("Consolas"), FontSize = 11,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            Grid.SetRow(box, 0);
            root.Children.Add(box);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var copy = new Button { Content = "Copiar al portapapeles", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(4, 0, 4, 0) };
            copy.Click += (s, e) => { try { Clipboard.SetText(text); copy.Content = "Copiado"; } catch (Exception ex) { copy.Content = "No se pudo copiar: " + ex.Message; } };
            buttons.Children.Add(copy);
            var open = new Button { Content = "Abrir carpeta del log", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(4, 0, 4, 0), ToolTip = Log.Path };
            open.Click += (s, e) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + Log.Path + "\"") { UseShellExecute = true }); }
                catch (Exception ex) { open.Content = "No se pudo abrir: " + ex.Message; }
            };
            buttons.Children.Add(open);
            var close = new Button { Content = "Cerrar", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(4, 0, 0, 0), IsCancel = true, IsDefault = true };
            buttons.Children.Add(close);
            Grid.SetRow(buttons, 1);
            root.Children.Add(buttons);
            Content = root;
        }
    }
}
