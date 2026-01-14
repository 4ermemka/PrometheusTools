using Assets.Shared.ChangeDetector;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Простейший "мировой стейт": одна точка на сцене, у которой синхронизируется позиция.
/// Ведущие будут двигать этот объект локально, а изменения будут уходить по сети.
/// </summary>
public sealed class NetworkedSpriteState : SyncNode
{
    private Vector2 _position;

    [Sync]
    public Vector2 Position
    {
        get => _position;
        set => SetProperty(ref _position, value);
    }
}

