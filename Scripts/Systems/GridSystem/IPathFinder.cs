using System;
using System.Collections.Generic;

public interface IPathFinder<T> where T : IGridCell
{
    /// <summary>
    /// Поиск кратчайшего пути по числу переходов между зарегистрированными ячейками.
    /// predicate определяет, можно ли перейти на соседнюю ячейку; null разрешает все связи.
    /// Возвращает список ячеек от start до end включительно или null, если путь не найден.
    /// </summary>
    List<T> FindPath(T start, T end, Func<T, T, bool> predicate);
}
