using Assets.Shared.ChangeDetector;

/// <summary>
/// Простой счётчик для отладки сетевой синхронизации.
/// Изменение Value должно вызывать генерацию патча и расходиться по всем участникам.
/// </summary>
public sealed class DebugCounter : SyncNode
{
    private int _value;
    private string _label;

    [Sync]
    public int Value
    {
        get => _value;
        set => SetProperty(ref _value, value);
    }

    [Sync]
    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value);
    }

    public override string ToString() => $"{Label}: {Value}";
}