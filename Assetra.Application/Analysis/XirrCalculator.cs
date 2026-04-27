using Assetra.Core.Interfaces.Analysis;
using Assetra.Core.Models.Analysis;

namespace Assetra.Application.Analysis;

public sealed class XirrCalculator : IXirrCalculator
{
    private const int MaxIterations = 100;
    private const double Tolerance = 1e-7;

    public decimal? Compute(IReadOnlyList<CashFlow> flows, double guess = 0.1)
    {
        ArgumentNullException.ThrowIfNull(flows);
        if (flows.Count < 2) return null;

        var positives = flows.Any(f => f.Amount > 0);
        var negatives = flows.Any(f => f.Amount < 0);
        if (!positives || !negatives) return null;

        var d0 = flows.Min(f => f.Date);
        var pts = flows.Select(f => (
            t: (f.Date.DayNumber - d0.DayNumber) / 365.0,
            a: (double)f.Amount)).ToArray();

        var newton = NewtonRaphson(pts, guess);
        if (newton is not null) return (decimal)newton.Value;

        var bisect = Bisection(pts);
        return bisect is null ? null : (decimal)bisect.Value;
    }

    private static double? NewtonRaphson((double t, double a)[] pts, double guess)
    {
        var r = guess;
        for (var i = 0; i < MaxIterations; i++)
        {
            if (1 + r <= 0) return null;
            var npv = 0.0;
            var dnpv = 0.0;
            foreach (var (t, a) in pts)
            {
                var disc = Math.Pow(1 + r, -t);
                npv += a * disc;
                dnpv += -t * a * disc / (1 + r);
            }
            if (Math.Abs(dnpv) < 1e-15) return null;
            var next = r - npv / dnpv;
            if (Math.Abs(next - r) < Tolerance) return next;
            r = next;
        }
        return null;
    }

    private static double? Bisection((double t, double a)[] pts)
    {
        var lo = -0.99;
        var hi = 10.0;
        var fLo = Npv(pts, lo);
        var fHi = Npv(pts, hi);
        if (fLo * fHi > 0) return null;

        for (var i = 0; i < 200; i++)
        {
            var mid = (lo + hi) / 2;
            var fMid = Npv(pts, mid);
            if (Math.Abs(fMid) < Tolerance || (hi - lo) / 2 < Tolerance) return mid;
            if (fMid * fLo < 0) { hi = mid; fHi = fMid; }
            else { lo = mid; fLo = fMid; }
        }
        return (lo + hi) / 2;
    }

    private static double Npv((double t, double a)[] pts, double r)
    {
        var sum = 0.0;
        foreach (var (t, a) in pts) sum += a * Math.Pow(1 + r, -t);
        return sum;
    }
}
