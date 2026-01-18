using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Assets.Shared.ChangeDetector
{
    public abstract class TrackableNode
    {
        public event Action<FieldChange>? Changed;

        protected virtual void RaiseChange(FieldChange change)
        {
            Changed?.Invoke(change);
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            var oldValue = field;
            field = value;

            var path = new List<FieldPathSegment> { new FieldPathSegment(propertyName) };
            RaiseChange(new FieldChange(path, oldValue, value));

            return true;
        }
    }
}