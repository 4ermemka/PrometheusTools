namespace Assets.Shared.ChangeDetector
{
    /// <summary>
    /// Изменение коллекции
    /// </summary>
    public struct CollectionChange
    {
        public CollectionOpKind Kind { get; }
        public object? KeyOrIndex { get; }
        public object? Value { get; }
        public object? OldValue { get; }

        public CollectionChange(CollectionOpKind kind, object? keyOrIndex, object? value, object? oldValue = null)
        {
            Kind = kind;
            KeyOrIndex = keyOrIndex;
            Value = value;
            OldValue = oldValue;
        }
    }
}