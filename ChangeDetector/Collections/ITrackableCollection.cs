using Assets.Shared.ChangeDetector.Collections;
using System;
using System.Collections;
using UnityEngine;

namespace Assets.Shared.ChangeDetector
{
    /// <summary>
    /// Общий интерфейс для коллекций, которые умеют генерировать и применять патчи.
    /// </summary>
    public interface ITrackableCollection : ISyncIndexableCollection
    {
        /// <summary>
        /// Событие изменения элементов коллекции
        /// </summary>
        event Action<FieldChange>? CollectionElementChanged;

        /// <summary>
        /// Событие операций с коллекцией
        /// </summary>
        event Action<CollectionChange>? CollectionChanged;

        /// <summary>
        /// Применение патча к коллекции (на принимающей стороне).
        /// </summary>
        void ApplyCollectionChange(CollectionChange change);
    }

}