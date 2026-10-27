using System;
using System.Collections.Generic;
using System.Globalization;
using Shared.WorldObjects;

namespace Shared.TurnPlanning
{
    public interface IGridPlanningWorld : IPlanningWorld
    {
        GridCoordinate PositionOf(string objectId);
        bool ContainsCell(GridCoordinate cell);
        void Move(string objectId, GridCoordinate cell);
    }

    /// <summary>Единственное начальное действие; ядро планирования о нём не знает.</summary>
    public sealed class MovementAction : IPlanAction
    {
        public const string ActionId = "move";
        public string Id => ActionId;
        public static Dictionary<string, string> Arguments(GridCoordinate cell) => new()
        {
            ["x"] = cell.X.ToString(CultureInfo.InvariantCulture),
            ["y"] = cell.Y.ToString(CultureInfo.InvariantCulture),
            ["z"] = cell.Z.ToString(CultureInfo.InvariantCulture)
        };
        public static bool TryDestination(PlannedAction step, out GridCoordinate cell)
        {
            cell = default;
            if (step.Arguments == null || !Read(step, "x", out int x) || !Read(step, "y", out int y) || !Read(step, "z", out int z)) return false;
            cell = new GridCoordinate(x, y, z);
            return true;
        }
        private static bool Read(PlannedAction step, string key, out int value)
        {
            value = 0;
            return step.Arguments.TryGetValue(key, out var text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
        public string Validate(IPlanningWorld world, PlannedAction step)
        {
            if (world is not IGridPlanningWorld grid || !world.Contains(step.ObjectId)) return "Объект отсутствует.";
            if (!TryDestination(step, out var cell)) return "Некорректные координаты движения.";
            return grid.ContainsCell(cell) ? null : "Ячейка отсутствует в сцене.";
        }
        public void Execute(IPlanningWorld world, PlannedAction step)
        {
            if (!TryDestination(step, out var cell)) throw new InvalidOperationException("Invalid movement arguments.");
            ((IGridPlanningWorld)world).Move(step.ObjectId, cell);
        }
    }
}
