using System;
using System.Collections;
using UnityEngine;

namespace Assets.Shared.ChangeDetector.Base.Mapping
{
    /// <summary>
    /// Член, который может получать входящие изменения через ApplyPatch
    /// (SyncNode применяет патчи к этим членам без генерации Changed)
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class PatchableAttribute : Attribute
    {
        /// <summary>
        /// Имя поля для записи (если отличается от имени члена)
        /// </summary>
        public string? BackingField { get; set; }

        /// <summary>
        /// Игнорировать при ApplySnapshot
        /// </summary>
        public bool IgnoreInSnapshot { get; set; }
    }
}