using Zev.Api.Domain;

namespace Zev.Api.Maintenance;

public static class MaintenanceWorkflow
{
    public static bool CanTransition(MaintenanceStatus current, MaintenanceStatus next) => (current, next) switch
    {
        (MaintenanceStatus.Open, MaintenanceStatus.InProgress or MaintenanceStatus.Cancelled) => true,
        (MaintenanceStatus.InProgress, MaintenanceStatus.Completed or MaintenanceStatus.Cancelled) => true,
        (MaintenanceStatus.Completed, MaintenanceStatus.Verified or MaintenanceStatus.InProgress) => true,
        _ => false
    };
}
