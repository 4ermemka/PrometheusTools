using Assets.Shared.ChangeDetector;
using Assets.Shared.ChangeDetector.Collections;
using System;

namespace Assets.Shared.Model
{
    [Serializable]
    public sealed class WorldData : SyncNode
    {
        [SyncField]
        public SyncList<BoxData> Boxes { get; set; } = null!;
    }
}