using Assets.Shared.ChangeDetector;
using Assets.Shared.ChangeDetector.Collections;

/// <summary>
/// Корневое состояние для теста: набор счётчиков по ID.
/// </summary>
public sealed class WorldState : SyncNode
{
    // Словарь счётчиков: ключом будет условный ID игрока/сущности.
    [Sync]
    public SyncDictionary<int, DebugCounter> Counters { get; set; }

    public WorldState()
    {
        // TrackableNode/SyncNode сами инициализируют вложенные узлы,
        // если свойство null и тип не абстрактный.
        // Но можно явно подстраховаться:
        if (Counters == null)
            Counters = new SyncDictionary<int, DebugCounter>();
    }
}