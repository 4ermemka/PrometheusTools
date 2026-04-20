using System;

namespace Assets.Shared.SyncSystem.Core
{
    public abstract class SyncBase : ITrackable
    {
        public abstract event Action<string, object, object> Changed;
        public abstract event Action<string, object> Patched;
        public abstract void ApplyPatch(string path, object value);
        public abstract object GetValue(string path);
        public abstract void SetValueSilent(object value);
    }
}