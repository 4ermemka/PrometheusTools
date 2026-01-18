using Assets.Shared.ChangeDetector;
using Assets.Shared.ChangeDetector.Base;
using System;
using UnityEngine;

namespace Assets.Shared.Model
{
    [Serializable]
    public sealed class BoxData : SyncNode // или : TrackableNode, если нужно
    {
        [SyncField]
        public SyncProperty<Vector2> Position { get; set; } = null!;

        // при необходимости: Id, цвет, имя и т.п.
    }
}