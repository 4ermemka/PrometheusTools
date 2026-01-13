using System.Collections;
using UnityEngine;

namespace Assets.Shared.ChangeDetector
{
    /// <summary>
    /// Тип операции над коллекцией в сетевой синхронизации.
    /// </summary>
    public enum CollectionOpKind
    {
        /// <summary>Добавление элемента.</summary>
        Add,

        /// <summary>Удаление элемента.</summary>
        Remove,

        /// <summary>Замена элемента.</summary>
        Replace,

        /// <summary>Перемещение элемента (актуально для списков).</summary>
        Move,

        /// <summary>Полная очистка коллекции.</summary>
        Clear
    }
}