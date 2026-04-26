using System;
using System.Collections.Generic;

public interface IPathFinder<T> where T : IGridCell
{
    /// <summary>
    /// Поиск кратчайшего пути между двумя ячейками.
    /// predicate определяет, можно ли перейти на соседнюю ячейку.
    /// Возвращает список ячеек от start до end включительно или null, если путь не найден.
    /// </summary>
    List<T> FindPath(T start, T end, Func<T, T, bool> predicate);
}