using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Maintenance;
using Zev.Api.RecycleBin;
using Zev.Api.Security;

namespace Zev.Api.Endpoints;

public static class MaintenanceEndpoints
{
    public static RouteGroupBuilder MapMaintenanceEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/zevs/{zevId:guid}/maintenance");
        MapPlans(group);

        group.MapGet("/tickets", async (Guid zevId, int? page, int? pageSize, TenantAccess access,
            ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (access.CurrentUserId is not Guid userId) return Results.NotFound();
            if (await access.RequirePermissionAsync(zevId, "Maintenance.View", cancellationToken, staffOnly: false) is { } denied) return denied;
            var canManage = await access.HasPermissionAsync(zevId, "Maintenance.View", cancellationToken);
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.MaintenanceTickets.AsNoTracking().Where(item => item.ZevId == zevId);
            if (!canManage) query = query.Where(item => item.ReportedByUserId == userId);
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderByDescending(item => item.CreatedUtc).Skip((currentPage - 1) * currentSize)
                .Take(currentSize).Select(item => new { item.Id, item.PropertyUnitId, item.Category,
                    item.Description, item.Location, priority = item.Priority.ToString(),
                    status = item.Status.ToString(), item.CreatedUtc, item.ReportedByUserId,
                    rowVersion = Convert.ToBase64String(item.RowVersion) }).ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount, items });
        });

        group.MapPost("/tickets", async (Guid zevId, CreateMaintenanceTicketRequest request,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (access.CurrentUserId is not Guid userId) return Results.NotFound();
            if (await access.RequirePermissionAsync(zevId, "Maintenance.Create", cancellationToken, staffOnly: false)
                is { } denied) return denied;
            // A unit the caller may not report for is not revealed (404), like other people's records.
            if (!await access.CanReportTicketAsync(zevId, request.PropertyUnitId, cancellationToken)) return Results.NotFound();
            if (!AdminValidation.Required(request.Category, 100) || !AdminValidation.Required(request.Description, 4000)
                || !AdminValidation.Required(request.Location, 250)
                || !Enum.TryParse<MaintenancePriority>(request.Priority, true, out var priority)
                || !Enum.IsDefined(priority) || request.FileIds?.Distinct().Count() != request.FileIds?.Count)
                return Results.BadRequest();
            var organizationId = await database.Zevs.Where(item => item.Id == zevId)
                .Select(item => item.OrganizationId).SingleAsync(cancellationToken);
            if (request.PropertyUnitId is Guid unitId && !await database.PropertyUnits.AnyAsync(item => item.Id == unitId
                && item.ZevId == zevId && item.IsActive, cancellationToken)) return Results.NotFound();
            var fileIds = request.FileIds ?? [];
            if (fileIds.Count > 10 || await database.StoredFiles.CountAsync(item => fileIds.Contains(item.Id)
                && item.ZevId == zevId && item.UploadedByUserId == userId, cancellationToken) != fileIds.Count)
                return Results.BadRequest();
            var ticket = new MaintenanceTicket { OrganizationId = organizationId, ZevId = zevId,
                PropertyUnitId = request.PropertyUnitId, ReportedByUserId = userId,
                Category = request.Category.Trim(), Description = request.Description.Trim(),
                Location = request.Location.Trim(), Priority = priority };
            database.MaintenanceTickets.Add(ticket);
            database.MaintenanceStatusEvents.Add(new MaintenanceStatusEvent
            {
                OrganizationId = organizationId, ZevId = zevId, TicketId = ticket.Id,
                Status = MaintenanceStatus.Open, ActorUserId = userId
            });
            database.MaintenanceTicketFiles.AddRange(fileIds.Select(fileId => new MaintenanceTicketFile
            {
                OrganizationId = organizationId, ZevId = zevId, TicketId = ticket.Id, StoredFileId = fileId
            }));
            await database.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/zevs/{zevId}/maintenance/tickets/{ticket.Id}", new { ticket.Id });
        });

        group.MapPatch("/tickets/{ticketId:guid}/status", async (Guid zevId, Guid ticketId,
            ChangeMaintenanceStatusRequest request, TenantAccess access, ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse<MaintenanceStatus>(request.Status, true, out var next) || !Enum.IsDefined(next)
                || !AdminValidation.TryRowVersion(request.RowVersion, out var rowVersion)) return Results.BadRequest();
            var ticket = await database.MaintenanceTickets.SingleOrDefaultAsync(item => item.Id == ticketId
                && item.ZevId == zevId, cancellationToken);
            if (ticket is null || access.CurrentUserId is not Guid userId) return Results.NotFound();
            var canManage = await access.HasPermissionAsync(zevId, "Maintenance.Edit", cancellationToken)
                || await access.HasPermissionAsync(zevId, "Maintenance.Approve", cancellationToken);
            if (!canManage && !(ticket.ReportedByUserId == userId && ticket.Status == MaintenanceStatus.Completed
                && next == MaintenanceStatus.Verified && await access.CanReadZevAsync(zevId, cancellationToken)))
                return Results.NotFound();
            if (!MaintenanceWorkflow.CanTransition(ticket.Status, next)) return Results.Conflict();
            database.Entry(ticket).Property(item => item.RowVersion).OriginalValue = rowVersion;
            ticket.Status = next;
            database.MaintenanceStatusEvents.Add(new MaintenanceStatusEvent
            {
                OrganizationId = ticket.OrganizationId, ZevId = zevId, TicketId = ticketId,
                Status = next, ActorUserId = userId
            });
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return Results.Ok(new { ticket.Id, status = ticket.Status.ToString(),
                    rowVersion = Convert.ToBase64String(ticket.RowVersion) });
            }
            catch (DbUpdateConcurrencyException) { return Results.Conflict(); }
        });

        group.MapGet("/tickets/{ticketId:guid}/history", async (Guid zevId, Guid ticketId,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            var ticket = await database.MaintenanceTickets.AsNoTracking().SingleOrDefaultAsync(item => item.Id == ticketId
                && item.ZevId == zevId, cancellationToken);
            if (ticket is null || access.CurrentUserId is not Guid userId ||
                (!await access.HasPermissionAsync(zevId, "Maintenance.View", cancellationToken)
                    && !(ticket.ReportedByUserId == userId && await access.CanReadZevAsync(zevId, cancellationToken))))
                return Results.NotFound();
            var history = await database.MaintenanceStatusEvents.AsNoTracking().Where(item => item.TicketId == ticketId)
                .OrderBy(item => item.ChangedUtc).Select(item => new { item.Id, status = item.Status.ToString(),
                    item.ActorUserId, item.ChangedUtc }).ToListAsync(cancellationToken);
            return Results.Ok(history);
        });

        group.MapGet("/work-orders", async (Guid zevId, int? page, int? pageSize,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Maintenance.View", cancellationToken) is { } denied) return denied;
            var (currentPage, currentSize) = AdminValidation.Page(page, pageSize);
            var query = database.WorkOrders.AsNoTracking().Where(item => item.ZevId == zevId);
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderByDescending(item => item.CreatedUtc).Skip((currentPage - 1) * currentSize)
                .Take(currentSize).Select(item => new { item.Id, item.TicketId, item.SupplierId, item.Title,
                    item.Description, priority = item.Priority.ToString(), status = item.Status.ToString(),
                    item.CostAmount, item.DueOn, rowVersion = Convert.ToBase64String(item.RowVersion) })
                .ToListAsync(cancellationToken);
            return Results.Ok(new { page = currentPage, pageSize = currentSize, totalCount, items });
        });

        group.MapPost("/work-orders", async (Guid zevId, CreateWorkOrderRequest request,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (access.CurrentUserId is not Guid userId) return Results.NotFound();
            if (await access.RequirePermissionAsync(zevId, "Maintenance.Assign", cancellationToken) is { } denied) return denied;
            if (!AdminValidation.Required(request.Title, 200) || !AdminValidation.Required(request.Description, 4000)
                || request.CostAmount < 0m || !Enum.TryParse<MaintenancePriority>(request.Priority, true, out var priority)
                || !Enum.IsDefined(priority)) return Results.BadRequest();
            var organizationId = await database.Zevs.Where(item => item.Id == zevId)
                .Select(item => item.OrganizationId).SingleAsync(cancellationToken);
            if ((request.TicketId is Guid ticketId && !await database.MaintenanceTickets.AnyAsync(item => item.Id == ticketId
                    && item.ZevId == zevId, cancellationToken))
                || (request.SupplierId is Guid supplierId && !await database.Suppliers.AnyAsync(item => item.Id == supplierId
                    && item.OrganizationId == organizationId && item.IsActive, cancellationToken))) return Results.NotFound();
            var order = new WorkOrder { OrganizationId = organizationId, ZevId = zevId,
                TicketId = request.TicketId, SupplierId = request.SupplierId, Title = request.Title.Trim(),
                Description = request.Description.Trim(), Priority = priority, CostAmount = request.CostAmount,
                DueOn = request.DueOn };
            database.WorkOrders.Add(order);
            database.MaintenanceStatusEvents.Add(new MaintenanceStatusEvent
            {
                OrganizationId = organizationId, ZevId = zevId, WorkOrderId = order.Id,
                Status = MaintenanceStatus.Open, ActorUserId = userId
            });
            await database.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/zevs/{zevId}/maintenance/work-orders/{order.Id}", new { order.Id });
        });

        group.MapPatch("/work-orders/{orderId:guid}/status", async (Guid zevId, Guid orderId,
            ChangeMaintenanceStatusRequest request, TenantAccess access, ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (access.CurrentUserId is not Guid userId) return Results.NotFound();
            if (await access.RequirePermissionAsync(zevId, "Maintenance.Edit", cancellationToken) is { } denied) return denied;
            if (!Enum.TryParse<MaintenanceStatus>(request.Status, true, out var next) || !Enum.IsDefined(next)
                || !AdminValidation.TryRowVersion(request.RowVersion, out var rowVersion)) return Results.BadRequest();
            var order = await database.WorkOrders.SingleOrDefaultAsync(item => item.Id == orderId
                && item.ZevId == zevId, cancellationToken);
            if (order is null) return Results.NotFound();
            if (!MaintenanceWorkflow.CanTransition(order.Status, next)) return Results.Conflict();
            database.Entry(order).Property(item => item.RowVersion).OriginalValue = rowVersion;
            order.Status = next;
            database.MaintenanceStatusEvents.Add(new MaintenanceStatusEvent
            {
                OrganizationId = order.OrganizationId, ZevId = zevId, WorkOrderId = orderId,
                Status = next, ActorUserId = userId
            });
            var linked = await database.ScheduledMaintenances.Where(item => item.ZevId == zevId && item.WorkOrderId == orderId
                && item.Status != ScheduleStatus.Cancelled).ToListAsync(cancellationToken);
            foreach (var schedule in linked)
            {
                schedule.Status = next switch
                {
                    MaintenanceStatus.Completed or MaintenanceStatus.Verified => ScheduleStatus.Completed,
                    MaintenanceStatus.Cancelled => ScheduleStatus.Due,
                    _ => ScheduleStatus.InProgress
                };
                if (next == MaintenanceStatus.Cancelled) schedule.WorkOrderId = null;
            }
            try
            {
                await database.SaveChangesAsync(cancellationToken);
                return Results.Ok(new { order.Id, status = order.Status.ToString(),
                    rowVersion = Convert.ToBase64String(order.RowVersion) });
            }
            catch (DbUpdateConcurrencyException) { return Results.Conflict(); }
        });

        group.MapPost("/work-orders/{orderId:guid}/files", async (Guid zevId, Guid orderId,
            LinkWorkOrderFileRequest request, TenantAccess access, ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Maintenance.Edit", cancellationToken) is { } denied) return denied;
            var order = await database.WorkOrders.AsNoTracking().SingleOrDefaultAsync(item => item.Id == orderId
                && item.ZevId == zevId, cancellationToken);
            if (order is null || !await database.StoredFiles.AnyAsync(item => item.Id == request.FileId
                && item.ZevId == zevId, cancellationToken)) return Results.NotFound();
            var link = new WorkOrderFile { OrganizationId = order.OrganizationId, ZevId = zevId,
                WorkOrderId = orderId, StoredFileId = request.FileId };
            database.WorkOrderFiles.Add(link);
            try { await database.SaveChangesAsync(cancellationToken); return Results.Ok(new { link.Id }); }
            catch (DbUpdateException) { return Results.Conflict(); }
        });

        group.MapGet("/work-orders/{orderId:guid}/history", async (Guid zevId, Guid orderId,
            TenantAccess access, ZevDbContext database, CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Maintenance.View", cancellationToken) is { } denied) return denied;
            if (!await database.WorkOrders.AnyAsync(item => item.Id == orderId && item.ZevId == zevId,
                    cancellationToken)) return Results.NotFound();
            var history = await database.MaintenanceStatusEvents.AsNoTracking().Where(item => item.WorkOrderId == orderId)
                .OrderBy(item => item.ChangedUtc).Select(item => new { item.Id,
                    status = item.Status.ToString(), item.ActorUserId, item.ChangedUtc }).ToListAsync(cancellationToken);
            return Results.Ok(history);
        });

        return api;
    }

    private static void MapPlans(RouteGroupBuilder group)
    {
        group.MapGet("/plans", async (Guid zevId, TenantAccess access, ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Maintenance.View", cancellationToken) is { } denied) return denied;
            var plans = await database.MaintenancePlans.AsNoTracking().Where(item => item.ZevId == zevId)
                .OrderBy(item => item.Title).Select(item => new { item.Id, item.Title, item.Category,
                    item.FrequencyDays, item.FirstDueOn, item.IsActive }).ToListAsync(cancellationToken);
            return Results.Ok(plans);
        });

        group.MapPost("/plans", async (Guid zevId, CreateMaintenancePlanRequest request, TenantAccess access,
            ZevDbContext database, MaintenanceScheduleService scheduleService, CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Maintenance.Create", cancellationToken) is { } denied) return denied;
            if (!AdminValidation.Required(request.Title, 200) || !AdminValidation.Required(request.Category, 100)
                || request.FrequencyDays is < 1 or > 3660 || request.FirstDueOn == default) return Results.BadRequest();
            var organizationId = await database.Zevs.Where(item => item.Id == zevId)
                .Select(item => item.OrganizationId).SingleAsync(cancellationToken);
            var plan = new MaintenancePlan { OrganizationId = organizationId, ZevId = zevId,
                Title = request.Title.Trim(), Category = request.Category.Trim(),
                FrequencyDays = request.FrequencyDays, FirstDueOn = request.FirstDueOn };
            database.MaintenancePlans.Add(plan);
            await database.SaveChangesAsync(cancellationToken);
            await scheduleService.EnsureUntilAsync(zevId, plan.Id, MaintenanceScheduleService.Today()
                .AddDays(MaintenanceScheduleService.HorizonDays), cancellationToken);
            return Results.Created($"/api/zevs/{zevId}/maintenance/plans/{plan.Id}", new { plan.Id });
        });

        // Delete only a plan nobody has worked on; its open occurrences go with it. A plan with history is stopped instead.
        group.MapDelete("/plans/{planId:guid}", async (Guid zevId, Guid planId, TenantAccess access, ZevDbContext database,
            MaintenanceScheduleService scheduleService, SoftDeleteAdminService recycleBin, CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Maintenance.Delete", cancellationToken) is { } denied) return denied;
            var plan = await database.MaintenancePlans.SingleOrDefaultAsync(item => item.Id == planId && item.ZevId == zevId,
                cancellationToken);
            if (plan is null) return Results.NotFound();
            if (await scheduleService.HasRealizedWorkAsync(planId, cancellationToken))
                return Results.Conflict(new { error = MaintenanceScheduleService.RealizedWorkMessage });
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            await scheduleService.ClearOpenOccurrencesAsync(planId, cancelOverdue: false, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            var result = await recycleBin.DeleteAsync(plan, access.CurrentUserId, cancellationToken);
            if (result.Outcome == SoftDeleteOutcome.Done) await transaction.CommitAsync(cancellationToken);
            return result.ToHttp();
        });

        // Stop a plan (keeps its history; open occurrences are removed, overdue ones cancelled) or start it again.
        group.MapPut("/plans/{planId:guid}/toggle-active", async (Guid zevId, Guid planId, TenantAccess access,
            ZevDbContext database, MaintenanceScheduleService scheduleService, CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Maintenance.Edit", cancellationToken) is { } denied) return denied;
            var plan = await database.MaintenancePlans.SingleOrDefaultAsync(item => item.Id == planId && item.ZevId == zevId,
                cancellationToken);
            if (plan is null) return Results.NotFound();
            plan.IsActive = !plan.IsActive;
            if (!plan.IsActive) await scheduleService.ClearOpenOccurrencesAsync(planId, cancelOverdue: true, cancellationToken);
            await database.SaveChangesAsync(cancellationToken);
            if (plan.IsActive)
                await scheduleService.EnsureUntilAsync(zevId, planId, MaintenanceScheduleService.Today()
                    .AddDays(MaintenanceScheduleService.HorizonDays), cancellationToken);
            return Results.Ok(new { plan.Id, plan.IsActive });
        });

        group.MapPost("/plans/{planId:guid}/generate", async (Guid zevId, Guid planId,
            GenerateScheduleRequest request, TenantAccess access, MaintenanceScheduleService service,
            CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Maintenance.Create", cancellationToken) is { } denied) return denied;
            var created = await service.EnsureUntilAsync(zevId, planId, request.Through, cancellationToken);
            return Results.Ok(new { created });
        });

        // Default view: everything still open (also overdue from earlier dates) plus the next HorizonDays days.
        group.MapGet("/calendar", async (Guid zevId, DateOnly? from, DateOnly? to,
            TenantAccess access, ZevDbContext database, MaintenanceScheduleService scheduleService,
            CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Maintenance.View", cancellationToken) is { } denied) return denied;
            var today = MaintenanceScheduleService.Today();
            var end = to ?? today.AddDays(MaintenanceScheduleService.HorizonDays);
            if (from is DateOnly first && (end < first || end.DayNumber - first.DayNumber > 366)) return Results.BadRequest();
            await scheduleService.EnsureHorizonAsync(zevId, cancellationToken);
            var query = database.ScheduledMaintenances.AsNoTracking().Where(item => item.ZevId == zevId && item.DueOn <= end);
            query = from is DateOnly start ? query.Where(item => item.DueOn >= start)
                : query.Where(item => item.DueOn >= today || item.Status == ScheduleStatus.Due || item.Status == ScheduleStatus.InProgress);
            var items = await query.OrderBy(item => item.DueOn).ThenBy(item => item.Plan.Title)
                .Select(item => new { item.Id, item.PlanId, title = item.Plan.Title, category = item.Plan.Category,
                    item.DueOn, item.Status, item.WorkOrderId })
                .ToListAsync(cancellationToken);
            return Results.Ok(items.Select(item => new { item.Id, item.PlanId, item.title, item.category, item.DueOn,
                status = MaintenanceScheduleService.EffectiveStatus(item.Status, item.DueOn, today), item.WorkOrderId }));
        });

        group.MapPatch("/calendar/{scheduleId:guid}/status", async (Guid zevId, Guid scheduleId,
            ChangeScheduleStatusRequest request, TenantAccess access, ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Maintenance.Edit", cancellationToken) is { } denied) return denied;
            if (!Enum.TryParse<ScheduleStatus>(request.Status, true, out var status) || !Enum.IsDefined(status))
                return Results.BadRequest();
            var schedule = await database.ScheduledMaintenances.SingleOrDefaultAsync(item => item.Id == scheduleId
                && item.ZevId == zevId, cancellationToken);
            if (schedule is null) return Results.NotFound();
            if (schedule.Status is ScheduleStatus.Completed or ScheduleStatus.Cancelled) return Results.Conflict();
            if (request.WorkOrderId is Guid orderId && !await database.WorkOrders.AnyAsync(item => item.Id == orderId
                && item.ZevId == zevId, cancellationToken)) return Results.NotFound();
            schedule.Status = status;
            schedule.WorkOrderId = request.WorkOrderId;
            await database.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { schedule.Id, status = schedule.Status.ToString() });
        });
    }
}

public sealed record CreateMaintenanceTicketRequest(Guid? PropertyUnitId, string Category,
    string Description, string Location, string Priority, List<Guid>? FileIds);
public sealed record ChangeMaintenanceStatusRequest(string Status, string RowVersion);
public sealed record CreateWorkOrderRequest(Guid? TicketId, Guid? SupplierId, string Title,
    string Description, string Priority, decimal? CostAmount, DateOnly? DueOn);
public sealed record LinkWorkOrderFileRequest(Guid FileId);
public sealed record CreateMaintenancePlanRequest(string Title, string Category, int FrequencyDays, DateOnly FirstDueOn);
public sealed record GenerateScheduleRequest(DateOnly Through);
public sealed record ChangeScheduleStatusRequest(string Status, Guid? WorkOrderId);
