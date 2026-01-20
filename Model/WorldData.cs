using Assets.Shared.SyncSystem.Core;
using UnityEngine;

namespace Assets.Shared.Model
{
    public class WorldState : TrackableNode
    {
        [SerializeField]
        public PlayerData PlayerData = new();
    }

    public class PlayerData : TrackableNode
    {
        public Sync<int> Health = new Sync<int>();
        public Sync<int> Score = new Sync<int>();
        public Sync<string> Name = new Sync<string>();
        public Sync<Vector2Dto> Position = new Sync<Vector2Dto>();

        public PlayerData()
        {
            // Можно добавить дополнительную инициализацию
            Health.Value = 100;
            Score.Value = 100;
            Name.Value = "Player";
            Position.Value = Vector2Dto.One();
        }
    }
}