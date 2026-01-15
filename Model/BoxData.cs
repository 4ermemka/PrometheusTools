using Assets.Shared.ChangeDetector;
using System;
using UnityEngine;

namespace Assets.Shared.Model
{
    [Serializable]
    public sealed class BoxData : SyncNode // или : TrackableNode, если нужно
    {
        private Vector2 _position;

        [Sync]
        public Vector2 Position
        {
            get => _position;
            set => SetProperty(ref _position, value);
        }

        // при необходимости: Id, цвет, имя и т.п.
    }
}