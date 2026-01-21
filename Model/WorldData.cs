using Assets.Shared.SyncSystem.Core;
using UnityEngine;

namespace Assets.Shared.Model
{
    public class WorldState : TrackableNode
    {
        [SerializeField]
        public BoxData BoxData = new();
    }

    public class BoxData : TrackableNode
    {
        public Sync<Vector2Dto> Position = new Sync<Vector2Dto>();

        public BoxData()
        {
            Position.Value = Vector2Dto.One();
        }
    }
}