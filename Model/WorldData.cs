using Assets.Shared.ChangeDetector;
using Assets.Shared.ChangeDetector.Collections;
using System;
using UnityEngine;

namespace Assets.Shared.Model
{
    [Serializable]
    public sealed class WorldData : SyncNode
    {
        [SerializeField]
        [SyncField]
        public SyncList<BoxData> Boxes { get; set; } = new();
    }
}