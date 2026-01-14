using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Простой контроллер, который двигает спрайт по WASD/стрелкам,
/// обновляя NetworkedSpriteState.Position. Все изменения улетают в сеть.
/// </summary>
[RequireComponent(typeof(WorldStateMono))]
public sealed class SpriteMoveDebug : MonoBehaviour
{
    [SerializeField] private float speed = 5f;

    private WorldStateMono _worldMono;
    private NetworkedSpriteState _state;

    private void Start()
    {
        var worldMono = GetComponent<WorldStateMono>();
        _state = worldMono != null ? worldMono.State : null;

        if (_state == null)
            Debug.LogError("[SpriteMoveDebug] State is null, check WorldStateMono on this GameObject");
    }

    private void Awake()
    {
        _worldMono = GetComponent<WorldStateMono>();
        _state = _worldMono != null ? _worldMono.State : null;
    }

    private void Update()
    {
        if (_state == null)
            return; // защитный ранний выход

        var input = new Vector2(
            Input.GetAxisRaw("Horizontal"),
            Input.GetAxisRaw("Vertical")
        );

        if (input.sqrMagnitude <= 0f)
            return;

        var delta = input.normalized * speed * Time.deltaTime;
        _state.Position += delta;
    }
}
