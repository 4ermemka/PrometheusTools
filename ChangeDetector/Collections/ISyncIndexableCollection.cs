namespace Assets.Shared.ChangeDetector.Collections
{
    /// <summary>
    /// Коллекция, которая может применить полный снапшот своего содержимого.
    /// </summary>
    public interface ISnapshotCollection : ITrackableCollection
    {
        void ApplySnapshotFrom(object? sourceCollection);
    }

    /// <summary>
    /// Коллекция, в которую можно адресоваться по сегменту пути (индекс/ключ).
    /// </summary>
    public interface ISyncIndexableCollection : ITrackableCollection
    {
        int Count { get; }

        object? GetElement(string segmentName);
        void SetElement(string segmentName, object? value);
    }
}
