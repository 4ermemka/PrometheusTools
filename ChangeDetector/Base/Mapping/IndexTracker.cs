using System;
using System.Collections;
using System.Collections.Generic;

namespace Assets.Shared.ChangeDetector.Collections
{
    /// <summary>
    /// Быстрый трекер индексов для коллекций с поддержкой дубликатов
    /// </summary>
    internal class IndexTracker<T>
    {
        private readonly Dictionary<T, List<int>> _indicesByItem = new();
        private readonly IEqualityComparer<T> _comparer;

        public IndexTracker(IEqualityComparer<T>? comparer = null)
        {
            _comparer = comparer ?? EqualityComparer<T>.Default;
        }

        public void Add(T item, int index)
        {
            if (!_indicesByItem.TryGetValue(item, out var indices))
            {
                indices = new List<int>();
                _indicesByItem[item] = indices;
            }

            // Вставляем индекс с сохранением порядка
            var insertPos = indices.BinarySearch(index);
            if (insertPos < 0) insertPos = ~insertPos;
            indices.Insert(insertPos, index);
        }

        public void Remove(T item, int index)
        {
            if (_indicesByItem.TryGetValue(item, out var indices))
            {
                indices.Remove(index);
                if (indices.Count == 0)
                {
                    _indicesByItem.Remove(item);
                }
            }
        }

        public void Clear()
        {
            _indicesByItem.Clear();
        }

        public bool TryGetFirstIndex(T item, out int index)
        {
            if (_indicesByItem.TryGetValue(item, out var indices) && indices.Count > 0)
            {
                index = indices[0];
                return true;
            }

            index = -1;
            return false;
        }

        public bool TryGetExactIndex(T item, int expectedIndex)
        {
            if (_indicesByItem.TryGetValue(item, out var indices))
            {
                return indices.Contains(expectedIndex);
            }
            return false;
        }

        public void ShiftIndices(int fromIndex, int delta)
        {
            foreach (var indices in _indicesByItem.Values)
            {
                for (int i = 0; i < indices.Count; i++)
                {
                    if (indices[i] >= fromIndex)
                    {
                        indices[i] += delta;
                    }
                }
            }
        }

        public IEnumerable<int> GetAllIndices(T item)
        {
            return _indicesByItem.TryGetValue(item, out var indices)
                ? indices
                : Array.Empty<int>();
        }

        public int GetCount(T item)
        {
            return _indicesByItem.TryGetValue(item, out var indices)
                ? indices.Count
                : 0;
        }
    }
}