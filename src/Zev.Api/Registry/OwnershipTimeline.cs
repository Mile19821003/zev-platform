using Zev.Api.Domain;

namespace Zev.Api.Registry;

public static class OwnershipTimeline
{
    public static bool IsValid(IEnumerable<Ownership> ownerships)
    {
        var rows = ownerships.ToList();
        if (rows.Any(row => row.Share <= 0m || row.Share > 1m || row.ValidTo <= row.ValidFrom))
        {
            return false;
        }

        foreach (var byPerson in rows.GroupBy(row => row.PersonId))
        {
            var ordered = byPerson.OrderBy(row => row.ValidFrom).ToList();
            for (var index = 1; index < ordered.Count; index++)
            {
                if (ordered[index - 1].ValidTo is null || ordered[index - 1].ValidTo > ordered[index].ValidFrom)
                {
                    return false;
                }
            }
        }

        var events = new SortedDictionary<DateOnly, decimal>();
        foreach (var ownership in rows)
        {
            events[ownership.ValidFrom] = events.GetValueOrDefault(ownership.ValidFrom) + ownership.Share;
            if (ownership.ValidTo is DateOnly validTo)
            {
                events[validTo] = events.GetValueOrDefault(validTo) - ownership.Share;
            }
        }

        var activeShare = 0m;
        foreach (var change in events)
        {
            activeShare += change.Value;
            if (activeShare > 1m || activeShare < 0m)
            {
                return false;
            }
        }

        return true;
    }
}
