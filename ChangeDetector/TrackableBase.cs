using ParamsSynchronizer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public abstract class TrackableBase
{
    // Событие изменения; передается строка с полным путем, старым и новым значениями
    public event Action<string> OnChange;

    // Словарь для хранения подписок на события вложенных объектов
    private readonly Dictionary<string, IDisposable> _nestedSubscriptions = new Dictionary<string, IDisposable>();

    protected TrackableBase()
    {
        SubscribeNested();
    }

    // Находит поля и свойства, помеченные [TrackableField]
    private IEnumerable<MemberInfo> GetTrackableMembers()
    {
        BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        var fields = this.GetType()
            .GetFields(flags)
            .Where(f => f.GetCustomAttribute<TrackableFieldAttribute>() != null)
            .Cast<MemberInfo>();

        var properties = this.GetType()
            .GetProperties(flags)
            .Where(p => p.GetCustomAttribute<TrackableFieldAttribute>() != null)
            .Cast<MemberInfo>();

        return fields.Concat(properties);
    }

    // Подписка на события вложенных TrackableBase объектов
    private void SubscribeNested()
    {
        var trackableMembers = GetTrackableMembers();

        foreach (var member in trackableMembers)
        {
            var val = GetValue(member);
            if (val is TrackableBase nested)
            {
                if (_nestedSubscriptions.TryGetValue(member.Name, out var oldSub))
                {
                    oldSub.Dispose();
                    _nestedSubscriptions.Remove(member.Name);
                }

                void NestedHandler(string subChange)
                {
                    var fullChange = $"{member.Name}.{subChange}";
                    OnChange?.Invoke(fullChange);
                }

                nested.OnChange += NestedHandler;
                _nestedSubscriptions[member.Name] = new Subscription(() => nested.OnChange -= NestedHandler);
            }
        }
    }

    // Метод получения значения поля или свойства
    private object GetValue(MemberInfo member)
    {
        if (member is FieldInfo f)
            return f.GetValue(this);
        if (member is PropertyInfo p && p.GetMethod != null)
            return p.GetValue(this);
        return null;
    }

    // Метод установки значения поля или свойства с уведомлением и подпиской
    protected bool SetTrackedField<T>(ref T field, T value, string fieldName)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        var oldValue = field;
        field = value;

        SubscribeNested();

        NotifyChange(fieldName, oldValue, value);
        return true;
    }

    // Метод уведомления об изменении поля
    protected void NotifyChange(string fieldName, object oldValue, object newValue)
    {
        var oldValStr = oldValue?.ToString() ?? "null";
        var newValStr = newValue?.ToString() ?? "null";
        var message = $"{fieldName} {oldValStr} {newValStr}";
        OnChange?.Invoke(message);
    }

    // Метод применения изменения по строке вида "A.B.C.field oldValue newValue"
    public void ApplyChange(string changeData)
    {
        if (string.IsNullOrWhiteSpace(changeData))
            return;

        var parts = changeData.Split(new[] { ' ' }, 3);
        if (parts.Length < 3)
            throw new ArgumentException("Invalid changeData format. Expected format: 'path oldValue newValue'");

        var path = parts[0];
        var newValueStr = parts[2];

        ApplyChangeByPath(path.Split('.'), newValueStr);
    }

    // Внутренний рекурсивный метод для применения значения по пути
    private void ApplyChangeByPath(string[] pathParts, string newValueStr)
    {
        if (pathParts.Length == 0)
            return;

        string currentPart = pathParts[0];
        var members = GetTrackableMembers();

        var member = members.FirstOrDefault(m => string.Equals(m.Name, currentPart, StringComparison.Ordinal));
        if (member == null)
            throw new ArgumentException($"Field or property '{currentPart}' not found on {GetType().Name}");

        if (pathParts.Length == 1)
        {
            var memberType = member is PropertyInfo pinfo ? pinfo.PropertyType : ((FieldInfo)member).FieldType;

            object convertedValue = ConvertFromString(newValueStr, memberType);

            if (member is FieldInfo fi)
            {
                var fieldValue = (object)fi.GetValue(this);
                var method = typeof(TrackableBase).GetMethod(nameof(SetTrackedField), BindingFlags.Instance | BindingFlags.NonPublic)
                    .MakeGenericMethod(memberType);

                // Вызов SetTrackedField(ref field, value, fieldName) через reflection
                var parameters = new object[] { fieldValue, convertedValue, currentPart };

                // Для передачи ref параметра нужно менять значение напрямую
                // Поэтому здесь просто делаем SetValue напрямую и вызываем NotifyChange:

                fi.SetValue(this, convertedValue);
                NotifyChange(currentPart, fieldValue, convertedValue);
            }
            else if (member is PropertyInfo pi && pi.CanWrite)
            {
                var oldVal = pi.GetValue(this);
                pi.SetValue(this, convertedValue);
                NotifyChange(currentPart, oldVal, convertedValue);
            }
            else
            {
                throw new InvalidOperationException($"Member '{currentPart}' is not settable");
            }
        }
        else
        {
            object nestedObj = null;
            if (member is FieldInfo fi)
                nestedObj = fi.GetValue(this);
            else if (member is PropertyInfo pi && pi.CanRead)
                nestedObj = pi.GetValue(this);

            if (nestedObj is TrackableBase nestedTrackable)
                nestedTrackable.ApplyChangeByPath(pathParts.Skip(1).ToArray(), newValueStr);
            else
                throw new InvalidOperationException($"Member '{currentPart}' is not a TrackableBase");
        }
    }

    // Конвертация строки в целевой тип поля
    private object ConvertFromString(string value, Type targetType)
    {
        if (targetType == typeof(string))
            return value;

        if (targetType.IsEnum)
            return Enum.Parse(targetType, value);

        if (Nullable.GetUnderlyingType(targetType) != null)
        {
            if (string.IsNullOrEmpty(value))
                return null;
            targetType = Nullable.GetUnderlyingType(targetType);
        }

        return Convert.ChangeType(value, targetType);
    }

    // Вспомогательный класс для отписки от событий
    private class Subscription : IDisposable
    {
        private readonly Action _unsubscribe;
        private bool _disposed = false;
        public Subscription(Action unsubscribe)
        {
            _unsubscribe = unsubscribe;
        }
        public void Dispose()
        {
            if (!_disposed)
            {
                _unsubscribe?.Invoke();
                _disposed = true;
            }
        }
    }
}
