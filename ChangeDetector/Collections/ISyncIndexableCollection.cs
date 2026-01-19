namespace Assets.Shared.ChangeDetector.Collections
{
    public interface ISnapshotCollection
    {
        void ApplySnapshotFrom(object? sourceValue);
    }

    public interface ISyncIndexableCollection
    {
        object? GetElement(string index);
        void SetElement(string index, object? value);
    }
}
