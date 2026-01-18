using Assets.Shared.ChangeDetector;
using Assets.Shared.ChangeDetector.Collections;
using System;
using System.Linq;
using UnityEngine;

namespace Assets.Shared.Model
{
    [Serializable]
    public class WorldData : SyncNode
    {
        [SyncField]
        public SyncList<BoxData> Boxes { get; private set; } = new();

        // Подписка на изменения
        public WorldData()
        {
            // Подписываемся на изменения во всем WorldData
            Changed += change =>
            {
                Debug.Log($"Патч сгенерирован: {string.Join(".", change.Path.Select(p => p.Name))}");
                Debug.Log($"Старое значение: {change.OldValue}, Новое: {change.NewValue}");

            };
        }
    }
}