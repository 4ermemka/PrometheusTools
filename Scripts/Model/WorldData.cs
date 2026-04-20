using Assets.Shared.SyncSystem.Collections;
using Assets.Shared.SyncSystem.Core;
using UnityEngine;

namespace Assets.Shared.Model
{
    public class WorldState : TrackableNode
    {
        [SerializeField]
        public SyncList<BoxData> Boxes = new();
    }

    public class BoxData : TrackableNode
    {
        public Sync<Vector2Dto> Position = new Sync<Vector2Dto>();

        public BoxData()
        {
            Position.Value = Vector2Dto.Zero();
        }
    }
}