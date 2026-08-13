namespace SuspensionSimulation.Components;

/// <summary>Conservative piecewise-linear curve. X must be strictly increasing.</summary>
public sealed class PiecewiseLinearCurve
{
    private readonly IReadOnlyList<CurvePoint> _points;

    public PiecewiseLinearCurve(IEnumerable<CurvePoint> points)
    {
        _points = points?.ToArray() ?? throw new ArgumentNullException(nameof(points));
        if (_points.Count < 2) throw new ArgumentException("At least two curve points are required.", nameof(points));
        for (var i = 0; i < _points.Count; i++)
        {
            if (!double.IsFinite(_points[i].X) || !double.IsFinite(_points[i].Y)) throw new ArgumentException("Curve values must be finite.", nameof(points));
            if (i > 0 && _points[i].X <= _points[i - 1].X) throw new ArgumentException("Curve X values must be strictly increasing.", nameof(points));
        }
    }

    public IReadOnlyList<CurvePoint> Points => _points;

    public (double Value, double Slope, bool IsExtrapolated) Evaluate(double x)
    {
        if (!double.IsFinite(x)) throw new ArgumentOutOfRangeException(nameof(x));
        var extrapolated = x < _points[0].X || x > _points[^1].X;
        var index = x <= _points[0].X ? 0 : x >= _points[^1].X ? _points.Count - 2 : FindSegment(x);
        var a = _points[index];
        var b = _points[index + 1];
        var slope = (b.Y - a.Y) / (b.X - a.X);
        return (a.Y + slope * (x - a.X), slope, extrapolated);
    }

    private int FindSegment(double x)
    {
        var low = 0; var high = _points.Count - 1;
        while (high - low > 1)
        {
            var middle = low + (high - low) / 2;
            if (_points[middle].X <= x) low = middle; else high = middle;
        }
        return low;
    }
}
