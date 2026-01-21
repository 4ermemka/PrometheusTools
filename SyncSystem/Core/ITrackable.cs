using System;

namespace Assets.Shared.SyncSystem.Core
{
    public interface ITrackable
    {
        event Action<string, object, object> Changed;  // Для локальных изменений → сеть
        event Action<string, object> Patched;          // Для сетевых изменений → локальная реакция
        void ApplyPatch(string path, object value);
        object GetValue(string path);
    }
}