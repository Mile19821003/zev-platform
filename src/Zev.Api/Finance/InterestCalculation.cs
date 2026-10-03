using Zev.Api.Domain;

namespace Zev.Api.Finance;

public static class InterestCalculation
{
    public static decimal? Calculate(decimal principal, DateOnly dueDate, DateOnly asOf,
        IEnumerable<InterestRatePolicy> policies)
    {
        if (principal <= 0m || asOf <= dueDate) return 0m;
        var start = dueDate.AddDays(1);
        var endExclusive = asOf.AddDays(1);
        var current = start;
        var interest = 0m;
        foreach (var policy in policies.Where(item => item.IsActive).OrderBy(item => item.ValidFrom))
        {
            if (policy.AnnualRatePercent < 0m || policy.DaysPerYear <= 0
                || policy.ValidTo <= policy.ValidFrom) return null;
            var segmentStart = policy.ValidFrom > start ? policy.ValidFrom : start;
            var segmentEnd = policy.ValidTo is DateOnly limit && limit < endExclusive ? limit : endExclusive;
            if (segmentEnd <= current) continue;
            if (segmentStart > current) return null;
            var days = segmentEnd.DayNumber - current.DayNumber;
            interest += principal * policy.AnnualRatePercent / 100m * days / policy.DaysPerYear;
            current = segmentEnd;
            if (current >= endExclusive) break;
        }
        return current < endExclusive ? null : decimal.Round(interest, 2, MidpointRounding.AwayFromZero);
    }
}
