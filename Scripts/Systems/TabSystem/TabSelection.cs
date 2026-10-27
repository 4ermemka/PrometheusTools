using System;
using System.Collections.Generic;

namespace Shared.UI
{
    /// <summary>Один выбранный элемент среди соседей. Не зависит от Unity, инвентарей и способа отображения.</summary>
    public sealed class TabSelection<T> where T : class
    {
        private readonly List<T> items = new();
        public IReadOnlyList<T> Items => items;
        public T Selected { get; private set; }
        public event Action<T> Changed;

        public void SetItems(IEnumerable<T> values)
        {
            var next = new List<T>(values);
            var unique = new HashSet<T>();
            foreach (var item in next)
                if (item == null || !unique.Add(item)) throw new ArgumentException("Вкладки должны быть непустыми и уникальными.");
            int previousIndex = Math.Max(0, items.IndexOf(Selected));
            items.Clear(); items.AddRange(next);
            if (Selected != null && items.Contains(Selected)) return;
            SetSelected(items.Count == 0 ? null : items[Math.Min(previousIndex, items.Count - 1)]);
        }

        public bool Select(T item)
        {
            if (item == null || !items.Contains(item)) return false;
            SetSelected(item);
            return true;
        }

        private void SetSelected(T item)
        {
            if (EqualityComparer<T>.Default.Equals(Selected, item)) return;
            Selected = item;
            Changed?.Invoke(item);
        }
    }
}
