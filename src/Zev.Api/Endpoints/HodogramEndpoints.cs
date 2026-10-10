using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Hodograms;
using Zev.Api.Security;

namespace Zev.Api.Endpoints;

public static class HodogramEndpoints
{
    private const int MaxSortOrder = 10000;

    public static RouteGroupBuilder MapHodogramEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/organizations/{organizationId:guid}/hodograms");

        group.MapGet("", async (Guid organizationId, TenantAccess access, ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (!await access.CanReadOrganizationAsync(organizationId, cancellationToken)) return Results.NotFound();
            var items = await database.Hodograms.AsNoTracking().Where(item => item.OrganizationId == organizationId)
                .OrderByDescending(item => item.IsActive).ThenBy(item => item.Name)
                .Select(item => new { item.Id, item.Name, item.Description, item.IsActive, item.Revision, item.UpdatedUtc,
                    phaseCount = database.HodogramPhases.Count(phase => phase.HodogramId == item.Id),
                    stepCount = database.HodogramSteps.Count(step => step.HodogramId == item.Id) })
                .Take(200).ToListAsync(cancellationToken);
            return Results.Ok(items);
        });

        group.MapPost("", async (Guid organizationId, CreateHodogramRequest request, TenantAccess access,
            HodogramService service, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (!await access.CanReadOrganizationAsync(organizationId, cancellationToken)) return Results.NotFound();
            if (!AdminValidation.Required(request.Name, 200) || request.Description?.Trim().Length > 2000)
                return Results.BadRequest();
            var name = request.Name.Trim();
            if (await database.Hodograms.AnyAsync(item => item.OrganizationId == organizationId && item.Name == name,
                cancellationToken)) return DuplicateName();
            var hodogram = new Hodogram { OrganizationId = organizationId, Name = name,
                Description = AdminValidation.Clean(request.Description) };
            database.Hodograms.Add(hodogram);
            try { await database.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateException problem) when (IsUniqueViolation(problem)) { return DuplicateName(); }
            return Results.Created($"/api/organizations/{organizationId}/hodograms/{hodogram.Id}",
                await service.GetAsync(organizationId, hodogram.Id, cancellationToken));
        });

