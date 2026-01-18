namespace Assets.Shared.ChangeDetector.Collections
{
    public interface ISnapshotCollection
    {
        void ApplySnapshotFrom(object? sourceCollection);
    }

    public interface ISyncIndexableCollection
    {
        int Count { get; }
        object? GetElement(string segmentName);
        void SetElement(string segmentName, object? value);
    }
}
