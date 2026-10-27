using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace Shared.UI
{
    public enum TabSide { Top, Bottom, Left, Right }

    /// <summary>
    /// Фасад панели: иерархия Transform задаёт дерево, ближайший TabPanel — родитель.
    /// Корень сохраняет обычные anchors/размеры. Соседние страницы заполняют Body родителя.
    /// Переключение страниц меняет CanvasGroup, а не активность моделей и незавершённого drag.
    /// </summary>
    [DisallowMultipleComponent, RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
    public sealed class TabPanel : MonoBehaviour
    {
        [SerializeField] private string caption = "Вкладка";
        [SerializeField] private Sprite icon;
        [SerializeField] private TabSide side;
        [SerializeField] private RectTransform body;
        [SerializeField] private TabStrip strip;
        [SerializeField, Min(24)] private float stripHeight = 48;
        [SerializeField, Min(80)] private float stripWidth = 180;
        [SerializeField] private bool available = true;
        [SerializeField] private TabPanel initialPage;
        [SerializeField] private UnityEvent onShown = new();
        [SerializeField] private UnityEvent onHidden = new();
        private readonly TabSelection<TabPanel> selection = new();
        private readonly List<TabPanel> children = new();
        private bool initialized, dirty = true, rebuilding, visible;
        private CanvasGroup group;
        public string Caption { get => caption; set { caption = value; InvalidateHierarchy(); } }
        public Sprite Icon { get => icon; set { icon = value; InvalidateHierarchy(); } }
        public TabSide Side { get => side; set { side = value; InvalidateHierarchy(); } }
        public RectTransform Body => body;
        public TabPanel Parent { get; private set; }
        public IReadOnlyList<TabPanel> Children => children;
        public TabPanel SelectedPage => selection.Selected;
        public bool IsVisible => visible && isActiveAndEnabled;
        public bool Available => available;
        public event Action<TabPanel> SelectionChanged;
        public event Action<bool> VisibilityChanged;

        private void Awake()
        {
            group = GetComponent<CanvasGroup>();
            selection.Changed += page => { if (!rebuilding) ApplyTreeVisibility(); SelectionChanged?.Invoke(page); };
        }
        private void OnEnable() { InvalidateHierarchy(); RefreshHierarchy(); }
        private void OnDisable()
        {
            SetVisible(false);
            Parent?.InvalidateHierarchy();
        }
        private void OnTransformParentChanged()
        {
            Parent?.InvalidateHierarchy();
            InvalidateHierarchy();
        }
        private void OnTransformChildrenChanged() => InvalidateHierarchy();
        private void OnRectTransformDimensionsChange() { if (Application.isPlaying) dirty = true; }
        // Unity может вызвать OnValidate при десериализации. Обход Transform выполняется в LateUpdate.
        private void OnValidate() => dirty = true;
        private void LateUpdate() { if (dirty) RefreshHierarchy(); }

        public void InvalidateHierarchy()
        {
            dirty = true;
            var parent = FindParent();
            if (parent != null) parent.InvalidateHierarchy();
        }
        private TabPanel FindParent() => transform.parent != null ? transform.parent.GetComponentInParent<TabPanel>(true) : null;

        /// <summary>Вызовите после пакетной перестройки вложенных wrapper-объектов; AttachTo делает это сам.</summary>
        public void RefreshHierarchy()
        {
            if (!Application.isPlaying || rebuilding) return;
            var parent = FindParent();
            if (parent != null) { parent.RefreshHierarchy(); return; }
            RebuildBranch(null);
            ApplyTreeVisibility();
        }
        private void RebuildBranch(TabPanel parent)
        {
            rebuilding = true; dirty = false; Parent = parent;
            children.Clear();
            foreach (var page in GetComponentsInChildren<TabPanel>(true))
                if (page != this && page.FindParent() == this) children.Add(page);
            var selectable = new List<TabPanel>();
            foreach (var child in children)
            {
                child.RebuildBranch(this);
                if (child.IsAvailableUnder(this)) selectable.Add(child);
            }
            selection.SetItems(selectable);
            if (!initialized && initialPage != null) selection.Select(initialPage);
            initialized = true;
            LayoutSurface(selectable.Count > 0);
            foreach (var child in children)
            {
                var rect = (RectTransform)child.transform;
                rect.localScale = Vector3.one;
                if (child.transform.parent == transform && body != null)
                {
                    rect.anchorMin = body.anchorMin; rect.anchorMax = body.anchorMax;
                    rect.offsetMin = body.offsetMin; rect.offsetMax = body.offsetMax;
                }
                else Stretch(rect);
            }
            if (strip != null) strip.Rebuild(selection.Items, selection.Selected, side == TabSide.Left || side == TabSide.Right);
            rebuilding = false;
        }
        private bool IsAvailableUnder(TabPanel parent)
        {
            if (!available || !enabled) return false;
            for (Transform current = transform; current != null && current != parent.transform; current = current.parent)
                if (!current.gameObject.activeSelf) return false;
            return true;
        }
        private void LayoutSurface(bool hasTabs)
        {
            if (body == null || strip == null) return;
            Stretch(body);
            strip.gameObject.SetActive(hasTabs);
            if (!hasTabs) return;
            var rect = (RectTransform)strip.transform;
            Stretch(rect);
            var size = ((RectTransform)transform).rect.size;
            float h = Mathf.Min(stripHeight, size.y * .4f), w = Mathf.Min(stripWidth, size.x * .4f);
            switch (side)
            {
                case TabSide.Top:
                    body.offsetMax = new Vector2(0, -h - 4); rect.anchorMin = new Vector2(0, 1); rect.offsetMin = new Vector2(0, -h); break;
                case TabSide.Bottom:
                    body.offsetMin = new Vector2(0, h + 4); rect.anchorMax = new Vector2(1, 0); rect.offsetMax = new Vector2(0, h); break;
                case TabSide.Left:
                    body.offsetMin = new Vector2(w + 4, 0); rect.anchorMax = new Vector2(0, 1); rect.offsetMax = new Vector2(w, 0); break;
                case TabSide.Right:
                    body.offsetMax = new Vector2(-w - 4, 0); rect.anchorMin = new Vector2(1, 0); rect.offsetMin = new Vector2(-w, 0); break;
            }
        }
        private void ApplyTreeVisibility()
        {
            SetVisible(available && isActiveAndEnabled && (Parent == null || Parent.IsVisible && Parent.SelectedPage == this));
            foreach (var child in children) child.ApplyTreeVisibility();
        }
        private void SetVisible(bool value)
        {
            if (group == null) group = GetComponent<CanvasGroup>();
            group.alpha = value ? 1 : 0; group.interactable = value; group.blocksRaycasts = value;
            if (visible == value) return;
            visible = value;
            // Скрытое поле ввода не должно удерживать клавиатурный фокус.
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (!value && selected != null && selected.transform.IsChildOf(transform)) EventSystem.current.SetSelectedGameObject(null);
            if (value) onShown.Invoke(); else onHidden.Invoke();
            VisibilityChanged?.Invoke(value);
        }

        /// <summary>UnityEvent-compatible API. Для дочерней страницы открывает всю цепочку предков.</summary>
        public void Open()
        {
            available = true;
            var parent = FindParent();
            if (parent != null) parent.Open();
            gameObject.SetActive(true);
            RefreshHierarchy();
            if (parent != null) { parent.selection.Select(this); parent.RefreshHierarchy(); }
        }
        public void Close()
        {
            if (FindParent() == null) gameObject.SetActive(false);
            else { available = false; RefreshHierarchy(); }
        }
        public void Toggle() { if (IsVisible) Close(); else Open(); }
        public void SetOpen(bool value) { if (value) Open(); else Close(); }
        public void SelectChild(TabPanel child) { RefreshHierarchy(); selection.Select(child); RefreshHierarchy(); }
        public void SetAvailable(bool value) { available = value; RefreshHierarchy(); }

        public void AttachTo(TabPanel parent)
        {
            if (parent == this || parent != null && parent.transform.IsChildOf(transform)) throw new ArgumentException("Цикл дерева вкладок.");
            var oldParent = FindParent();
            transform.SetParent(parent != null ? parent.Body : null, false);
            oldParent?.RefreshHierarchy();
            RefreshHierarchy();
        }
        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
