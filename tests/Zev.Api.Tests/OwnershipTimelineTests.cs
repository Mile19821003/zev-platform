using Xunit;
using Zev.Api.Domain;
using Zev.Api.Registry;

namespace Zev.Api.Tests;

public sealed class OwnershipTimelineTests
{
    [Fact]
    public void AcceptsSequentialOwnerChangeWithoutLosingHistory()
    {
        var firstOwner = Guid.NewGuid();
        var secondOwner = Guid.NewGuid();
        var history = new[]
        {
            Row(firstOwner, new DateOnly(2024, 1, 1), new DateOnly(2025, 1, 1), 1m),
            Row(secondOwner, new DateOnly(2025, 1, 1), null, 1m)
        };

        Assert.True(OwnershipTimeline.IsValid(history));
        Assert.Equal(firstOwner, history[0].PersonId);
        Assert.Equal(new DateOnly(2025, 1, 1), history[0].ValidTo);
    }

    [Fact]
    public void RejectsOverlappingSharesAboveOne()
    {
        var history = new[]
        {
            Row(Guid.NewGuid(), new DateOnly(2024, 1, 1), null, .6m),
            Row(Guid.NewGuid(), new DateOnly(2024, 2, 1), null, .5m)
        };

        Assert.False(OwnershipTimeline.IsValid(history));
    }

    [Fact]
    public void AcceptsConcurrentCoownersWhoseSharesSumToOne()
    {
        var history = new[]
        {
            Row(Guid.NewGuid(), new DateOnly(2024, 1, 1), null, .6m),
            Row(Guid.NewGuid(), new DateOnly(2024, 1, 1), null, .4m)
        };

        Assert.True(OwnershipTimeline.IsValid(history));
    }

    [Fact]
    public void RejectsOverlappingIntervalsForSamePerson()
    {
        var personId = Guid.NewGuid();
        var history = new[]
        {
            Row(personId, new DateOnly(2024, 1, 1), new DateOnly(2025, 1, 1), .5m),
            Row(personId, new DateOnly(2024, 6, 1), null, .5m)
        };

        Assert.False(OwnershipTimeline.IsValid(history));
    }

    [Fact]
    public void RejectsInvalidDatesAndShares()
    {
        Assert.False(OwnershipTimeline.IsValid([Row(Guid.NewGuid(), new DateOnly(2025, 1, 1), new DateOnly(2024, 1, 1), 1m)]));
        Assert.False(OwnershipTimeline.IsValid([Row(Guid.NewGuid(), new DateOnly(2025, 1, 1), null, 0m)]));
    }

    private static Ownership Row(Guid personId, DateOnly from, DateOnly? to, decimal share) =>
        new() { PersonId = personId, ValidFrom = from, ValidTo = to, Share = share };
}
