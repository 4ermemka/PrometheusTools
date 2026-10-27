using UnityEngine;
using UnityEngine.UI;

namespace Shared.UI
{
    /// <summary>Независимые корневые окна: рядом на широком родителе, друг под другом на узком.</summary>
    [AddComponentMenu("Layout/Responsive Columns")]
    public sealed class ResponsiveColumns : LayoutGroup
    {
        [SerializeField, Min(100)] private float stackBelowWidth = 850;
        [SerializeField, Min(0)] private float gap = 16;
        public bool IsStacked => rectTransform.rect.width < stackBelowWidth;
        public override void CalculateLayoutInputHorizontal() { base.CalculateLayoutInputHorizontal(); SetLayoutInputForAxis(0, -1, 0, 1, 0); }
        public override void CalculateLayoutInputVertical() => SetLayoutInputForAxis(0, -1, 0, 1, 1);
        public override void SetLayoutHorizontal() => LayoutAxis(0);
        public override void SetLayoutVertical() => LayoutAxis(1);
        private void LayoutAxis(int axis)
        {
            int count = rectChildren.Count;
            if (count == 0) return;
            bool divided = (axis == 1) == IsStacked;
            float area = rectTransform.rect.size[axis] - (axis == 0 ? padding.horizontal : padding.vertical);
            float size = Mathf.Max(1, divided ? (area - gap * (count - 1)) / count : area);
            for (int i = 0; i < count; i++)
                SetChildAlongAxis(rectChildren[i], axis, (axis == 0 ? padding.left : padding.top) + (divided ? i * (size + gap) : 0), size);
        }
    }
}
