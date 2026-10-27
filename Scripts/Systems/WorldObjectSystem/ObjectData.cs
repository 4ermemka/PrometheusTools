using System;
using System.Collections.Generic;
using Assets.Shared.SyncSystem.Core;

namespace Shared.WorldObjects
{
    public interface IGridPositioned
    {
        GridCoordinate Position { get; set; }
    }

    public sealed class ObjectTransformData
    {
        // Относительно авторского transform префаба; значения не зависят от Unity.
        public float RotationX { get; set; }
        public float RotationY { get; set; }
        public float RotationZ { get; set; }
        public float ScaleX { get; set; } = 1;
        public float ScaleY { get; set; } = 1;
        public float ScaleZ { get; set; } = 1;
    }

    /// <summary>Стабильные ID, подтверждённая позиция и расширяемые параметры. Sync-поля не заменять.</summary>
    public abstract class ObjectData : TrackableNode, IGridPositioned
    {
        public readonly Sync<string> Id = new("");
        public readonly Sync<string> DefinitionId = new("");
        public readonly Sync<GridCoordinate> Cell = new();
        public readonly Sync<Guid> OwnerId = new();
        public readonly Sync<string> DisplayName = new("");
        public readonly Sync<ObjectTransformData> Transform = new(new ObjectTransformData());
        public readonly Sync<Dictionary<string, string>> Parameters = new(new Dictionary<string, string>());

        GridCoordinate IGridPositioned.Position { get => Cell.Value; set => Cell.Value = value; }
    }
}
