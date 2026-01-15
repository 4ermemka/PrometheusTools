using Assets.Shared.ChangeDetector;
using System;
using UnityEngine;

namespace Assets.Shared.Model
{
    [Serializable]
    public sealed class BoxData : SyncNode // или : TrackableNode, если нужно
    {
        public Vector2 Position;
        // при необходимости: Id, цвет, имя и т.п.
    }
}