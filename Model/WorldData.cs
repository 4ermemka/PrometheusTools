using Assets.Shared.ChangeDetector;
using System;
using System.Collections.Generic;

namespace Assets.Shared.Model
{
    [Serializable]
    public sealed class WorldData : SyncNode
    {
        public List<BoxData> Boxes = new List<BoxData>();
    }
}