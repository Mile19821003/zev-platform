using Zev.Api.Domain;

namespace Zev.Api.Hodograms;

public sealed record RaciIssue(Guid StepId, string Code, string Message);

public static class RaciValidator
{
    public const string MissingResponsible = "MISSING_RESPONSIBLE";
    public const string MissingAccountable = "MISSING_ACCOUNTABLE";
    public const string MultipleAccountable = "MULTIPLE_ACCOUNTABLE";

    // Every step needs at least one R and exactly one A; C and I are optional and may repeat.
    public static IReadOnlyList<RaciIssue> Validate(IEnumerable<Guid> stepIds,
        IEnumerable<(Guid StepId, RaciValue Value)> cells)
    {
        var byStep = cells.ToLookup(cell => cell.StepId, cell => cell.Value);
        var issues = new List<RaciIssue>();
        foreach (var stepId in stepIds)
        {
            var values = byStep[stepId].ToList();
            if (!values.Contains(RaciValue.R))
                issues.Add(new(stepId, MissingResponsible, "Korak mora imati najmanje jednu ulogu R (izvršava)."));
            var accountable = values.Count(value => value == RaciValue.A);
            if (accountable == 0)
                issues.Add(new(stepId, MissingAccountable, "Korak mora imati tačno jednu ulogu A (odgovoran)."));
            else if (accountable > 1)
                issues.Add(new(stepId, MultipleAccountable, "Korak smije imati samo jednu ulogu A (odgovoran)."));
        }
        return issues;
    }
}
