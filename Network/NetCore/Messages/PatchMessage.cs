using Assets.Shared.SyncSystem.Core;

namespace Assets.Scripts.Network.NetCore
{
    /// <summary>
    /// Сетевое сообщение-патч: путь до поля и новое значение.
    /// Отправляется от клиента к хосту и от хоста ко всем клиентам.
    /// </summary>
    public sealed class PatchMessage
    {
        public string Path;
        public object? NewValue;
        public object? OldValue;
    }

}