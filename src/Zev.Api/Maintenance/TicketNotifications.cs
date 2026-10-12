using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;
using Zev.Api.Notifications;

namespace Zev.Api.Maintenance;

// New fault report → e-mail to the platform's notification address (Sistemska podešavanja → DefaultAdminEmail).
public sealed class TicketNotifications(ZevDbContext database, EmailQueue queue, IConfiguration configuration,
    ILogger<TicketNotifications> logger)
{
    public async Task<bool> NotifyNewTicketAsync(MaintenanceTicket ticket, CancellationToken cancellationToken)
    {
        var recipient = await database.SystemSettings.AsNoTracking().Where(item => item.Id == SystemSettings.SingletonId)
            .Select(item => item.DefaultAdminEmail).SingleOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(recipient))
        {
            logger.LogInformation("No notification address in the system settings; ticket {TicketId} reported without e-mail.", ticket.Id);
            return false;
        }
        var zev = await database.Zevs.AsNoTracking().Where(item => item.Id == ticket.ZevId)
            .Select(item => new { item.Code, item.Name }).SingleAsync(cancellationToken);
        var unit = ticket.PropertyUnitId is Guid unitId
            ? await database.PropertyUnits.AsNoTracking().Where(item => item.Id == unitId).Select(item => item.Code).SingleOrDefaultAsync(cancellationToken)
            : null;
        var reporter = await database.Users.AsNoTracking().Where(item => item.Id == ticket.ReportedByUserId)
            .Select(item => item.Email).SingleOrDefaultAsync(cancellationToken);
        // The priority as named in the MAINTENANCE_PRIORITY code list (what the reporter picked in the form).
        var priorityCode = ticket.Priority.ToString();
        var priorityName = await database.LookupItems.AsNoTracking()
            .Where(item => item.LookupTypeCode == "MAINTENANCE_PRIORITY" && item.Code == priorityCode)
            .Select(item => item.Name).FirstOrDefaultAsync(cancellationToken);
        return queue.Enqueue(Compose(recipient, zev.Name, zev.Code, unit, reporter, ticket, configuration["Web:Origin"],
            priorityName));
    }

    // Bosnian priority names, as seeded in the MAINTENANCE_PRIORITY code list; used when the list has no entry.
    public static string PriorityLabel(MaintenancePriority priority) => priority switch
    {
        MaintenancePriority.Urgent => "Hitno",
        MaintenancePriority.High => "Visok",
        MaintenancePriority.Normal => "Standardni",
        MaintenancePriority.Low => "Nizak",
        _ => priority.ToString()
    };

    public static EmailMessage Compose(string to, string zevName, string zevCode, string? unitCode, string? reporter,
        MaintenanceTicket ticket, string? webOrigin, string? priorityName = null) => new(to,
        $"Nova prijava kvara – {zevName}: {ticket.Category}",
        string.Join("\n", new[]
        {
            $"Prijavljen je novi kvar u zajednici {zevName} ({zevCode}).",
            "",
            $"Kategorija: {ticket.Category}",
            $"Prioritet: {(string.IsNullOrWhiteSpace(priorityName) ? PriorityLabel(ticket.Priority) : priorityName)}",
            $"Mjesto: {(unitCode is null ? "Zajednički prostori" : $"Stan / posebni dio {unitCode}")} – {ticket.Location}",
            $"Prijavio: {reporter ?? "nepoznat korisnik"}",
            $"Vrijeme: {ticket.CreatedUtc:dd.MM.yyyy HH:mm} UTC",
            "",
            ticket.Description,
            "",
            webOrigin is null ? "Prijava je vidljiva u modulu Održavanje." : $"Prijava je vidljiva u modulu Održavanje: {webOrigin}"
        }));
}
