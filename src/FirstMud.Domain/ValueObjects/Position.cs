using FirstMud.Domain.Enums;

namespace FirstMud.Domain.ValueObjects;

public sealed record Position(WorldId World, int ZoneId, int X, int Y)
{
    public double DistanceTo(Position other)
    {
        if (World != other.World || ZoneId != other.ZoneId)
            return double.MaxValue;

        var dx = X - other.X;
        var dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public bool IsAdjacentTo(Position other)
    {
        if (World != other.World || ZoneId != other.ZoneId)
            return false;

        return Math.Abs(X - other.X) <= 1 && Math.Abs(Y - other.Y) <= 1;
    }
}
