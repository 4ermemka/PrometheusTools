using System;
using System.Collections;
using UnityEngine;

namespace Assets.Shared.ChangeDetector.Base.Mapping
{
    /// <summary>
    /// Член, который отслеживается для генерации исходящих изменений
    /// (TrackableNode подписывается и поднимает Changed)
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class TrackAttribute : Attribute
    {
        /// <summary>
        /// Автоматически создавать экземпляр, если null
        /// </summary>
        public bool AutoCreate { get; set; } = true;

        /// <summary>
        /// Подписываться на изменения дочерних TrackableNode
        /// </summary>
        public bool WireChildren { get; set; } = true;
    }
}