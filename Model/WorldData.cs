using Assets.Shared.ChangeDetector;
using System;

namespace Assets.Shared.Model
{
    [Serializable]
    public class WorldData : SyncNode
    {
        [SyncField]
        public BoxData Box { get; set; }
    }
}