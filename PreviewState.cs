using System;

namespace BlockRebar
{
    /// <summary>
    /// Estado compartido de las tres vistas de la lamina (planta, seccion A-A y seccion B-B):
    /// posicion de los dos cortes, barra resaltada, familia aislada, capa de barras que se
    /// muestra en planta e interruptores. Cada cambio avisa a las vistas para que redibujen.
    /// </summary>
    public sealed class PreviewState
    {
        public event Action Changed;

        private double _cutA, _cutB;
        private PlannedBar _hover;
        private Family? _isolated;
        private Family _planLayer = Family.F1;
        private bool _showDims = true, _showLabels = true, _showCovers, _showGrids = true, _showAngles = true;

        /// <summary>Corte A-A: plano u-z a v = CutA (pies, coordenadas locales).</summary>
        public double CutA { get => _cutA; set { if (Math.Abs(_cutA - value) > 1e-9) { _cutA = value; Raise(); } } }
        /// <summary>Corte B-B: plano v-z a u = CutB.</summary>
        public double CutB { get => _cutB; set { if (Math.Abs(_cutB - value) > 1e-9) { _cutB = value; Raise(); } } }
        /// <summary>Barra bajo el raton en cualquiera de las vistas (se resalta su conjunto en las tres).</summary>
        public PlannedBar Hover { get => _hover; set { if (!ReferenceEquals(_hover, value)) { _hover = value; Raise(); } } }
        /// <summary>Familia aislada desde la leyenda (null = todas).</summary>
        public Family? Isolated { get => _isolated; set { if (_isolated != value) { _isolated = value; Raise(); } } }
        /// <summary>Capa de malla que se dibuja en la planta: F1, F2 o F3.</summary>
        public Family PlanLayer { get => _planLayer; set { if (_planLayer != value) { _planLayer = value; Raise(); } } }
        public bool ShowDims { get => _showDims; set { if (_showDims != value) { _showDims = value; Raise(); } } }
        public bool ShowLabels { get => _showLabels; set { if (_showLabels != value) { _showLabels = value; Raise(); } } }
        public bool ShowCovers { get => _showCovers; set { if (_showCovers != value) { _showCovers = value; Raise(); } } }
        public bool ShowGrids { get => _showGrids; set { if (_showGrids != value) { _showGrids = value; Raise(); } } }
        public bool ShowAngles { get => _showAngles; set { if (_showAngles != value) { _showAngles = value; Raise(); } } }

        /// <summary>Cortes al centro del bloque (al cambiar de elemento).</summary>
        public void SetCuts(double cutA, double cutB)
        {
            _cutA = cutA; _cutB = cutB;
            Raise();
        }

        /// <summary>True si la barra pertenece al conjunto resaltado.</summary>
        public bool IsHighlighted(PlannedBar bar, BlockPlan plan)
        {
            if (_hover == null || bar == null) return false;
            if (ReferenceEquals(_hover, bar)) return true;
            BarGroup g = plan?.GroupOf(_hover);
            return g != null && g.Bars.Contains(bar);
        }

        public bool Visible(Family f) => _isolated == null || _isolated == f;

        private void Raise() => Changed?.Invoke();
    }
}
