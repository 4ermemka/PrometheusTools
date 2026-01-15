using Assets.Shared.ChangeDetector;
using Assets.Shared.ChangeDetector.Collections;
using System;
using System.Collections.Generic;

namespace Assets.Shared.Model
{
    [Serializable]
    public sealed class WorldData : SyncNode
    {
        [Sync]
        public SyncList<BoxData> Boxes = new SyncList<BoxData>();
    }
}