        group.MapGet("/{hodogramId:guid}", async (Guid organizationId, Guid hodogramId, TenantAccess access,
            HodogramService service, CancellationToken cancellationToken) =>
        {
            if (!await access.CanReadOrganizationAsync(organizationId, cancellationToken)) return Results.NotFound();
            var detail = await service.GetAsync(organizationId, hodogramId, cancellationToken);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        group.MapPut("/{hodogramId:guid}", (Guid organizationId, Guid hodogramId, UpdateHodogramRequest request,
            TenantAccess access, HodogramService service, ZevDbContext database, CancellationToken cancellationToken) =>
            WriteAsync(organizationId, hodogramId, request.Revision, access, service, database, async hodogram =>
            {
                if (!AdminValidation.Required(request.Name, 200) || request.Description?.Trim().Length > 2000)
                    return Results.BadRequest();
                var name = request.Name.Trim();
                if (await database.Hodograms.AnyAsync(item => item.OrganizationId == organizationId
                    && item.Name == name && item.Id != hodogramId, cancellationToken)) return DuplicateName();
                hodogram.Name = name;
                hodogram.Description = AdminValidation.Clean(request.Description);
                hodogram.IsActive = request.IsActive;
                return null;
            }, cancellationToken));

        group.MapPost("/{hodogramId:guid}/phases", (Guid organizationId, Guid hodogramId,
            CreateHodogramPhaseRequest request, TenantAccess access, HodogramService service, ZevDbContext database,
            CancellationToken cancellationToken) =>
            WriteAsync(organizationId, hodogramId, request.Revision, access, service, database, async hodogram =>
            {
                if (!AdminValidation.Required(request.Name, 200) || !ValidOrder(request.SortOrder))
                    return Results.BadRequest();
                database.HodogramPhases.Add(new HodogramPhase { OrganizationId = organizationId, HodogramId = hodogramId,
                    Name = request.Name.Trim(),
                    SortOrder = request.SortOrder ?? await service.NextPhaseOrderAsync(hodogramId, cancellationToken) });
                return null;
            }, cancellationToken));

        group.MapPut("/{hodogramId:guid}/phases/{phaseId:guid}", (Guid organizationId, Guid hodogramId, Guid phaseId,
            UpdateHodogramPhaseRequest request, TenantAccess access, HodogramService service, ZevDbContext database,
            CancellationToken cancellationToken) =>
            WriteAsync(organizationId, hodogramId, request.Revision, access, service, database, async hodogram =>
            {
                if (!AdminValidation.Required(request.Name, 200) || !ValidOrder(request.SortOrder))
                    return Results.BadRequest();
                var phase = await database.HodogramPhases.SingleOrDefaultAsync(item => item.Id == phaseId
                    && item.HodogramId == hodogramId, cancellationToken);
                if (phase is null) return Results.NotFound();
                phase.Name = request.Name.Trim();
                phase.SortOrder = request.SortOrder;
                return null;
            }, cancellationToken));

        group.MapDelete("/{hodogramId:guid}/phases/{phaseId:guid}", (Guid organizationId, Guid hodogramId, Guid phaseId,
            int revision, TenantAccess access, HodogramService service, ZevDbContext database,
            CancellationToken cancellationToken) =>
            WriteAsync(organizationId, hodogramId, revision, access, service, database, async hodogram =>
            {
                var phase = await database.HodogramPhases.SingleOrDefaultAsync(item => item.Id == phaseId
                    && item.HodogramId == hodogramId, cancellationToken);
                if (phase is null) return Results.NotFound();
                if (await database.HodogramSteps.AnyAsync(item => item.PhaseId == phaseId, cancellationToken))
                    return Results.Conflict(new { error = "Faza sadrži korake. Prvo uklonite ili premjestite korake." });
                database.HodogramPhases.Remove(phase);
                return null;
            }, cancellationToken));

        group.MapPost("/{hodogramId:guid}/phases/{phaseId:guid}/steps", (Guid organizationId, Guid hodogramId,
            Guid phaseId, CreateHodogramStepRequest request, TenantAccess access, HodogramService service,
            ZevDbContext database, CancellationToken cancellationToken) =>
            WriteAsync(organizationId, hodogramId, request.Revision, access, service, database, async hodogram =>
            {
                if (!AdminValidation.Required(request.Name, 300) || request.Description?.Trim().Length > 2000
                    || !ValidOrder(request.SortOrder)) return Results.BadRequest();
                if (!await database.HodogramPhases.AnyAsync(item => item.Id == phaseId
                    && item.HodogramId == hodogramId, cancellationToken)) return Results.NotFound();
                database.HodogramSteps.Add(new HodogramStep { OrganizationId = organizationId, HodogramId = hodogramId,
                    PhaseId = phaseId, Name = request.Name.Trim(), Description = AdminValidation.Clean(request.Description),
                    SortOrder = request.SortOrder ?? await service.NextStepOrderAsync(phaseId, cancellationToken) });
                return null;
            }, cancellationToken));

        group.MapPut("/{hodogramId:guid}/steps/{stepId:guid}", (Guid organizationId, Guid hodogramId, Guid stepId,
            UpdateHodogramStepRequest request, TenantAccess access, HodogramService service, ZevDbContext database,
            CancellationToken cancellationToken) =>
            WriteAsync(organizationId, hodogramId, request.Revision, access, service, database, async hodogram =>
            {
                if (!AdminValidation.Required(request.Name, 300) || request.Description?.Trim().Length > 2000
                    || !ValidOrder(request.SortOrder)) return Results.BadRequest();
                var step = await database.HodogramSteps.SingleOrDefaultAsync(item => item.Id == stepId
                    && item.HodogramId == hodogramId, cancellationToken);
                if (step is null || !await database.HodogramPhases.AnyAsync(item => item.Id == request.PhaseId
                    && item.HodogramId == hodogramId, cancellationToken)) return Results.NotFound();
                step.Name = request.Name.Trim();
                step.Description = AdminValidation.Clean(request.Description);
                step.SortOrder = request.SortOrder;
                step.PhaseId = request.PhaseId;
                return null;
            }, cancellationToken));

        group.MapDelete("/{hodogramId:guid}/steps/{stepId:guid}", (Guid organizationId, Guid hodogramId, Guid stepId,
            int revision, TenantAccess access, HodogramService service, ZevDbContext database,
            CancellationToken cancellationToken) =>
            WriteAsync(organizationId, hodogramId, revision, access, service, database, async hodogram =>
            {
                var step = await database.HodogramSteps.SingleOrDefaultAsync(item => item.Id == stepId
                    && item.HodogramId == hodogramId, cancellationToken);
                if (step is null) return Results.NotFound();
                database.HodogramAssignments.RemoveRange(await database.HodogramAssignments
                    .Where(item => item.StepId == stepId).ToListAsync(cancellationToken));
                database.HodogramSteps.Remove(step);
                return null;
            }, cancellationToken));

        group.MapPut("/{hodogramId:guid}/matrix", (Guid organizationId, Guid hodogramId,
            SaveHodogramMatrixRequest request, TenantAccess access, HodogramService service, ZevDbContext database,
            CancellationToken cancellationToken) =>
            WriteAsync(organizationId, hodogramId, request.Revision, access, service, database, async hodogram =>
            {
                if (request.Cells is null) return Results.BadRequest();
                var result = await service.ApplyMatrixAsync(hodogram, request.Cells, cancellationToken);
                return result.Outcome switch
                {
                    MatrixSaveOutcome.Saved => null,
                    MatrixSaveOutcome.Invalid => Results.UnprocessableEntity(new
                    {
                        error = "RACI matrica nije ispravna: svaki korak mora imati najmanje jedan R i tačno jedan A.",
                        issues = result.Issues
                    }),
                    _ => Results.BadRequest()
                };
            }, cancellationToken));

        return api;
    }

    private static async Task<IResult> WriteAsync(Guid organizationId, Guid hodogramId, int revision,
        TenantAccess access, HodogramService service, ZevDbContext database, Func<Hodogram, Task<IResult?>> change,
        CancellationToken cancellationToken)
    {
        if (!await access.CanReadOrganizationAsync(organizationId, cancellationToken)) return Results.NotFound();
        var (hodogram, stale) = await service.BeginUpdateAsync(organizationId, hodogramId, revision, cancellationToken);
        if (hodogram is null) return Results.NotFound();
        if (stale) return Results.Conflict();
        var problem = await change(hodogram);
        if (problem is not null) return problem;
        try { await database.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { return Results.Conflict(); }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // A concurrent writer can hit the cell index before the revision check; only the name index is a naming conflict.
            return exception.InnerException!.Message.Contains("IX_Hodograms_OrganizationId_Name", StringComparison.Ordinal)
                ? DuplicateName() : Results.Conflict();
        }
        return Results.Ok(await service.GetAsync(organizationId, hodogramId, cancellationToken));
    }

    private static bool ValidOrder(int? sortOrder) => sortOrder is null or (>= 0 and <= MaxSortOrder);

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: 2601 or 2627 };

    private static IResult DuplicateName() =>
        Results.Conflict(new { error = "Hodogram s ovim nazivom već postoji u organizaciji." });
}

public sealed record CreateHodogramRequest(string Name, string? Description);
public sealed record UpdateHodogramRequest(string Name, string? Description, bool IsActive, int Revision);
public sealed record CreateHodogramPhaseRequest(string Name, int? SortOrder, int Revision);
public sealed record UpdateHodogramPhaseRequest(string Name, int SortOrder, int Revision);
public sealed record CreateHodogramStepRequest(string Name, string? Description, int? SortOrder, int Revision);
public sealed record UpdateHodogramStepRequest(string Name, string? Description, int SortOrder, Guid PhaseId, int Revision);
public sealed record SaveHodogramMatrixRequest(int Revision, List<HodogramMatrixCell>? Cells);
