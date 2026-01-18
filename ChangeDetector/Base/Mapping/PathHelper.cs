using System;

namespace Assets.Shared.ChangeDetector
{
    /// <summary>
    /// Вспомогательные методы для работы с путями
    /// </summary>
    public static class PathHelper
    {
        /// <summary>
        /// Проверяет, является ли сегмент индексом коллекции
        /// </summary>
        public static bool IsCollectionIndex(string segment)
        {
            return !string.IsNullOrEmpty(segment) &&
                   segment.Length >= 3 &&
                   segment[0] == '[' &&
                   segment[^1] == ']';
        }

        /// <summary>
        /// Извлекает индекс из сегмента [index]
        /// </summary>
        public static int ParseListIndex(string segment)
        {
            if (!IsCollectionIndex(segment))
                throw new FormatException($"Invalid collection index format: {segment}");

            var content = segment.Substring(1, segment.Length - 2);
            if (int.TryParse(content, out var index))
                return index;

            throw new FormatException($"Cannot parse index from: {segment}");
        }

        /// <summary>
        /// Извлекает ключ из сегмента ["key"] или ['key']
        /// </summary>
        public static string ParseDictionaryKey(string segment)
        {
            if (!IsCollectionIndex(segment))
                throw new FormatException($"Invalid dictionary key format: {segment}");

            var content = segment.Substring(1, segment.Length - 2);

            // Убираем кавычки если есть
            if ((content.StartsWith("\"") && content.EndsWith("\"")) ||
                (content.StartsWith("'") && content.EndsWith("'")))
            {
                content = content.Substring(1, content.Length - 2);
            }

            return content;
        }

        /// <summary>
        /// Создает сегмент пути для индекса списка
        /// </summary>
        public static string CreateIndexSegment(int index) => $"[{index}]";

        /// <summary>
        /// Создает сегмент пути для ключа словаря
        /// </summary>
        public static string CreateKeySegment(string key) => $"[\"{key}\"]";
    }
}