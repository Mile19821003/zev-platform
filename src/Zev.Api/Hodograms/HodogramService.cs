using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;

namespace Zev.Api.Hodograms;

public sealed record HodogramStepView(Guid Id, string Name, string? Description, int SortOrder,
    IReadOnlyDictionary<string, string> Cells);
public sealed record HodogramPhaseView(Guid Id, string Name, int SortOrder, IReadOnlyList<HodogramStepView> Steps);
public sealed record HodogramDetail(Guid Id, Guid OrganizationId, string Name, string? Description, bool IsActive,
    int Revision, DateTimeOffset UpdatedUtc, IReadOnlyList<string> Roles, IReadOnlyList<HodogramPhaseView> Phases,
    bool IsValid, IReadOnlyList<RaciIssue> Issues);
public sealed record HodogramMatrixCell(Guid StepId, string Role, string? Value);

public enum MatrixSaveOutcome { Saved, BadRequest, Invalid }
public sealed record MatrixSaveResult(MatrixSaveOutcome Outcome, IReadOnlyList<RaciIssue> Issues);

public sealed class HodogramService(ZevDbContext database)
{
    public static IReadOnlyList<string> Roles { get; } = Enum.GetNames<MembershipRole>();

    public async Task<HodogramDetail?> GetAsync(Guid organizationId, Guid hodogramId, CancellationToken cancellationToken)
    {
        var hodogram = await database.Hodograms.AsNoTracking().SingleOrDefaultAsync(item => item.Id == hodogramId
            && item.OrganizationId == organizationId, cancellationToken);
        if (hodogram is null) return null;
        var phases = await database.HodogramPhases.AsNoTracking().Where(item => item.HodogramId == hodogramId)
            .OrderBy(item => item.SortOrder).ThenBy(item => item.Name).ToListAsync(cancellationToken);
        var steps = await database.HodogramSteps.AsNoTracking().Where(item => item.HodogramId == hodogramId)
            .OrderBy(item => item.SortOrder).ThenBy(item => item.Name).ToListAsync(cancellationToken);
        var cells = await database.HodogramAssignments.AsNoTracking().Where(item => item.HodogramId == hodogramId)
            .ToListAsync(cancellationToken);
        var cellsByStep = cells.ToLookup(item => item.StepId);
        var stepsByPhase = steps.ToLookup(item => item.PhaseId);
        var orderedStepIds = phases.SelectMany(phase => stepsByPhase[phase.Id]).Select(step => step.Id).ToList();
        var issues = RaciValidator.Validate(orderedStepIds, cells.Select(item => (item.StepId, item.Value)));
        return new(hodogram.Id, hodogram.OrganizationId, hodogram.Name, hodogram.Description, hodogram.IsActive,
            hodogram.Revision, hodogram.UpdatedUtc, Roles,
            phases.Select(phase => new HodogramPhaseView(phase.Id, phase.Name, phase.SortOrder,
                stepsByPhase[phase.Id].Select(step => new HodogramStepView(step.Id, step.Name, step.Description,
                    step.SortOrder, cellsByStep[step.Id].ToDictionary(cell => cell.Role.ToString(),
                        cell => cell.Value.ToString()))).ToList())).ToList(),
            issues.Count == 0, issues);
    }

    // Loads the hodogram for a write and claims the next revision; SaveChanges fails on a concurrent writer.
    public async Task<(Hodogram? Hodogram, bool Stale)> BeginUpdateAsync(Guid organizationId, Guid hodogramId,
        int expectedRevision, CancellationToken cancellationToken)
    {
        var hodogram = await database.Hodograms.SingleOrDefaultAsync(item => item.Id == hodogramId
            && item.OrganizationId == organizationId, cancellationToken);
        if (hodogram is null) return (null, false);
        if (hodogram.Revision != expectedRevision) return (hodogram, true);
        hodogram.Revision = expectedRevision + 1;
        hodogram.UpdatedUtc = DateTimeOffset.UtcNow;
        return (hodogram, false);
    }

    public async Task<int> NextPhaseOrderAsync(Guid hodogramId, CancellationToken cancellationToken) =>
        (await database.HodogramPhases.Where(item => item.HodogramId == hodogramId)
            .MaxAsync(item => (int?)item.SortOrder, cancellationToken) ?? -1) + 1;

    public async Task<int> NextStepOrderAsync(Guid phaseId, CancellationToken cancellationToken) =>
        (await database.HodogramSteps.Where(item => item.PhaseId == phaseId)
            .MaxAsync(item => (int?)item.SortOrder, cancellationToken) ?? -1) + 1;

    // The payload is the complete matrix. Unchanged cells keep their identity; only real differences are written.
    public async Task<MatrixSaveResult> ApplyMatrixAsync(Hodogram hodogram, IReadOnlyList<HodogramMatrixCell> cells,
        CancellationToken cancellationToken)
    {
        var stepIds = await database.HodogramSteps.Where(item => item.HodogramId == hodogram.Id)
            .Select(item => item.Id).ToListAsync(cancellationToken);
        var knownSteps = stepIds.ToHashSet();
        var desired = new Dictionary<(Guid StepId, MembershipRole Role), RaciValue>();
        foreach (var cell in cells)
        {
            if (!knownSteps.Contains(cell.StepId) || !Enum.TryParse<MembershipRole>(cell.Role, false, out var role)
                || !Enum.IsDefined(role)) return new(MatrixSaveOutcome.BadRequest, []);
            if (string.IsNullOrWhiteSpace(cell.Value)) continue;
            if (!Enum.TryParse<RaciValue>(cell.Value.Trim(), true, out var value) || !Enum.IsDefined(value)
                || !desired.TryAdd((cell.StepId, role), value)) return new(MatrixSaveOutcome.BadRequest, []);
        }
        var issues = RaciValidator.Validate(stepIds, desired.Select(pair => (pair.Key.StepId, pair.Value)));
        if (issues.Count != 0) return new(MatrixSaveOutcome.Invalid, issues);

        var existing = await database.HodogramAssignments.Where(item => item.HodogramId == hodogram.Id)
            .ToListAsync(cancellationToken);
        foreach (var assignment in existing)
        {
            if (!desired.Remove((assignment.StepId, assignment.Role), out var value))
                database.HodogramAssignments.Remove(assignment);
            else if (assignment.Value != value)
                assignment.Value = value;
        }
        database.HodogramAssignments.AddRange(desired.Select(pair => new HodogramAssignment
        {
            OrganizationId = hodogram.OrganizationId, HodogramId = hodogram.Id,
            StepId = pair.Key.StepId, Role = pair.Key.Role, Value = pair.Value
        }));
        return new(MatrixSaveOutcome.Saved, []);
    }
}
