using Zev.Api.Domain;

namespace Zev.Api.Community;

public sealed record VotingOutcome(decimal EligibleWeight, decimal CastWeight, decimal YesWeight,
    decimal NoWeight, decimal AbstainWeight, bool QuorumMet, bool Approved);

public static class VotingCalculator
{
    public static VotingOutcome Calculate(decimal eligibleWeight, IEnumerable<Vote> votes,
        decimal quorumPercent, decimal approvalPercent, ApprovalDenominator denominator)
    {
        var rows = votes.ToList();
        var cast = rows.Sum(item => item.WeightSnapshot);
        var yes = rows.Where(item => item.Choice == VoteChoice.Yes).Sum(item => item.WeightSnapshot);
        var no = rows.Where(item => item.Choice == VoteChoice.No).Sum(item => item.WeightSnapshot);
        var abstain = rows.Where(item => item.Choice == VoteChoice.Abstain).Sum(item => item.WeightSnapshot);
        var approvalBase = denominator switch
        {
            ApprovalDenominator.Eligible => eligibleWeight,
            ApprovalDenominator.Participating => cast,
            ApprovalDenominator.YesNo => yes + no,
            _ => throw new ArgumentOutOfRangeException(nameof(denominator))
        };
        return new VotingOutcome(eligibleWeight, cast, yes, no, abstain,
            eligibleWeight > 0m && cast * 100m >= eligibleWeight * quorumPercent,
            approvalBase > 0m && yes * 100m >= approvalBase * approvalPercent);
    }
}
