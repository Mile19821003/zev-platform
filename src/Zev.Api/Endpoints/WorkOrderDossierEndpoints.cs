using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Finance;
using Zev.Api.Maintenance;
using Zev.Api.Security;
using Zev.Api.Storage;

namespace Zev.Api.Endpoints;

public sealed record UploadWorkOrderAttachmentRequest(string FileName, string ContentType, string ContentBase64, string AttachmentType);

public sealed record CompleteWorkOrderRequest(DateOnly CompletedDate, string WorkDescription, string? InvoiceNumber,
    decimal? CostAmount, string? ExpenseFund, Guid? SupplierId, string? ContractorName, string? ContractorPhone,
    bool RecordExpense, string RowVersion);

// The work order as a dossier: contractor, finance, attachments and completion that closes the occurrence and the report.
public static class WorkOrderDossierEndpoints
{
    private static readonly Dictionary<string, string[]> AttachmentTypes = new()
    {
        ["application/pdf"] = [".pdf"], ["image/jpeg"] = [".jpg", ".jpeg"], ["image/png"] = [".png"],
    };

    public static RouteGroupBuilder MapWorkOrderDossierEndpoints(this RouteGroupBuilder api)
    {
        var group = api.MapGroup("/zevs/{zevId:guid}/maintenance");

        group.MapGet("/work-orders/{orderId:guid}", async (Guid zevId, Guid orderId, TenantAccess access, ZevDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Maintenance.View", cancellationToken) is { } denied) return denied;
            var order = await database.WorkOrders.AsNoTracking().SingleOrDefaultAsync(item => item.Id == orderId && item.ZevId == zevId,
                cancellationToken);
            if (order is null) return Results.NotFound();
            var supplier = order.SupplierId is Guid supplierId
                ? await database.Suppliers.AsNoTracking().Where(item => item.Id == supplierId).Select(item => item.Name).SingleOrDefaultAsync(cancellationToken)
                : null;
            var schedule = await database.ScheduledMaintenances.AsNoTracking().Where(item => item.WorkOrderId == orderId)
                .Select(item => new { item.Id, item.DueOn, planTitle = item.Plan.Title, category = item.Plan.Category, status = item.Status.ToString() })
                .FirstOrDefaultAsync(cancellationToken);
            var ticket = order.TicketId is Guid ticketId
                ? await database.MaintenanceTickets.AsNoTracking().Where(item => item.Id == ticketId)
                    .Select(item => new { item.Id, item.Category, item.Location, item.Description, status = item.Status.ToString() })
                    .SingleOrDefaultAsync(cancellationToken)
                : null;
            var attachments = await database.WorkOrderAttachments.AsNoTracking().Where(item => item.WorkOrderId == orderId)
                .OrderBy(item => item.UploadedAt).Select(item => new { item.Id, attachmentType = item.AttachmentType.ToString(),
                    item.FileName, item.ContentType, item.Length, item.UploadedAt, item.UploadedByUserId })
                .ToListAsync(cancellationToken);
            var history = await database.MaintenanceStatusEvents.AsNoTracking().Where(item => item.WorkOrderId == orderId)
                .OrderBy(item => item.ChangedUtc).Select(item => new { status = item.Status.ToString(), item.ChangedUtc,
                    actor = database.Users.Where(user => user.Id == item.ActorUserId).Select(user => user.Email).FirstOrDefault() })
                .ToListAsync(cancellationToken);
            return Results.Ok(new
            {
                order.Id, order.Title, order.Description, priority = order.Priority.ToString(), status = order.Status.ToString(),
                order.DueOn, order.SupplierId, supplierName = supplier, order.ContractorName, order.ContractorPhone,
                order.InvoiceNumber, order.CostAmount, expenseFund = order.ExpenseFundKind?.ToString(), order.ExpenseId,
                order.CompletedDate, order.WorkDescription, order.CreatedUtc,
                readOnly = IsClosed(order.Status), schedule, ticket, attachments, history,
                rowVersion = Convert.ToBase64String(order.RowVersion)
            });
        });

        group.MapPost("/work-orders/{orderId:guid}/attachments", async (Guid zevId, Guid orderId, UploadWorkOrderAttachmentRequest request,
            TenantAccess access, ZevDbContext database, IFileStorage storage, CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Maintenance.Edit", cancellationToken) is { } denied) return denied;
            var order = await database.WorkOrders.AsNoTracking().SingleOrDefaultAsync(item => item.Id == orderId && item.ZevId == zevId,
                cancellationToken);
            if (order is null || access.CurrentUserId is not Guid userId) return Results.NotFound();
            if (IsClosed(order.Status)) return Results.Conflict(new { error = "Završen radni nalog je arhiviran i ne mijenja se." });
            if (!Enum.TryParse<WorkOrderAttachmentType>(request.AttachmentType, true, out var type) || !Enum.IsDefined(type)
                || ValidateAttachment(request) is not { } content)
                return Results.BadRequest(new { error = "Dozvoljeni su PDF, JPG i PNG do 10 MB." });
            var key = await storage.SaveAsync(content, request.ContentType, cancellationToken);
            var attachment = new WorkOrderAttachment
            {
                OrganizationId = order.OrganizationId, ZevId = zevId, WorkOrderId = orderId, AttachmentType = type,
                FileName = Path.GetFileName(request.FileName), StorageKey = key, ContentType = request.ContentType,
                Length = content.Length, UploadedByUserId = userId
            };
            database.WorkOrderAttachments.Add(attachment);
            try { await database.SaveChangesAsync(cancellationToken); }
            catch
            {
                await storage.RemoveAsync(key, CancellationToken.None);
                throw;
            }
            return Results.Created($"/api/zevs/{zevId}/maintenance/work-orders/{orderId}/attachments/{attachment.Id}",
                new { attachment.Id, attachmentType = type.ToString(), attachment.FileName, attachment.Length });
        }).RequireRateLimiting("upload");

        group.MapGet("/work-orders/{orderId:guid}/attachments/{attachmentId:guid}", async (Guid zevId, Guid orderId, Guid attachmentId,
            TenantAccess access, ZevDbContext database, IFileStorage storage, CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Maintenance.View", cancellationToken) is { } denied) return denied;
            var attachment = await database.WorkOrderAttachments.AsNoTracking().SingleOrDefaultAsync(item => item.Id == attachmentId
                && item.WorkOrderId == orderId && item.ZevId == zevId, cancellationToken);
            if (attachment is null) return Results.NotFound();
            byte[] content;
            try { content = await storage.ReadAsync(attachment.StorageKey, cancellationToken); }
            catch (FileNotFoundException) { return Results.NotFound(); }
            return Results.File(content, attachment.ContentType, attachment.FileName);
        });

        group.MapDelete("/work-orders/{orderId:guid}/attachments/{attachmentId:guid}", async (Guid zevId, Guid orderId, Guid attachmentId,
            TenantAccess access, ZevDbContext database, IFileStorage storage, CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Maintenance.Edit", cancellationToken) is { } denied) return denied;
            var attachment = await database.WorkOrderAttachments.SingleOrDefaultAsync(item => item.Id == attachmentId
                && item.WorkOrderId == orderId && item.ZevId == zevId, cancellationToken);
            if (attachment is null) return Results.NotFound();
            var status = await database.WorkOrders.Where(item => item.Id == orderId).Select(item => item.Status).SingleAsync(cancellationToken);
            if (IsClosed(status)) return Results.Conflict(new { error = "Završen radni nalog je arhiviran i ne mijenja se." });
            database.WorkOrderAttachments.Remove(attachment);
            await database.SaveChangesAsync(cancellationToken);
            try { await storage.RemoveAsync(attachment.StorageKey, CancellationToken.None); }
            catch (IOException) { }
            return Results.NoContent();
        });

        // Completion: closes the linked calendar occurrence and fault report; optionally books the cost as an expense.
        group.MapPut("/work-orders/{orderId:guid}/complete", async (Guid zevId, Guid orderId, CompleteWorkOrderRequest request,
            TenantAccess access, ZevDbContext database, IConfiguration configuration, CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Maintenance.Edit", cancellationToken) is { } denied) return denied;
            if (access.CurrentUserId is not Guid userId) return Results.NotFound();
            FundKind? fundKind = null;
            if (request.ExpenseFund is not null)
            {
                if (!Enum.TryParse<FundKind>(request.ExpenseFund, true, out var parsedFund) || !Enum.IsDefined(parsedFund)) return Results.BadRequest();
                fundKind = parsedFund;
            }
            if (request.CompletedDate == default || request.CompletedDate > DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1)
                || !AdminValidation.Required(request.WorkDescription, 4000) || !AdminValidation.Optional(request.InvoiceNumber, 100)
                || request.CostAmount < 0m || !AdminValidation.Optional(request.ContractorName, 200)
                || !AdminValidation.Optional(request.ContractorPhone, 40) || !AdminValidation.TryRowVersion(request.RowVersion, out var rowVersion))
                return Results.BadRequest();
            var order = await database.WorkOrders.SingleOrDefaultAsync(item => item.Id == orderId && item.ZevId == zevId, cancellationToken);
            if (order is null) return Results.NotFound();
            if (order.Status is not (MaintenanceStatus.Open or MaintenanceStatus.InProgress))
                return Results.Conflict(new { error = "Radni nalog je već završen ili otkazan." });
            var supplierId = request.SupplierId ?? order.SupplierId;
            if (supplierId is Guid chosen && !await database.Suppliers.AnyAsync(item => item.Id == chosen
                && item.OrganizationId == order.OrganizationId, cancellationToken)) return Results.NotFound();

            Expense? expense = null;
            if (request.RecordExpense)
            {
                if (await access.RequirePermissionAsync(zevId, "Expenses.Create", cancellationToken) is { } noExpense) return noExpense;
                if (supplierId is null || request.CostAmount is not > 0m || string.IsNullOrWhiteSpace(request.InvoiceNumber) || fundKind is null)
                    return Results.BadRequest(new { error = "Za upis u rashode potrebni su dobavljač iz šifarnika, broj računa, iznos i fond." });
                var fund = await database.Funds.AsNoTracking().Where(item => item.ZevId == zevId && item.Kind == fundKind && item.IsActive)
                    .OrderBy(item => item.Name).FirstOrDefaultAsync(cancellationToken);
                if (fund is null) return Results.Conflict(new { error = "ZEV nema aktivan fond odabrane vrste." });
                var currency = await FundCurrency.ResolveAsync(database, zevId, fund.Id, configuration, cancellationToken);
                if (currency is null) return Results.Conflict(new { error = "Fond ima aktivne račune u više valuta; valuta rashoda nije jednoznačna." });
                var category = await database.ScheduledMaintenances.Where(item => item.WorkOrderId == orderId).Select(item => item.Plan.Category).FirstOrDefaultAsync(cancellationToken)
                    ?? (order.TicketId is Guid ticketId ? await database.MaintenanceTickets.Where(item => item.Id == ticketId).Select(item => item.Category).SingleOrDefaultAsync(cancellationToken) : null)
                    ?? "Održavanje";
                expense = new Expense
                {
                    OrganizationId = order.OrganizationId, ZevId = zevId, FundId = fund.Id, SupplierId = supplierId.Value,
                    InvoiceNumber = request.InvoiceNumber.Trim(), InvoiceDate = request.CompletedDate, Amount = request.CostAmount.Value,
                    CurrencyCode = currency, Category = category, Description = $"Radni nalog: {order.Title}"
                };
                database.Expenses.Add(expense);
            }

            database.Entry(order).Property(item => item.RowVersion).OriginalValue = rowVersion;
            order.Status = MaintenanceStatus.Completed;
            order.CompletedDate = request.CompletedDate;
            order.WorkDescription = request.WorkDescription.Trim();
            order.InvoiceNumber = AdminValidation.Clean(request.InvoiceNumber);
            order.CostAmount = request.CostAmount ?? order.CostAmount;
            order.ExpenseFundKind = fundKind;
            order.SupplierId = supplierId;
            order.ContractorName = AdminValidation.Clean(request.ContractorName) ?? order.ContractorName;
            order.ContractorPhone = AdminValidation.Clean(request.ContractorPhone) ?? order.ContractorPhone;
            order.CompletedByUserId = userId;
            order.ExpenseId = expense?.Id;
            database.MaintenanceStatusEvents.Add(new MaintenanceStatusEvent
            {
                OrganizationId = order.OrganizationId, ZevId = zevId, WorkOrderId = orderId, Status = MaintenanceStatus.Completed, ActorUserId = userId
            });
            foreach (var schedule in await database.ScheduledMaintenances.Where(item => item.WorkOrderId == orderId
                && item.Status != ScheduleStatus.Cancelled).ToListAsync(cancellationToken))
                schedule.Status = ScheduleStatus.Completed;
            if (order.TicketId is Guid linkedTicket)
            {
                var ticket = await database.MaintenanceTickets.SingleAsync(item => item.Id == linkedTicket, cancellationToken);
                if (ticket.Status is MaintenanceStatus.Open or MaintenanceStatus.InProgress)
                {
                    ticket.Status = MaintenanceStatus.Completed;
                    database.MaintenanceStatusEvents.Add(new MaintenanceStatusEvent
                    {
                        OrganizationId = order.OrganizationId, ZevId = zevId, TicketId = ticket.Id, Status = MaintenanceStatus.Completed, ActorUserId = userId
                    });
                }
            }
            try { await database.SaveChangesAsync(cancellationToken); }
            catch (DbUpdateConcurrencyException) { return Results.Conflict(new { error = "Radni nalog je u međuvremenu izmijenjen." }); }
            return Results.Ok(new { order.Id, status = order.Status.ToString(), order.ExpenseId });
        });

