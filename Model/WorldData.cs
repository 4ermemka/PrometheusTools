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

    }
}