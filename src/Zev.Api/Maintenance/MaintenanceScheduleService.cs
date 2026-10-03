using Microsoft.EntityFrameworkCore;
using Zev.Api.Data;
using Zev.Api.Domain;

namespace Zev.Api.Maintenance;

public sealed class MaintenanceScheduleService(ZevDbContext database)
{
    public async Task<int> EnsureUntilAsync(Guid zevId, Guid planId, DateOnly through,
        CancellationToken cancellationToken)
    {
        var plan = await database.MaintenancePlans.AsNoTracking().SingleOrDefaultAsync(item => item.Id == planId
            && item.ZevId == zevId && item.IsActive, cancellationToken);
        if (plan is null || plan.FrequencyDays <= 0 || through < plan.FirstDueOn
            || through.DayNumber - plan.FirstDueOn.DayNumber > 3660)
            return 0;
        var existing = await database.ScheduledMaintenances.AsNoTracking().Where(item => item.PlanId == planId)
            .Select(item => item.DueOn).ToListAsync(cancellationToken);
        var existingDates = existing.ToHashSet();
        var created = 0;
        for (var due = plan.FirstDueOn; due <= through; due = due.AddDays(plan.FrequencyDays))
        {
            if (existingDates.Contains(due)) continue;
            database.ScheduledMaintenances.Add(new ScheduledMaintenance
            {
                OrganizationId = plan.OrganizationId, ZevId = zevId, PlanId = planId, DueOn = due
            });
            created++;
        }
        if (created > 0) await database.SaveChangesAsync(cancellationToken);
        return created;
    }
}
