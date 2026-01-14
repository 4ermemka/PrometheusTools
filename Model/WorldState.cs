using Assets.Shared.ChangeDetector;
using System.Linq;
using UnityEngine;

/// <summary>
/// Обёртка над NetworkedSpriteState, которая вешается на объект со спрайтом.
/// Этот MonoBehaviour отвечает за связь между Sync-моделью и Transform.
/// </summary>
public sealed class WorldStateMono : MonoBehaviour
{
    public NetworkedSpriteState State { get; private set; }

    private void Awake()
    {
        if (State == null)
        {
            State = new NetworkedSpriteState();
            State.Position = transform.position;
            State.Changed += OnStateChanged;
        }
    }

    private void OnDestroy()
    {
        if (State != null)
            State.Changed -= OnStateChanged;
    }

    private void OnStateChanged(FieldChange change)
    {
        var pathStr = string.Join(".", change.Path.Select(p => p.Name));
        //Debug.Log($"[WORLD_MONO] Change {pathStr}: {change.OldValue} -> {change.NewValue}");

        if (change.Path.Count == 1 &&
            change.Path[0].Name == nameof(NetworkedSpriteState.Position) &&
            change.NewValue is Vector2 v2)
        {
            transform.position = new Vector3(v2.x, v2.y, transform.position.z);
        }
    }
}