        // Home screen card: due occurrences, work in progress, new reports.
        group.MapGet("/dashboard-summary", async (Guid zevId, TenantAccess access, ZevDbContext database,
            MaintenanceScheduleService scheduleService, CancellationToken cancellationToken) =>
        {
            if (await access.RequirePermissionAsync(zevId, "Maintenance.View", cancellationToken) is { } denied) return denied;
            await scheduleService.EnsureHorizonAsync(zevId, cancellationToken);
            var today = MaintenanceScheduleService.Today();
            var due = await database.ScheduledMaintenances.CountAsync(item => item.ZevId == zevId && item.Status == ScheduleStatus.Due
                && item.DueOn <= today, cancellationToken);
            var inProgress = await database.WorkOrders.CountAsync(item => item.ZevId == zevId
                && (item.Status == MaintenanceStatus.Open || item.Status == MaintenanceStatus.InProgress), cancellationToken);
            var handles = await access.HasPermissionAsync(zevId, "Maintenance.Edit", cancellationToken)
                || await access.HasPermissionAsync(zevId, "Maintenance.Approve", cancellationToken);
            int? unread = handles ? await database.MaintenanceTickets.CountAsync(item => item.ZevId == zevId && !item.IsReadByAdmin, cancellationToken) : null;
            var nextDue = await database.ScheduledMaintenances.Where(item => item.ZevId == zevId && item.Status == ScheduleStatus.Due && item.DueOn > today)
                .OrderBy(item => item.DueOn).Select(item => (DateOnly?)item.DueOn).FirstOrDefaultAsync(cancellationToken);
            return Results.Ok(new { due, inProgress, unreadTickets = unread, nextDueOn = nextDue });
        });
        return api;
    }

    private static bool IsClosed(MaintenanceStatus status) =>
        status is MaintenanceStatus.Completed or MaintenanceStatus.Verified or MaintenanceStatus.Cancelled;

    // PDF / JPG / PNG up to 10 MB; name, declared type and the file's own signature must agree.
    public static byte[]? ValidateAttachment(UploadWorkOrderAttachmentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FileName) || request.FileName != Path.GetFileName(request.FileName)
            || request.FileName.Length > 255 || !AttachmentTypes.TryGetValue(request.ContentType ?? "", out var extensions)
            || !extensions.Contains(Path.GetExtension(request.FileName).ToLowerInvariant())
            || string.IsNullOrWhiteSpace(request.ContentBase64)
            || request.ContentBase64.Length > (WorkOrderAttachment.MaxBytes + 2) / 3 * 4 + 4) return null;
        byte[] content;
        try { content = Convert.FromBase64String(request.ContentBase64); }
        catch (FormatException) { return null; }
        return LocalFileStorage.ValidContent(content, request.ContentType!, WorkOrderAttachment.MaxBytes) ? content : null;
    }
}
