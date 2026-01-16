using Assets.Shared.ChangeDetector;
using System;

namespace Assets.Shared.Model
{
    [Serializable]
    public sealed class WorldData : SyncNode
    {
        private BoxData _boxData;

        [Sync]
        public BoxData BoxData
        {
            get => _boxData;
            set => SetProperty(ref _boxData, value);
        }
    }
}