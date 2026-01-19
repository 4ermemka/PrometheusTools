using System;

namespace Assets.Shared.ChangeDetector
{
    /// <summary>
    /// Вспомогательные методы для работы с путями.
    /// </summary>
    public static class PathHelper
    {
        public static bool IsCollectionIndex(string segment)
        {
            return int.TryParse(segment, out _);
        }
    }
}