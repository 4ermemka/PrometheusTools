using Assets.Shared.SyncSystem.Core;
using System;

namespace Assets.Scripts.Network.NetCore
{
    // PatchMessage - сообщение с патчем
    [Serializable]
    public class PatchMessage
    {
        public ChangeData ChangeData { get; set; }
    }

    // ChangeData - изменение одного поля
    [Serializable]
    public class ChangeData
    {
        public string Path { get; set; }
        public object OldValue { get; set; }
        public object NewValue { get; set; }
        public long Timestamp { get; set; } = DateTime.UtcNow.Ticks;
        public Guid SourceClientId { get; set; }
    }

}