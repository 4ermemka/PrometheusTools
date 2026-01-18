using System;

namespace Assets.Shared.ChangeDetector
{
    /// <summary>
    /// Атрибут для автоматической регистрации SyncProperty и коллекций.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public class SyncFieldAttribute : Attribute
    {
        public bool TrackChanges { get; set; } = true;
        public bool ReceivePatches { get; set; } = true;
        public object? DefaultValue { get; set; }
        public bool IgnoreInSnapshot { get; set; }
    }
}