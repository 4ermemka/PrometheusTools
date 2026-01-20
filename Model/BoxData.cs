using Assets.Shared.ChangeDetector;
using Assets.Shared.Model.Extensions;
using System;
using UnityEngine;

namespace Assets.Shared.Model
{
    [Serializable]
    public class BoxData : SyncNode
    {
        // ДОЛЖЕН быть [SyncField] и SyncProperty<T>
        [SyncField]
        public SyncProperty<Vector2Dto> Position { get; set; } = null!;

        // Конструктор для инициализации
        public BoxData()
        {
            // Автоматически инициализируется через SyncField,
            // но можно установить начальное значение:
            Position.Value = Vector2.one.FromVector2();
        }
        // Конструктор для инициализации
        public BoxData(Vector2 position)
        {
            // Автоматически инициализируется через SyncField,
            // но можно установить начальное значение:
            Position.Value = position.FromVector2();
        }

        // Метод для изменения позиции
        public void Move(Vector2 newPosition)
        {
            // Это вызовет патч!
            Position.Value = newPosition.FromVector2();
        }

        // при необходимости: Id, цвет, имя и т.п.
    }
}