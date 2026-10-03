using Zev.Api.Domain;

namespace Zev.Api.Security;

public sealed record PermissionModule(string Name, string Label, string[] Actions, string[] Details);

public static class GranularPermissions
{
    public static readonly PermissionModule[] Modules =
    [
        new("Overview", "Pregled", ["View"], ["Access.Manage"]),
        new("Finance", "Finansije", ["View", "Create", "Edit", "Approve", "Export"],
            ["Billing.View", "Billing.Preview", "Billing.Post", "Billing.Reverse", "Funds.View", "Funds.Create", "Funds.Edit",
                "Invoice.View", "Invoice.Generate", "Invoice.Export", "Invoice.Correct", "OwnerLedger.View", "OwnerLedger.Export"]),
        new("Payments", "Uplate i izvodi", ["View", "Create", "Edit", "Approve", "Export"],
            ["BankAccounts.View", "BankAccounts.Create", "BankAccounts.Edit", "BankAccounts.Deactivate",
                "BankStatements.View", "BankStatements.Preview", "BankStatements.Import", "BankStatements.Post",
                "Payments.ReconcileAutomatic", "Payments.ReconcileManual", "Payments.Reverse",
                "Payments.CreateManual", "Payments.ReverseAllocation", "Payments.ChangePayer"]),
        new("Expenses", "Rashodi i dugovanja", ["View", "Create", "Edit", "Approve", "Export"],
            ["Expenses.Submit", "Expenses.Post", "Expenses.Reverse"]),
        new("Reports", "Izvještaji", ["View", "Export"],
            ["Reports.Finance", "Reports.Debtors", "Reports.Maintenance", "Reports.Registry"]),
        new("Legal", "Pravni predmeti", ["View", "Create", "Edit", "Export"],
            ["Legal.ChangeStatus", "Legal.Documents"]),
        new("Registry", "Vlasnici i nekretnine", ["View", "Create", "Edit", "Export"],
            ["Buildings.Create", "Buildings.Edit", "Entrances.Create", "Entrances.Edit",
                "PropertyUnits.View", "PropertyUnits.Create", "PropertyUnits.Edit", "Persons.View",
                "Persons.Create", "Persons.Edit", "Ownership.View", "Ownership.Create", "Ownership.Close"]),
        new("Maintenance", "Održavanje", ["View", "Create", "Edit", "Approve", "Export"],
            ["Maintenance.Assign", "Maintenance.Complete"]),
        new("Community", "Zajednica", ["View", "Create", "Edit", "Approve", "Export"],
            ["Announcements.Create", "Announcements.Edit", "Documents.View", "Documents.Upload",
                "Documents.Edit", "Documents.Download", "Documents.Archive", "Voting.View", "Voting.Create", "Voting.Manage"])
    ];

    public static readonly string[] Keys = Modules.SelectMany(module => module.Actions.Select(action => $"{module.Name}.{action}")
        .Concat(module.Details)).Distinct(StringComparer.Ordinal).ToArray();

    public static bool IsKnown(string key) => Keys.Contains(key, StringComparer.Ordinal);

    public static bool RoleDefault(MembershipRole role, string key)
    {
        if (!IsKnown(key)) return false;
        if (key == "Payments.ChangePayer") return role == MembershipRole.OrganizationAdministrator;
        if (role == MembershipRole.OrganizationAdministrator || role == MembershipRole.Manager) return true;
        if (role == MembershipRole.Accountant) return key.StartsWith("Finance.", StringComparison.Ordinal)
            || key.StartsWith("Billing.", StringComparison.Ordinal) || key.StartsWith("Funds.", StringComparison.Ordinal)
            || key.StartsWith("Invoice.", StringComparison.Ordinal) || key.StartsWith("Payments.", StringComparison.Ordinal)
            || key.StartsWith("Bank", StringComparison.Ordinal) || key.StartsWith("Reports.", StringComparison.Ordinal)
            || key.StartsWith("OwnerLedger.", StringComparison.Ordinal) || key == "Overview.View";
        if (role == MembershipRole.BuildingRepresentative) return key == "Overview.View"
            || key is "Maintenance.View" or "Maintenance.Approve"
            || key == "Community.View" || key == "Documents.View" || key == "Documents.Download"
            || key == "Voting.View";
        return role == MembershipRole.Owner && (key == "Overview.View" || key == "Finance.View"
            || key == "OwnerLedger.View" || key == "Documents.View" || key == "Documents.Download"
            || key == "Voting.View" || key == "Community.View" || key == "Maintenance.View"
            || key == "Maintenance.Create");
    }
}

public sealed record PermissionState(string Key, bool RoleDefault, string? Override, bool Effective);
public sealed record PermissionContext(string[] Roles, bool IsOwner, bool IsStaff, string[] Sources,
    PermissionState[] Permissions);
