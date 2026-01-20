using System;

namespace Assets.Shared.ChangeDetector
{
    [AttributeUsage(AttributeTargets.Property)]
    public class SyncFieldAttribute : Attribute
    {
        public bool TrackChanges { get; set; } = true;
        public bool ReceivePatches { get; set; } = true;
        public bool IgnoreInSnapshot { get; set; } = false;
    }
}