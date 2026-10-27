using System;
using Newtonsoft.Json;

namespace Shared.WorldObjects
{
    /// <summary>
    /// Сетевой адрес ячейки одной сетки: X/Z относительно origin, Y — индекс высоты.
    /// Это значение, а не ссылка на GridCell; фактическую высоту поверхности хранит GridProvider.
    /// Все участники сессии должны использовать одну и ту же версию сетки.
    /// </summary>
    public readonly struct GridCoordinate : IEquatable<GridCoordinate>
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Z;

        [JsonConstructor]
        public GridCoordinate(int x, int y, int z) { X = x; Y = y; Z = z; }

        public bool Equals(GridCoordinate other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is GridCoordinate other && Equals(other);
        public override int GetHashCode() => unchecked((X * 397 ^ Y) * 397 ^ Z);
        public override string ToString() => $"({X}, {Y}, {Z})";
        public static bool operator ==(GridCoordinate a, GridCoordinate b) => a.Equals(b);
        public static bool operator !=(GridCoordinate a, GridCoordinate b) => !a.Equals(b);
    }
}
