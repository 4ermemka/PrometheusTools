using Assets.Shared.ChangeDetector;
using System;
using UnityEngine;

namespace Assets.Shared.Model
{
    [Serializable]
    public class BoxData : SyncNode
    {
        // ДОЛЖЕН быть [SyncField] и SyncProperty<T>
        [SyncField]
        public SyncProperty<Vector2> Position { get; set; } = null!;

        // Конструктор для инициализации
        public BoxData(Vector2 position)
        {
            // Автоматически инициализируется через SyncField,
            // но можно установить начальное значение:
            Position.Value = position;
        }

        // Метод для изменения позиции
        public void Move(Vector2 newPosition)
        {
            // Это вызовет патч!
            Position.Value = newPosition;
        }

        // при необходимости: Id, цвет, имя и т.п.
    }
}