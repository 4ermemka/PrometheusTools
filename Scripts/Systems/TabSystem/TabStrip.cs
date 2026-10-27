using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Shared.UI
{
    /// <summary>Прокручиваемая полоса закладок. Все размеры измеряются в координатах родительского Canvas.</summary>
    public sealed class TabStrip : MonoBehaviour
    {
        [SerializeField] private ScrollRect scroll;
        [SerializeField] private TabButton buttonPrefab;
        [SerializeField, Min(80)] private float preferredWidth = 190;
        [SerializeField, Min(24)] private float rowHeight = 48;
        [SerializeField, Min(0)] private float spacing = 4;
        private readonly List<TabButton> buttons = new();
        private bool vertical;
        private bool dirty;
        private Vector2 lastSize;
        public IReadOnlyList<TabButton> Buttons => buttons;
        public ScrollRect Scroll => scroll;

        public void Rebuild(IReadOnlyList<TabPanel> pages, TabPanel selected, bool isVertical)
        {
            vertical = isVertical;
            while (buttons.Count > pages.Count)
            {
                var last = buttons[buttons.Count - 1]; buttons.RemoveAt(buttons.Count - 1);
                last.gameObject.SetActive(false); Destroy(last.gameObject);
            }
            while (buttons.Count < pages.Count) buttons.Add(Instantiate(buttonPrefab, scroll.content, false));
            for (int i = 0; i < pages.Count; i++) buttons[i].Bind(pages[i], pages[i] == selected);
            scroll.horizontal = !vertical; scroll.vertical = vertical;
            dirty = true;
            Layout();
            Reveal(selected);
        }
        private void OnRectTransformDimensionsChange() => dirty = true;
        private void LateUpdate()
        {
            if (scroll != null && (dirty || lastSize != scroll.viewport.rect.size)) Layout();
        }
        public void Layout()
        {
            if (scroll == null) return;
            dirty = false;
            lastSize = scroll.viewport.rect.size;
            var content = scroll.content;
            content.anchorMin = content.anchorMax = content.pivot = new Vector2(0, 1);
            float width = vertical ? lastSize.x : Mathf.Min(preferredWidth, Mathf.Max(140, (lastSize.x - spacing * (buttons.Count - 1)) / Mathf.Max(1, buttons.Count)));
            float height = vertical ? rowHeight : lastSize.y;
            content.sizeDelta = vertical ? new Vector2(lastSize.x, Mathf.Max(lastSize.y, buttons.Count * (height + spacing) - spacing))
                : new Vector2(Mathf.Max(lastSize.x, buttons.Count * (width + spacing) - spacing), lastSize.y);
            for (int i = 0; i < buttons.Count; i++)
            {
                var rect = (RectTransform)buttons[i].transform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = vertical ? new Vector2(0, -i * (height + spacing)) : new Vector2(i * (width + spacing), 0);
                rect.sizeDelta = new Vector2(width, height);
            }
        }
        private void Reveal(TabPanel selected)
        {
            int index = buttons.FindIndex(button => button.Page == selected);
            if (index < 0) return;
            var rect = (RectTransform)buttons[index].transform;
            var position = scroll.content.anchoredPosition;
            if (vertical)
            {
                float top = -rect.anchoredPosition.y, bottom = top + rect.rect.height;
                position.y = Mathf.Clamp(position.y, bottom - lastSize.y, top);
                position.y = Mathf.Clamp(position.y, 0, Mathf.Max(0, scroll.content.rect.height - lastSize.y));
            }
            else
            {
                float left = rect.anchoredPosition.x, right = left + rect.rect.width;
                position.x = Mathf.Clamp(position.x, -left, lastSize.x - right);
                position.x = Mathf.Clamp(position.x, Mathf.Min(0, lastSize.x - scroll.content.rect.width), 0);
            }
            scroll.content.anchoredPosition = position;
        }
    }
}
