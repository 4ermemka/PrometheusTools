using UnityEngine;
using UnityEngine.UI;

namespace Shared.UI
{
    /// <summary>Сохраняет ручной рисунок дочернего UI: уменьшает его до ширины родителя и сообщает layout реальную высоту.</summary>
    public sealed class DesignSizeLayout : LayoutGroup
    {
        [SerializeField] private Vector2 designSize = new(300, 282);
        private float Scale => Mathf.Min(1, Mathf.Max(1, rectTransform.rect.width - padding.horizontal) / Mathf.Max(1, designSize.x));
        public override void CalculateLayoutInputHorizontal() { base.CalculateLayoutInputHorizontal(); SetLayoutInputForAxis(0, -1, designSize.x + padding.horizontal, -1, 0); }
        public override void CalculateLayoutInputVertical() => SetLayoutInputForAxis(designSize.y * Scale + padding.vertical, -1, designSize.y * Scale + padding.vertical, -1, 1);
        public override void SetLayoutHorizontal() => Arrange();
        public override void SetLayoutVertical() => Arrange();
        private void Arrange()
        {
            foreach (var child in rectChildren)
            {
                child.anchorMin = child.anchorMax = child.pivot = new Vector2(.5f, 1);
                child.sizeDelta = designSize;
                child.anchoredPosition = new Vector2(0, -padding.top);
                child.localScale = Vector3.one * Scale;
            }
        }
    }
}
