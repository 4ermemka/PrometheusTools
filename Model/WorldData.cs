using Assets.Shared.ChangeDetector;
using Assets.Shared.ChangeDetector.Collections;
using System;

namespace Assets.Shared.Model
{
    [Serializable]
    public sealed class WorldData : SyncNode
    {
        private SyncList<BoxData> _boxes;

        [Sync]
        public SyncList<BoxData> Boxes
        {
            get => _boxes;
            set => SetProperty(ref _boxes, value);
        }
    }
}