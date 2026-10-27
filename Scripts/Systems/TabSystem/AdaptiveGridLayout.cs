using UnityEngine;
using UnityEngine.UI;

namespace Shared.UI
{
    /// <summary>Количество столбцов определяется доступной шириной. Высота content следует за количеством ячеек.</summary>
    [AddComponentMenu("Layout/Adaptive Grid Layout")]
    public sealed class AdaptiveGridLayout : LayoutGroup
    {
        [SerializeField, Min(32)] private float preferredCellSize = 104;
        [SerializeField, Min(0)] private float spacing = 10;
        [SerializeField, Min(1)] private int maxColumns = 12;
        public int Columns { get; private set; } = 1;
        public float CellSize { get; private set; }
        public override void CalculateLayoutInputHorizontal()
        {
            base.CalculateLayoutInputHorizontal();
            Measure();
            SetLayoutInputForAxis(padding.horizontal + preferredCellSize, -1, padding.horizontal + preferredCellSize, -1, 0);
        }
        public override void CalculateLayoutInputVertical()
        {
            Measure();
            int rows = Mathf.CeilToInt(rectChildren.Count / (float)Columns);
            float height = padding.vertical + rows * CellSize + Mathf.Max(0, rows - 1) * spacing;
            SetLayoutInputForAxis(height, -1, height, -1, 1);
        }
        private void Measure()
        {
            float width = Mathf.Max(1, rectTransform.rect.width - padding.horizontal);
            Columns = Mathf.Clamp(Mathf.FloorToInt((width + spacing) / (preferredCellSize + spacing)), 1, maxColumns);
            CellSize = Mathf.Max(1, (width - (Columns - 1) * spacing) / Columns);
        }
        public override void SetLayoutHorizontal()
        {
            Measure();
            for (int i = 0; i < rectChildren.Count; i++) SetChildAlongAxis(rectChildren[i], 0, padding.left + i % Columns * (CellSize + spacing), CellSize);
        }
        public override void SetLayoutVertical()
        {
            for (int i = 0; i < rectChildren.Count; i++) SetChildAlongAxis(rectChildren[i], 1, padding.top + i / Columns * (CellSize + spacing), CellSize);
        }
    }
}
