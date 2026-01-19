using System;

namespace Assets.Shared.ChangeDetector.Base.Mapping
{
    public interface ISyncSerializable
    {
        /// <summary>
        /// Получает данные для сериализации
        /// </summary>
        object GetSerializableData();

        /// <summary>
        /// Применяет данные из сериализации
        /// </summary>
        void ApplySerializedData(object data);
    }

    public interface ISyncCollection : ISyncSerializable
    {
        Type ElementType { get; }
    }
}