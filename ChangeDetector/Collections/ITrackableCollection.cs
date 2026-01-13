using System;
using System.Collections;
using UnityEngine;

namespace Assets.Shared.ChangeDetector
{
    /// <summary>
    /// Общий интерфейс для коллекций, которые умеют генерировать и применять патчи.
    /// </summary>
    public interface ITrackableCollection
    {
        /// <summary>
        /// Событие изменения коллекции (для отправки патчей по сети).
        /// </summary>
        event Action<CollectionChange> CollectionChanged;

        /// <summary>
        /// Применение патча к коллекции (на принимающей стороне).
        /// </summary>
        void ApplyCollectionChange(CollectionChange change);
    }

}