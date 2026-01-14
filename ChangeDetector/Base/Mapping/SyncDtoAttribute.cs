using System;

namespace Assets.Shared.ChangeDetector.Base
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class SyncDtoAttribute : Attribute
    {
        public Type ModelType { get; }

        public SyncDtoAttribute(Type modelType)
        {
            ModelType = modelType;
        }
    }

}