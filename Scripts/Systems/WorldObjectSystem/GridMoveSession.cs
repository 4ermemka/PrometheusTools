using System;

namespace Shared.WorldObjects
{
    /// <summary>Узкий контракт сетки для логики перемещения, без зависимости от Unity.</summary>
    public interface IGridSpace
    {
        bool Contains(GridCoordinate cell);
    }

    /// <summary>
    /// Двухфазное перемещение: Preview проверяет кандидата, Commit повторяет проверку и
    /// единожды меняет Data. Правило возвращает null при разрешении либо причину отказа.
    /// Подходит и для мыши, и для команд/ИИ. Отображение preview остаётся у вызывающего кода.
    /// </summary>
    public sealed class GridMoveSession<T> where T : class, IGridPositioned
    {
        private readonly T item;
        private readonly IGridSpace grid;
        private readonly Func<T, GridCoordinate, string> rule;
        private readonly GridCoordinate origin;
        private GridCoordinate candidate;
        private bool hasCandidate;

        public bool IsActive { get; private set; } = true;
        public GridCoordinate Candidate => candidate;

        public GridMoveSession(T item, IGridSpace grid, Func<T, GridCoordinate, string> rule = null)
        {
            this.item = item ?? throw new ArgumentNullException(nameof(item));
            this.grid = grid ?? throw new ArgumentNullException(nameof(grid));
            this.rule = rule;
            origin = item.Position;
        }

        public bool Preview(GridCoordinate target, out string error)
        {
            candidate = target;
            hasCandidate = Validate(target, out error);
            return hasCandidate;
        }

        public bool Commit(out string error)
        {
            if (!hasCandidate) { error = "Не выбрана ячейка."; IsActive = false; return false; }
            bool valid = Validate(candidate, out error);
            IsActive = false;
            if (valid) item.Position = candidate;
            return valid;
        }

        public void Cancel() { IsActive = false; hasCandidate = false; }

        private bool Validate(GridCoordinate target, out string error)
        {
            error = !IsActive ? "Перенос завершён." : item.Position != origin
                ? "Положение объекта изменилось во время переноса."
                : !grid.Contains(target) ? "Ячейка отсутствует в сетке." : rule?.Invoke(item, target);
            return error == null;
        }
    }
}
