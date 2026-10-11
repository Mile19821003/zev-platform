using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Zev.Api.Domain;
using ZevEntity = global::Zev.Api.Domain.Zev;

namespace Zev.Api.Data;

public sealed class ZevDbContext(DbContextOptions<ZevDbContext> options, IHttpContextAccessor httpContextAccessor) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<ZevEntity> Zevs => Set<ZevEntity>();
    public DbSet<Building> Buildings => Set<Building>();
    public DbSet<Entrance> Entrances => Set<Entrance>();
    public DbSet<PropertyUnit> PropertyUnits => Set<PropertyUnit>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<Ownership> Ownerships => Set<Ownership>();
    public DbSet<Occupancy> Occupancies => Set<Occupancy>();
    public DbSet<Fund> Funds => Set<Fund>();
    public DbSet<TariffVersion> TariffVersions => Set<TariffVersion>();
    public DbSet<BillingRun> BillingRuns => Set<BillingRun>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceLine> InvoiceLines => Set<InvoiceLine>();
    public DbSet<FinancialDocument> FinancialDocuments => Set<FinancialDocument>();
    public DbSet<FinancialDocumentLine> FinancialDocumentLines => Set<FinancialDocumentLine>();
    public DbSet<Charge> Charges => Set<Charge>();
    public DbSet<FundTransaction> FundTransactions => Set<FundTransaction>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<BankStatementImport> BankStatementImports => Set<BankStatementImport>();
    public DbSet<BankTransaction> BankTransactions => Set<BankTransaction>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<PaymentAccount> PaymentAccounts => Set<PaymentAccount>();
    public DbSet<PaymentIdentificationHistory> PaymentIdentificationHistories => Set<PaymentIdentificationHistory>();
    public DbSet<ReconciliationAttempt> ReconciliationAttempts => Set<ReconciliationAttempt>();
    public DbSet<StoredFile> StoredFiles => Set<StoredFile>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<InterestRatePolicy> InterestRatePolicies => Set<InterestRatePolicy>();
    public DbSet<LegalCase> LegalCases => Set<LegalCase>();
    public DbSet<LegalCaseFile> LegalCaseFiles => Set<LegalCaseFile>();
    public DbSet<MaintenanceTicket> MaintenanceTickets => Set<MaintenanceTicket>();
    public DbSet<MaintenanceTicketFile> MaintenanceTicketFiles => Set<MaintenanceTicketFile>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<WorkOrderFile> WorkOrderFiles => Set<WorkOrderFile>();
    public DbSet<MaintenanceStatusEvent> MaintenanceStatusEvents => Set<MaintenanceStatusEvent>();
    public DbSet<MaintenancePlan> MaintenancePlans => Set<MaintenancePlan>();
    public DbSet<ScheduledMaintenance> ScheduledMaintenances => Set<ScheduledMaintenance>();
    public DbSet<Hodogram> Hodograms => Set<Hodogram>();
    public DbSet<HodogramPhase> HodogramPhases => Set<HodogramPhase>();
    public DbSet<HodogramStep> HodogramSteps => Set<HodogramStep>();
    public DbSet<HodogramAssignment> HodogramAssignments => Set<HodogramAssignment>();
    public DbSet<Announcement> Announcements => Set<Announcement>();
    public DbSet<AnnouncementFile> AnnouncementFiles => Set<AnnouncementFile>();
    public DbSet<ManagedDocument> ManagedDocuments => Set<ManagedDocument>();
    public DbSet<Ballot> Ballots => Set<Ballot>();
    public DbSet<BallotEligibility> BallotEligibilities => Set<BallotEligibility>();
    public DbSet<Vote> Votes => Set<Vote>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<AccessOverride> AccessOverrides => Set<AccessOverride>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SystemSettings> SystemSettings => Set<SystemSettings>();
    public DbSet<SystemThemeSettings> ThemeSettings => Set<SystemThemeSettings>();
    public DbSet<LookupType> LookupTypes => Set<LookupType>();
    public DbSet<LookupItem> LookupItems => Set<LookupItem>();
    public DbSet<ModuleIconSetting> ModuleIconSettings => Set<ModuleIconSetting>();
    public DbSet<SystemErrorLog> SystemErrorLogs => Set<SystemErrorLog>();
    public DbSet<AppVersionHistory> VersionHistory => Set<AppVersionHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(user => user.Id);
            entity.Property(user => user.Email).HasMaxLength(320).IsRequired();
            entity.Property(user => user.NormalizedEmail).HasMaxLength(320).IsRequired();
            entity.Property(user => user.PasswordHash).HasMaxLength(500).IsRequired();
            entity.Property(user => user.SecurityVersion).HasDefaultValue(0);
            entity.Property(user => user.MustChangePassword).HasDefaultValue(false);
            entity.Property(user => user.RowVersion).IsRowVersion();
            entity.HasIndex(user => user.NormalizedEmail).IsUnique();
        });

        modelBuilder.Entity<Organization>(entity =>
        {
            entity.ToTable("Organizations");
            entity.HasKey(organization => organization.Id);
            entity.Property(organization => organization.Code).HasMaxLength(50).IsRequired();
            entity.Property(organization => organization.Name).HasMaxLength(200).IsRequired();
            entity.Property(organization => organization.AddressLine).HasMaxLength(250);
            entity.Property(organization => organization.CityMunicipality).HasMaxLength(120);
            entity.Property(organization => organization.TaxIdentifier).HasMaxLength(32);
            entity.Property(organization => organization.ContactName).HasMaxLength(200);
            entity.Property(organization => organization.Email).HasMaxLength(320);
            entity.Property(organization => organization.Phone).HasMaxLength(40);
            entity.Property(organization => organization.RowVersion).IsRowVersion();
            entity.HasIndex(organization => organization.Code).IsUnique();
        });

        modelBuilder.Entity<ZevEntity>(entity =>
        {
            entity.ToTable("Zevs");
            entity.HasKey(zev => zev.Id);
            entity.Property(zev => zev.Code).HasMaxLength(50).IsRequired();
            entity.Property(zev => zev.Name).HasMaxLength(200).IsRequired();
            entity.Property(zev => zev.AddressLine).HasMaxLength(250);
            entity.Property(zev => zev.CityMunicipality).HasMaxLength(120);
            entity.Property(zev => zev.TaxIdentifier).HasMaxLength(32);
            entity.Property(zev => zev.PrimaryBankAccount).HasMaxLength(64);
            entity.Property(zev => zev.ReserveFundBankAccount).HasMaxLength(64);
            entity.Property(zev => zev.ContactEmail).HasMaxLength(320);
            entity.Property(zev => zev.ContactPhone).HasMaxLength(40);
            entity.Property(zev => zev.RowVersion).IsRowVersion();
            entity.HasOne(zev => zev.Organization).WithMany().HasForeignKey(zev => zev.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasAlternateKey(zev => new { zev.OrganizationId, zev.Id });
            entity.HasIndex(zev => new { zev.OrganizationId, zev.Code }).IsUnique();
        });

        modelBuilder.Entity<Building>(entity =>
        {
            entity.ToTable("Buildings");
            entity.HasKey(building => building.Id);
            entity.Property(building => building.Code).HasMaxLength(50).IsRequired();
            entity.Property(building => building.Name).HasMaxLength(200).IsRequired();
            entity.Property(building => building.AddressLine).HasMaxLength(250);
            entity.Property(building => building.RowVersion).IsRowVersion();
            entity.HasOne(building => building.Zev).WithMany()
                .HasForeignKey(building => new { building.OrganizationId, building.ZevId })
                .HasPrincipalKey(zev => new { zev.OrganizationId, zev.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasAlternateKey(building => new { building.OrganizationId, building.ZevId, building.Id });
            entity.HasIndex(building => new { building.OrganizationId, building.ZevId, building.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
            entity.HasQueryFilter(building => !building.IsDeleted);
            entity.HasIndex(building => new { building.ZevId, building.IsActive });
        });

        modelBuilder.Entity<Entrance>(entity =>
        {
            entity.ToTable("Entrances");
            entity.HasKey(entrance => entrance.Id);
            entity.Property(entrance => entrance.Code).HasMaxLength(50).IsRequired();
            entity.Property(entrance => entrance.Name).HasMaxLength(200).IsRequired();
            entity.Property(entrance => entrance.RowVersion).IsRowVersion();
            entity.HasOne(entrance => entrance.Building).WithMany()
                .HasForeignKey(entrance => new { entrance.OrganizationId, entrance.ZevId, entrance.BuildingId })
                .HasPrincipalKey(building => new { building.OrganizationId, building.ZevId, building.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasAlternateKey(entrance => new { entrance.OrganizationId, entrance.ZevId, entrance.BuildingId, entrance.Id });
            entity.HasIndex(entrance => new { entrance.OrganizationId, entrance.ZevId, entrance.BuildingId, entrance.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
            entity.HasQueryFilter(entrance => !entrance.IsDeleted);
            entity.HasIndex(entrance => new { entrance.ZevId, entrance.BuildingId, entrance.IsActive });
        });

        modelBuilder.Entity<PropertyUnit>(entity =>
        {
            entity.ToTable("PropertyUnits", table =>
            {
                table.HasCheckConstraint("CK_PropertyUnits_Area", "[AreaSquareMeters] > 0");
                table.HasCheckConstraint("CK_PropertyUnits_Coefficient", "[Coefficient] > 0");
                table.HasCheckConstraint("CK_PropertyUnits_Members", "[RegisteredMembers] >= 0");
            });
            entity.HasKey(unit => unit.Id);
            entity.Property(unit => unit.Code).HasMaxLength(50).IsRequired();
            entity.Property(unit => unit.Type).HasConversion<string>().HasMaxLength(30);
            entity.Property(unit => unit.AreaSquareMeters).HasPrecision(12, 3);
            entity.Property(unit => unit.Coefficient).HasPrecision(9, 4);
            entity.Property(unit => unit.RowVersion).IsRowVersion();
            entity.HasOne(unit => unit.Entrance).WithMany()
                .HasForeignKey(unit => new { unit.OrganizationId, unit.ZevId, unit.BuildingId, unit.EntranceId })
                .HasPrincipalKey(entrance => new { entrance.OrganizationId, entrance.ZevId, entrance.BuildingId, entrance.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasAlternateKey(unit => new { unit.OrganizationId, unit.ZevId, unit.Id });
            entity.HasIndex(unit => new { unit.OrganizationId, unit.ZevId, unit.EntranceId, unit.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
            entity.HasQueryFilter(unit => !unit.IsDeleted);
            entity.HasIndex(unit => new { unit.ZevId, unit.IsActive });
            entity.HasIndex(unit => new { unit.ZevId, unit.EntranceId, unit.Code, unit.Id });
        });

        modelBuilder.Entity<Person>(entity =>
        {
            entity.ToTable("People");
            entity.HasKey(person => person.Id);
            entity.Property(person => person.Kind).HasConversion<string>().HasMaxLength(20);
            entity.Property(person => person.UserLinkStatus).HasConversion<string>().HasMaxLength(20).HasDefaultValue(UserPersonLinkStatus.Pending);
            entity.HasIndex(person => new { person.UserId, person.UserLinkStatus });
            entity.Property(person => person.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(person => person.TaxIdentifier).HasMaxLength(32);
            entity.Property(person => person.AddressLine).HasMaxLength(250);
            entity.Property(person => person.CityMunicipality).HasMaxLength(120);
            entity.Property(person => person.Email).HasMaxLength(320);
            entity.Property(person => person.Phone).HasMaxLength(40);
            entity.Property(person => person.RowVersion).IsRowVersion();
            entity.HasOne(person => person.Organization).WithMany().HasForeignKey(person => person.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(person => person.User).WithMany().HasForeignKey(person => person.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasAlternateKey(person => new { person.OrganizationId, person.Id });
            entity.HasIndex(person => new { person.OrganizationId, person.DisplayName });
            entity.HasIndex(person => new { person.OrganizationId, person.UserId }).IsUnique()
                .HasFilter("[UserId] IS NOT NULL AND [IsDeleted] = 0");
            entity.HasQueryFilter(person => !person.IsDeleted);
        });

        modelBuilder.Entity<Ownership>(entity =>
        {
            entity.ToTable("Ownerships", table =>
            {
                table.HasCheckConstraint("CK_Ownerships_Dates", "[ValidTo] IS NULL OR [ValidTo] > [ValidFrom]");
                table.HasCheckConstraint("CK_Ownerships_Share", "[Share] > 0 AND [Share] <= 1");
            });
            entity.HasKey(ownership => ownership.Id);
            entity.Property(ownership => ownership.Share).HasPrecision(9, 6);
            entity.Property(ownership => ownership.RowVersion).IsRowVersion();
            entity.HasOne(ownership => ownership.PropertyUnit).WithMany()
                .HasForeignKey(ownership => new { ownership.OrganizationId, ownership.ZevId, ownership.PropertyUnitId })
                .HasPrincipalKey(unit => new { unit.OrganizationId, unit.ZevId, unit.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(ownership => ownership.Person).WithMany()
                .HasForeignKey(ownership => new { ownership.OrganizationId, ownership.PersonId })
                .HasPrincipalKey(person => new { person.OrganizationId, person.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(ownership => new { ownership.PropertyUnitId, ownership.PersonId, ownership.ValidFrom }).IsUnique();
            entity.HasIndex(ownership => new { ownership.OrganizationId, ownership.ZevId, ownership.PropertyUnitId, ownership.ValidFrom });
            entity.HasIndex(ownership => new { ownership.PersonId, ownership.ValidFrom });
        });

        modelBuilder.Entity<Occupancy>(entity =>
        {
            entity.ToTable("Occupancies", table => table.HasCheckConstraint("CK_Occupancies_Dates", "[ValidTo] IS NULL OR [ValidTo] > [ValidFrom]"));
            entity.HasKey(occupancy => occupancy.Id);
            entity.Property(occupancy => occupancy.Kind).HasConversion<string>().HasMaxLength(20);
            entity.Property(occupancy => occupancy.RowVersion).IsRowVersion();
            entity.HasOne(occupancy => occupancy.PropertyUnit).WithMany()
                .HasForeignKey(occupancy => new { occupancy.OrganizationId, occupancy.ZevId, occupancy.PropertyUnitId })
                .HasPrincipalKey(unit => new { unit.OrganizationId, unit.ZevId, unit.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(occupancy => occupancy.Person).WithMany()
                .HasForeignKey(occupancy => new { occupancy.OrganizationId, occupancy.PersonId })
                .HasPrincipalKey(person => new { person.OrganizationId, person.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(occupancy => new { occupancy.PropertyUnitId, occupancy.PersonId, occupancy.ValidFrom }).IsUnique();
            entity.HasIndex(occupancy => new { occupancy.ZevId, occupancy.PropertyUnitId, occupancy.ValidFrom });
        });

        modelBuilder.Entity<Fund>(entity =>
        {
            entity.ToTable("Funds");
            entity.HasKey(fund => fund.Id);
            entity.Property(fund => fund.Kind).HasConversion<string>().HasMaxLength(20);
            entity.Property(fund => fund.Name).HasMaxLength(120).IsRequired();
            entity.Property(fund => fund.RowVersion).IsRowVersion();
            entity.HasOne(fund => fund.Zev).WithMany().HasForeignKey(fund => new { fund.OrganizationId, fund.ZevId })
                .HasPrincipalKey(zev => new { zev.OrganizationId, zev.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasAlternateKey(fund => new { fund.OrganizationId, fund.ZevId, fund.Id });
            entity.HasIndex(fund => new { fund.ZevId, fund.Kind }).IsUnique();
        });

        modelBuilder.Entity<TariffVersion>(entity =>
        {
            entity.ToTable("TariffVersions", table =>
            {
                table.HasCheckConstraint("CK_TariffVersions_Rate", "[Rate] >= 0");
                table.HasCheckConstraint("CK_TariffVersions_Dates", "[ValidTo] IS NULL OR [ValidTo] > [ValidFrom]");
                table.HasCheckConstraint("CK_TariffVersions_DisplayOrder", "[DisplayOrder] >= 0");
            });
            entity.HasKey(tariff => tariff.Id);
            entity.Property(tariff => tariff.Code).HasMaxLength(50).IsRequired();
            entity.Property(tariff => tariff.Name).HasMaxLength(200).IsRequired();
            entity.Property(tariff => tariff.Description).HasMaxLength(1000);
            entity.Property(tariff => tariff.Basis).HasConversion<string>().HasMaxLength(30);
            entity.Property(tariff => tariff.Rate).HasPrecision(18, 4);
            entity.Property(tariff => tariff.RowVersion).IsRowVersion();
            entity.HasOne(tariff => tariff.Fund).WithMany()
                .HasForeignKey(tariff => new { tariff.OrganizationId, tariff.ZevId, tariff.FundId })
                .HasPrincipalKey(fund => new { fund.OrganizationId, fund.ZevId, fund.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(tariff => new { tariff.ZevId, tariff.Code, tariff.Version }).IsUnique();
            entity.HasIndex(tariff => new { tariff.ZevId, tariff.IsActive, tariff.ValidFrom, tariff.ValidTo });
        });

        modelBuilder.Entity<BillingRun>(entity =>
        {
            entity.ToTable("BillingRuns", table => table.HasCheckConstraint("CK_BillingRuns_Total", "[TotalAmount] >= 0"));
            entity.HasKey(run => run.Id);
            entity.Property(run => run.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(run => run.TotalAmount).HasPrecision(18, 2);
            entity.HasOne<ZevEntity>().WithMany().HasForeignKey(run => new { run.OrganizationId, run.ZevId })
                .HasPrincipalKey(zev => new { zev.OrganizationId, zev.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasAlternateKey(run => new { run.OrganizationId, run.ZevId, run.Id });
            entity.HasIndex(run => new { run.ZevId, run.Period }).IsUnique();
        });

        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.ToTable("Invoices", table =>
            {
                table.HasCheckConstraint("CK_Invoices_NumberSequence", "[NumberSequence] > 0");
                table.HasCheckConstraint("CK_Invoices_CurrentTotal", "[CurrentChargeTotal] >= 0 AND [TotalAmount] >= 0 AND [DiscountTotal] >= 0");
            });
            entity.HasKey(invoice => invoice.Id);
            entity.Property(invoice => invoice.InvoiceNumber).HasMaxLength(80).IsRequired();
            entity.Property(invoice => invoice.DocumentStatus).HasConversion<string>().HasMaxLength(20);
            entity.Property(invoice => invoice.Currency).HasMaxLength(3).IsRequired();
            entity.Property(invoice => invoice.PaymentReferenceSnapshot).HasMaxLength(120).IsRequired();
            entity.Property(invoice => invoice.ZevNameSnapshot).HasMaxLength(200).IsRequired();
            entity.Property(invoice => invoice.ZevAddressSnapshot).HasMaxLength(250);
            entity.Property(invoice => invoice.ZevTaxIdentifierSnapshot).HasMaxLength(32);
            entity.Property(invoice => invoice.ZevEmailSnapshot).HasMaxLength(320);
            entity.Property(invoice => invoice.ZevPhoneSnapshot).HasMaxLength(40);
            entity.Property(invoice => invoice.PersonNameSnapshot).HasMaxLength(200).IsRequired();
            entity.Property(invoice => invoice.PersonAddressSnapshot).HasMaxLength(250);
            entity.Property(invoice => invoice.UnitCodeSnapshot).HasMaxLength(50).IsRequired();
            entity.Property(invoice => invoice.UnitTypeSnapshot).HasMaxLength(30).IsRequired();
            entity.Property(invoice => invoice.UnitAreaSnapshot).HasPrecision(12, 3);
            entity.Property(invoice => invoice.UnitAddressSnapshot).HasMaxLength(250);
            entity.Property(invoice => invoice.PreviousBalanceSnapshot).HasPrecision(18, 2);
            entity.Property(invoice => invoice.CurrentChargeTotal).HasPrecision(18, 2);
            entity.Property(invoice => invoice.DiscountTotal).HasPrecision(18, 2);
            entity.Property(invoice => invoice.TotalAmount).HasPrecision(18, 2);
            entity.Property(invoice => invoice.TotalDueAtIssue).HasPrecision(18, 2);
            entity.HasOne<BillingRun>().WithMany().HasForeignKey(invoice => new { invoice.OrganizationId, invoice.ZevId, invoice.BillingRunId })
                .HasPrincipalKey(run => new { run.OrganizationId, run.ZevId, run.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PropertyUnit>().WithMany().HasForeignKey(invoice => new { invoice.OrganizationId, invoice.ZevId, invoice.PropertyUnitId })
                .HasPrincipalKey(unit => new { unit.OrganizationId, unit.ZevId, unit.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Person>().WithMany().HasForeignKey(invoice => new { invoice.OrganizationId, invoice.PersonId })
                .HasPrincipalKey(person => new { person.OrganizationId, person.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasAlternateKey(invoice => new { invoice.OrganizationId, invoice.ZevId, invoice.Id });
            entity.HasIndex(invoice => new { invoice.ZevId, invoice.InvoiceNumber }).IsUnique();
            entity.HasIndex(invoice => new { invoice.ZevId, invoice.NumberYear, invoice.NumberSequence }).IsUnique();
            entity.HasIndex(invoice => new { invoice.ZevId, invoice.Period, invoice.PropertyUnitId, invoice.PersonId }).IsUnique();
            entity.HasIndex(invoice => new { invoice.ZevId, invoice.BillingRunId });
        });

        modelBuilder.Entity<InvoiceLine>(entity =>
        {
            entity.ToTable("InvoiceLines", table =>
            {
                table.HasCheckConstraint("CK_InvoiceLines_Amounts", "[AmountBeforeDiscount] >= 0 AND [DiscountAmount] >= 0 AND [FinalAmount] >= 0");
                table.HasCheckConstraint("CK_InvoiceLines_Quantity", "[Quantity] > 0");
            });
            entity.HasKey(line => line.Id);
            entity.Property(line => line.CodeSnapshot).HasMaxLength(50).IsRequired();
            entity.Property(line => line.DescriptionSnapshot).HasMaxLength(200).IsRequired();
            entity.Property(line => line.UnitOfMeasureSnapshot).HasMaxLength(30).IsRequired();
            entity.Property(line => line.Quantity).HasPrecision(18, 3);
            entity.Property(line => line.UnitPrice).HasPrecision(18, 4);
            entity.Property(line => line.OwnershipShareSnapshot).HasPrecision(9, 6);
            entity.Property(line => line.DiscountType).HasConversion<string>().HasMaxLength(20);
            entity.Property(line => line.DiscountValue).HasPrecision(18, 4);
            entity.Property(line => line.DiscountAmount).HasPrecision(18, 2);
            entity.Property(line => line.AmountBeforeDiscount).HasPrecision(18, 2);
            entity.Property(line => line.FinalAmount).HasPrecision(18, 2);
            entity.Property(line => line.NoteSnapshot).HasMaxLength(1000);
            entity.Property(line => line.FundNameSnapshot).HasMaxLength(120).IsRequired();
            entity.Property(line => line.BankAccountSnapshot).HasMaxLength(64);
            entity.Property(line => line.BankNameSnapshot).HasMaxLength(120);
            entity.HasOne(line => line.Invoice).WithMany(invoice => invoice.Lines)
                .HasForeignKey(line => new { line.OrganizationId, line.ZevId, line.InvoiceId })
                .HasPrincipalKey(invoice => new { invoice.OrganizationId, invoice.ZevId, invoice.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(line => line.Charge).WithMany()
                .HasForeignKey(line => new { line.OrganizationId, line.ZevId, line.ChargeId })
                .HasPrincipalKey(charge => new { charge.OrganizationId, charge.ZevId, charge.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(line => line.ChargeId).IsUnique();
            entity.HasIndex(line => new { line.InvoiceId, line.SortOrder });
        });

        modelBuilder.Entity<Charge>(entity =>
        {
            entity.ToTable("Charges", table => table.HasCheckConstraint("CK_Charges_Amount",
                "[Amount] >= 0 OR ([Amount] < 0 AND [ReversesChargeId] IS NOT NULL AND [FinancialDocumentId] IS NOT NULL)"));
            entity.HasKey(charge => charge.Id);
            entity.Property(charge => charge.BasisQuantity).HasPrecision(18, 3);
            entity.Property(charge => charge.RateSnapshot).HasPrecision(18, 4);
            entity.Property(charge => charge.OwnershipShareSnapshot).HasPrecision(9, 6);
            entity.Property(charge => charge.Amount).HasPrecision(18, 2);
            entity.Property(charge => charge.Reference).HasMaxLength(80).IsRequired();
            entity.Property(charge => charge.Note).HasMaxLength(1000);
            entity.HasOne(charge => charge.PropertyUnit).WithMany()
                .HasForeignKey(charge => new { charge.OrganizationId, charge.ZevId, charge.PropertyUnitId })
                .HasPrincipalKey(unit => new { unit.OrganizationId, unit.ZevId, unit.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(charge => charge.Person).WithMany()
                .HasForeignKey(charge => new { charge.OrganizationId, charge.PersonId })
                .HasPrincipalKey(person => new { person.OrganizationId, person.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(charge => charge.Fund).WithMany()
                .HasForeignKey(charge => new { charge.OrganizationId, charge.ZevId, charge.FundId })
                .HasPrincipalKey(fund => new { fund.OrganizationId, fund.ZevId, fund.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(charge => charge.TariffVersion).WithMany().HasForeignKey(charge => charge.TariffVersionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(charge => charge.BillingRun).WithMany()
                .HasForeignKey(charge => new { charge.OrganizationId, charge.ZevId, charge.BillingRunId })
                .HasPrincipalKey(run => new { run.OrganizationId, run.ZevId, run.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<FinancialDocument>().WithMany()
                .HasForeignKey(charge => new { charge.OrganizationId, charge.ZevId, charge.FinancialDocumentId })
                .HasPrincipalKey(document => new { document.OrganizationId, document.ZevId, document.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(charge => charge.ReversesCharge).WithMany()
                .HasForeignKey(charge => charge.ReversesChargeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(charge => new { charge.ZevId, charge.Period, charge.PropertyUnitId, charge.PersonId, charge.TariffVersionId })
                .IsUnique().HasFilter("[BillingRunId] IS NOT NULL AND [TariffVersionId] IS NOT NULL");
            entity.HasIndex(charge => new { charge.PersonId, charge.DueDate });
            entity.HasIndex(charge => charge.Reference).IsUnique();
            entity.HasIndex(charge => charge.ReversesChargeId);
        });

        modelBuilder.Entity<FinancialDocument>(entity =>
        {
            entity.ToTable("FinancialDocuments", table =>
            {
                table.HasCheckConstraint("CK_FinancialDocuments_Amount",
                    "([Type] = 'Debit' AND [SignedAmount] > 0) OR ([Type] = 'Credit' AND [SignedAmount] < 0) OR ([Type] = 'Storno' AND [SignedAmount] <= 0)");
                table.HasCheckConstraint("CK_FinancialDocuments_Status", "[DocumentStatus] = 'Issued'");
            });
            entity.HasKey(document => document.Id);
            entity.Property(document => document.Type).HasConversion<string>().HasMaxLength(20);
            entity.Property(document => document.DocumentStatus).HasConversion<string>().HasMaxLength(20);
            entity.Property(document => document.Series).HasConversion<string>().HasMaxLength(20);
            entity.Property(document => document.DocumentNumber).HasMaxLength(80).IsRequired();
            entity.Property(document => document.Reason).HasMaxLength(500).IsRequired();
            entity.Property(document => document.Note).HasMaxLength(1000);
            entity.Property(document => document.SignedAmount).HasPrecision(18, 2);
            entity.Property(document => document.Currency).HasMaxLength(3).IsRequired();
            entity.Property(document => document.ZevNameSnapshot).HasMaxLength(200).IsRequired();
            entity.Property(document => document.PersonNameSnapshot).HasMaxLength(200).IsRequired();
            entity.Property(document => document.UnitCodeSnapshot).HasMaxLength(50).IsRequired();
            entity.Property(document => document.OriginalInvoiceNumberSnapshot).HasMaxLength(80).IsRequired();
            entity.HasOne(document => document.OriginalInvoice).WithMany()
                .HasForeignKey(document => new { document.OrganizationId, document.ZevId, document.OriginalInvoiceId })
                .HasPrincipalKey(invoice => new { invoice.OrganizationId, invoice.ZevId, invoice.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasAlternateKey(document => new { document.OrganizationId, document.ZevId, document.Id });
            entity.HasIndex(document => new { document.ZevId, document.RequestId }).IsUnique();
            entity.HasIndex(document => new { document.ZevId, document.DocumentNumber }).IsUnique();
            entity.HasIndex(document => new { document.ZevId, document.Series, document.NumberYear, document.NumberSequence }).IsUnique();
            entity.HasIndex(document => document.OriginalInvoiceId)
                .IsUnique().HasFilter("[Type] = 'Storno'");
        });

        modelBuilder.Entity<FinancialDocumentLine>(entity =>
        {
            entity.ToTable("FinancialDocumentLines", table => table.HasCheckConstraint("CK_FinancialDocumentLines_Amount",
                "[SignedAmount] <> 0"));
            entity.HasKey(line => line.Id);
            entity.Property(line => line.FundNameSnapshot).HasMaxLength(120).IsRequired();
            entity.Property(line => line.DescriptionSnapshot).HasMaxLength(200).IsRequired();
            entity.Property(line => line.SignedAmount).HasPrecision(18, 2);
            entity.HasOne(line => line.Document).WithMany(document => document.Lines)
                .HasForeignKey(line => new { line.OrganizationId, line.ZevId, line.DocumentId })
                .HasPrincipalKey(document => new { document.OrganizationId, document.ZevId, document.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(line => line.Charge).WithMany()
                .HasForeignKey(line => new { line.OrganizationId, line.ZevId, line.ChargeId })
                .HasPrincipalKey(charge => new { charge.OrganizationId, charge.ZevId, charge.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<InvoiceLine>().WithMany().HasForeignKey(line => line.OriginalInvoiceLineId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(line => line.ChargeId).IsUnique();
            entity.HasIndex(line => new { line.DocumentId, line.FundId });
        });

        modelBuilder.Entity<FundTransaction>(entity =>
        {
            entity.ToTable("FundTransactions", table => table.HasCheckConstraint("CK_FundTransactions_Nonzero", "[SignedAmount] <> 0"));
            entity.HasKey(transaction => transaction.Id);
            entity.Property(transaction => transaction.Kind).HasConversion<string>().HasMaxLength(20);
            entity.Property(transaction => transaction.SignedAmount).HasPrecision(18, 2);
            entity.HasOne(transaction => transaction.Fund).WithMany()
                .HasForeignKey(transaction => new { transaction.OrganizationId, transaction.ZevId, transaction.FundId })
                .HasPrincipalKey(fund => new { fund.OrganizationId, fund.ZevId, fund.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(transaction => transaction.ReversesTransaction).WithMany()
                .HasForeignKey(transaction => transaction.ReversesTransactionId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(transaction => new { transaction.FundId, transaction.EffectiveOn });
            entity.HasIndex(transaction => new { transaction.Kind, transaction.SourceId }).IsUnique()
                .HasFilter("[SourceId] IS NOT NULL");
            entity.HasIndex(transaction => transaction.ReversesTransactionId).IsUnique().HasFilter("[ReversesTransactionId] IS NOT NULL");
        });

        modelBuilder.Entity<BankAccount>(entity =>
        {
            entity.ToTable("BankAccounts");
            entity.HasKey(account => account.Id);
            entity.Property(account => account.AccountNumber).HasMaxLength(64).IsRequired();
            entity.Property(account => account.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(account => account.BankName).HasMaxLength(120);
            entity.Property(account => account.RowVersion).IsRowVersion();
            entity.HasOne(account => account.Fund).WithMany()
                .HasForeignKey(account => new { account.OrganizationId, account.ZevId, account.FundId })
                .HasPrincipalKey(fund => new { fund.OrganizationId, fund.ZevId, fund.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasAlternateKey(account => new { account.OrganizationId, account.ZevId, account.Id });
            entity.HasIndex(account => new { account.ZevId, account.AccountNumber }).IsUnique();
        });

        modelBuilder.Entity<BankStatementImport>(entity =>
        {
            entity.ToTable("BankStatementImports", table => table.HasCheckConstraint("CK_BankStatementImports_Period",
                "[StatementFrom] IS NULL OR [StatementTo] IS NULL OR [StatementTo] >= [StatementFrom]"));
            entity.HasKey(import => import.Id);
            entity.Property(import => import.OpeningBalance).HasPrecision(18, 2);
            entity.Property(import => import.ClosingBalance).HasPrecision(18, 2);
            entity.Property(import => import.OriginalFileName).HasMaxLength(255).IsRequired();
            entity.Property(import => import.ChecksumSha256).HasMaxLength(64).IsRequired();
            entity.Property(import => import.Format).HasMaxLength(30).IsRequired();
            entity.Property(import => import.Status).HasConversion<string>().HasMaxLength(30);
            entity.HasOne(import => import.BankAccount).WithMany()
                .HasForeignKey(import => new { import.OrganizationId, import.ZevId, import.BankAccountId })
                .HasPrincipalKey(account => new { account.OrganizationId, account.ZevId, account.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasAlternateKey(import => new { import.OrganizationId, import.ZevId, import.Id });
            entity.HasIndex(import => new { import.ZevId, import.BankAccountId, import.ChecksumSha256 }).IsUnique();
            entity.HasIndex(import => new { import.ZevId, import.ImportedUtc });
        });

        modelBuilder.Entity<BankTransaction>(entity =>
        {
            entity.ToTable("BankTransactions", table => table.HasCheckConstraint("CK_BankTransactions_Amount", "[Amount] <> 0"));
            entity.HasKey(transaction => transaction.Id);
            entity.Property(transaction => transaction.Amount).HasPrecision(18, 2);
            entity.Property(transaction => transaction.Reference).HasMaxLength(120);
            entity.Property(transaction => transaction.Counterparty).HasMaxLength(250);
            entity.Property(transaction => transaction.Description).HasMaxLength(500);
            entity.Property(transaction => transaction.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(transaction => transaction.RowVersion).IsRowVersion();
            entity.HasOne(transaction => transaction.Import).WithMany()
                .HasForeignKey(transaction => new { transaction.OrganizationId, transaction.ZevId, transaction.ImportId })
                .HasPrincipalKey(import => new { import.OrganizationId, import.ZevId, import.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasAlternateKey(transaction => new { transaction.OrganizationId, transaction.ZevId, transaction.Id });
            entity.HasIndex(transaction => new { transaction.ImportId, transaction.Sequence }).IsUnique();
            entity.HasIndex(transaction => new { transaction.ZevId, transaction.Status, transaction.BookingDate });
            entity.HasIndex(transaction => new { transaction.ZevId, transaction.Reference });
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.ToTable("Payments", table =>
            {
                table.HasCheckConstraint("CK_Payments_Amount", "[Amount] > 0");
                table.HasCheckConstraint("CK_Payments_Source", "([SourceType] = 'BankTransaction' AND [BankTransactionId] IS NOT NULL AND [ManualReason] IS NULL AND [ManualMethod] IS NULL) OR ([SourceType] = 'Manual' AND [BankTransactionId] IS NULL AND [ManualReason] IS NOT NULL AND LEN(LTRIM(RTRIM([ManualReason]))) > 0 AND [ManualMethod] IS NOT NULL AND LEN(LTRIM(RTRIM([ManualMethod]))) > 0 AND [RequestId] IS NOT NULL)");
                table.HasCheckConstraint("CK_Payments_Reversal", "[ReversalReason] IS NULL OR ([Status] = 'Reversed' AND LEN(LTRIM(RTRIM([ReversalReason]))) > 0)");
            });
            entity.HasKey(payment => payment.Id);
            entity.Property(payment => payment.Amount).HasPrecision(18, 2);
            entity.Property(payment => payment.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(payment => payment.SourceType).HasConversion<string>().HasMaxLength(20);
            entity.Property(payment => payment.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(payment => payment.ManualReason).HasMaxLength(500);
            entity.Property(payment => payment.ManualMethod).HasMaxLength(50);
            entity.Property(payment => payment.ManualReference).HasMaxLength(120);
            entity.Property(payment => payment.ManualDescription).HasMaxLength(500);
            entity.Property(payment => payment.ReversalReason).HasMaxLength(500);
            entity.HasOne<PropertyUnit>().WithMany()
                .HasForeignKey(payment => new { payment.OrganizationId, payment.ZevId, payment.ManualUnitId })
                .HasPrincipalKey(unit => new { unit.OrganizationId, unit.ZevId, unit.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(payment => payment.BankTransaction).WithMany()
                .HasForeignKey(payment => new { payment.OrganizationId, payment.ZevId, payment.BankTransactionId })
                .HasPrincipalKey(transaction => new { transaction.OrganizationId, transaction.ZevId, transaction.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Person>().WithMany().HasForeignKey(payment => new { payment.OrganizationId, payment.PersonId })
                .HasPrincipalKey(person => new { person.OrganizationId, person.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasAlternateKey(payment => new { payment.OrganizationId, payment.ZevId, payment.Id });
            entity.HasIndex(payment => payment.BankTransactionId).IsUnique()
                .HasFilter("[BankTransactionId] IS NOT NULL");
            entity.HasIndex(payment => new { payment.ZevId, payment.RequestId }).IsUnique()
                .HasFilter("[RequestId] IS NOT NULL");
            entity.HasIndex(payment => new { payment.ZevId, payment.PersonId, payment.PaidOn });
        });

        modelBuilder.Entity<PaymentAllocation>(entity =>
        {
            entity.ToTable("PaymentAllocations", table => table.HasCheckConstraint("CK_PaymentAllocations_Amount",
                "([Amount] > 0 AND [ReversesAllocationId] IS NULL AND [FinancialDocumentId] IS NULL AND [ReversalReason] IS NULL) OR ([Amount] < 0 AND [ReversesAllocationId] IS NOT NULL AND (([FinancialDocumentId] IS NOT NULL AND [ReversalReason] IS NULL) OR ([FinancialDocumentId] IS NULL AND [ReversalReason] IS NOT NULL AND LEN(LTRIM(RTRIM([ReversalReason]))) > 0)))"));
            entity.HasKey(allocation => allocation.Id);
            entity.Property(allocation => allocation.Amount).HasPrecision(18, 2);
            entity.Property(allocation => allocation.ReversalReason).HasMaxLength(500);
            entity.HasOne(allocation => allocation.Payment).WithMany()
                .HasForeignKey(allocation => new { allocation.OrganizationId, allocation.ZevId, allocation.PaymentId })
                .HasPrincipalKey(payment => new { payment.OrganizationId, payment.ZevId, payment.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(allocation => allocation.Charge).WithMany()
                .HasForeignKey(allocation => new { allocation.OrganizationId, allocation.ZevId, allocation.ChargeId })
                .HasPrincipalKey(charge => new { charge.OrganizationId, charge.ZevId, charge.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(allocation => allocation.ReversesAllocation).WithMany()
                .HasForeignKey(allocation => allocation.ReversesAllocationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<FinancialDocument>().WithMany()
                .HasForeignKey(allocation => new { allocation.OrganizationId, allocation.ZevId, allocation.FinancialDocumentId })
                .HasPrincipalKey(document => new { document.OrganizationId, document.ZevId, document.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(allocation => new { allocation.PaymentId, allocation.OperationId, allocation.ChargeId })
                .IsUnique().HasFilter("[OperationId] IS NOT NULL");
            entity.HasIndex(allocation => allocation.ChargeId);
            entity.HasIndex(allocation => new { allocation.ReversesAllocationId, allocation.FinancialDocumentId })
                .IsUnique().HasFilter("[ReversesAllocationId] IS NOT NULL");
        });

        modelBuilder.Entity<PaymentAccount>(entity =>
        {
            entity.ToTable("PaymentAccounts", table => table.HasCheckConstraint("CK_PaymentAccounts_Number",
                "[Number] BETWEEN 1 AND 9999999999"));
            entity.HasKey(account => account.Number);
            entity.Property(account => account.Number).UseIdentityColumn();
            entity.HasOne<ZevEntity>().WithMany().HasForeignKey(account => new { account.OrganizationId, account.ZevId })
                .HasPrincipalKey(zev => new { zev.OrganizationId, zev.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PropertyUnit>().WithMany()
                .HasForeignKey(account => new { account.OrganizationId, account.ZevId, account.PropertyUnitId })
                .HasPrincipalKey(unit => new { unit.OrganizationId, unit.ZevId, unit.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Person>().WithMany()
                .HasForeignKey(account => new { account.OrganizationId, account.PersonId })
                .HasPrincipalKey(person => new { person.OrganizationId, person.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(account => new { account.ZevId, account.PropertyUnitId, account.PersonId }).IsUnique();
        });

        modelBuilder.Entity<PaymentIdentificationHistory>(entity =>
        {
            entity.ToTable("PaymentIdentificationHistories", table =>
                table.HasCheckConstraint("CK_PaymentIdentificationHistories_Reason",
                    "LEN(LTRIM(RTRIM([Reason]))) > 0"));
            entity.HasKey(history => history.Id);
            entity.Property(history => history.Reason).HasMaxLength(500).IsRequired();
            entity.HasOne(history => history.Payment).WithMany()
                .HasForeignKey(history => new { history.OrganizationId, history.ZevId, history.PaymentId })
                .HasPrincipalKey(payment => new { payment.OrganizationId, payment.ZevId, payment.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Person>().WithMany()
                .HasForeignKey(history => new { history.OrganizationId, history.PreviousPersonId })
                .HasPrincipalKey(person => new { person.OrganizationId, person.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Person>().WithMany()
                .HasForeignKey(history => new { history.OrganizationId, history.NewPersonId })
                .HasPrincipalKey(person => new { person.OrganizationId, person.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(history => new { history.PaymentId, history.ChangedUtc });
            entity.HasIndex(history => new { history.ZevId, history.PreviousPersonId, history.ChangedUtc });
            entity.HasIndex(history => new { history.ZevId, history.NewPersonId, history.ChangedUtc });
        });

        modelBuilder.Entity<ReconciliationAttempt>(entity =>
        {
            entity.ToTable("ReconciliationAttempts");
            entity.HasKey(attempt => attempt.Id);
            entity.Property(attempt => attempt.Rule).HasMaxLength(80).IsRequired();
            entity.Property(attempt => attempt.Result).HasMaxLength(300).IsRequired();
            entity.Property(attempt => attempt.Status).HasConversion<string>().HasMaxLength(30);
            entity.HasOne(attempt => attempt.BankTransaction).WithMany()
                .HasForeignKey(attempt => new { attempt.OrganizationId, attempt.ZevId, attempt.BankTransactionId })
                .HasPrincipalKey(transaction => new { transaction.OrganizationId, transaction.ZevId, transaction.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(attempt => new { attempt.BankTransactionId, attempt.CreatedUtc });
        });

        modelBuilder.Entity<StoredFile>(entity =>
        {
            entity.ToTable("StoredFiles", table => table.HasCheckConstraint("CK_StoredFiles_Length", "[Length] > 0"));
            entity.HasKey(file => file.Id);
            entity.Property(file => file.OriginalFileName).HasMaxLength(255).IsRequired();
            entity.Property(file => file.ContentType).HasMaxLength(100).IsRequired();
            entity.Property(file => file.Sha256).HasMaxLength(64).IsRequired();
            entity.Property(file => file.StorageKey).HasMaxLength(100).IsRequired();
            entity.HasOne<ZevEntity>().WithMany().HasForeignKey(file => new { file.OrganizationId, file.ZevId })
                .HasPrincipalKey(zev => new { zev.OrganizationId, zev.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasAlternateKey(file => new { file.OrganizationId, file.ZevId, file.Id });
            entity.HasIndex(file => new { file.ZevId, file.UploadedUtc });
        });

        modelBuilder.Entity<Supplier>(entity =>
        {
            entity.ToTable("Suppliers");
            entity.HasKey(supplier => supplier.Id);
            entity.Property(supplier => supplier.Name).HasMaxLength(200).IsRequired();
            entity.Property(supplier => supplier.TaxIdentifier).HasMaxLength(32);
            entity.Property(supplier => supplier.Email).HasMaxLength(320);
            entity.Property(supplier => supplier.Phone).HasMaxLength(40);
            entity.Property(supplier => supplier.RowVersion).IsRowVersion();
            entity.HasOne<Organization>().WithMany().HasForeignKey(supplier => supplier.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasAlternateKey(supplier => new { supplier.OrganizationId, supplier.Id });
            entity.HasIndex(supplier => new { supplier.OrganizationId, supplier.Name });
            entity.HasQueryFilter(supplier => !supplier.IsDeleted);
        });

        modelBuilder.Entity<Expense>(entity =>
        {
            entity.ToTable("Expenses", table => table.HasCheckConstraint("CK_Expenses_Amount", "[Amount] > 0"));
            entity.HasKey(expense => expense.Id);
            entity.Property(expense => expense.InvoiceNumber).HasMaxLength(100).IsRequired();
            entity.Property(expense => expense.Category).HasMaxLength(100).IsRequired();
            entity.Property(expense => expense.Description).HasMaxLength(1000);
            entity.Property(expense => expense.Amount).HasPrecision(18, 2);
            entity.Property(expense => expense.CurrencyCode).HasMaxLength(7).IsRequired();
            entity.Property(expense => expense.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(expense => expense.RowVersion).IsRowVersion();
            entity.HasOne(expense => expense.Fund).WithMany()
                .HasForeignKey(expense => new { expense.OrganizationId, expense.ZevId, expense.FundId })
                .HasPrincipalKey(fund => new { fund.OrganizationId, fund.ZevId, fund.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(expense => expense.Supplier).WithMany()
                .HasForeignKey(expense => new { expense.OrganizationId, expense.SupplierId })
                .HasPrincipalKey(supplier => new { supplier.OrganizationId, supplier.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(expense => expense.Document).WithMany()
                .HasForeignKey(expense => new { expense.OrganizationId, expense.ZevId, expense.DocumentId })
                .HasPrincipalKey(file => new { file.OrganizationId, file.ZevId, file.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(expense => new { expense.ZevId, expense.InvoiceDate, expense.Status });
            entity.HasIndex(expense => new { expense.SupplierId, expense.InvoiceNumber }).IsUnique();
        });

        modelBuilder.Entity<Reminder>(entity =>
        {
            entity.ToTable("Reminders", table =>
            {
                table.HasCheckConstraint("CK_Reminders_Principal", "[PrincipalSnapshot] >= 0");
                table.HasCheckConstraint("CK_Reminders_Interest", "[InterestSnapshot] >= 0");
            });
            entity.HasKey(reminder => reminder.Id);
            entity.Property(reminder => reminder.Kind).HasConversion<string>().HasMaxLength(30);
            entity.Property(reminder => reminder.PrincipalSnapshot).HasPrecision(18, 2);
            entity.Property(reminder => reminder.InterestSnapshot).HasPrecision(18, 2);
            entity.HasOne(reminder => reminder.Person).WithMany()
                .HasForeignKey(reminder => new { reminder.OrganizationId, reminder.PersonId })
                .HasPrincipalKey(person => new { person.OrganizationId, person.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ZevEntity>().WithMany().HasForeignKey(reminder => new { reminder.OrganizationId, reminder.ZevId })
                .HasPrincipalKey(zev => new { zev.OrganizationId, zev.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(reminder => new { reminder.ZevId, reminder.PersonId, reminder.AsOf });
        });

        modelBuilder.Entity<InterestRatePolicy>(entity =>
        {
            entity.ToTable("InterestRatePolicies", table =>
            {
                table.HasCheckConstraint("CK_InterestRatePolicies_Rate", "[AnnualRatePercent] >= 0");
                table.HasCheckConstraint("CK_InterestRatePolicies_Days", "[DaysPerYear] > 0");
                table.HasCheckConstraint("CK_InterestRatePolicies_Dates", "[ValidTo] IS NULL OR [ValidTo] > [ValidFrom]");
            });
            entity.HasKey(policy => policy.Id);
            entity.Property(policy => policy.AnnualRatePercent).HasPrecision(9, 4);
            entity.HasOne<ZevEntity>().WithMany().HasForeignKey(policy => new { policy.OrganizationId, policy.ZevId })
                .HasPrincipalKey(zev => new { zev.OrganizationId, zev.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(policy => new { policy.ZevId, policy.ValidFrom });
        });

        modelBuilder.Entity<LegalCase>(entity =>
        {
            entity.ToTable("LegalCases", table =>
            {
                table.HasCheckConstraint("CK_LegalCases_Principal", "[PrincipalAmount] >= 0");
                table.HasCheckConstraint("CK_LegalCases_Interest", "[InterestAmount] >= 0");
                table.HasCheckConstraint("CK_LegalCases_Costs", "[CostsAmount] >= 0");
                table.HasCheckConstraint("CK_LegalCases_Recovered", "[RecoveredAmount] >= 0");
            });
            entity.HasKey(legalCase => legalCase.Id);
            entity.Property(legalCase => legalCase.PrincipalAmount).HasPrecision(18, 2);
            entity.Property(legalCase => legalCase.InterestAmount).HasPrecision(18, 2);
            entity.Property(legalCase => legalCase.CostsAmount).HasPrecision(18, 2);
            entity.Property(legalCase => legalCase.RecoveredAmount).HasPrecision(18, 2);
            entity.Property(legalCase => legalCase.Lawyer).HasMaxLength(200);
            entity.Property(legalCase => legalCase.Court).HasMaxLength(200);
            entity.Property(legalCase => legalCase.CaseNumber).HasMaxLength(100);
            entity.Property(legalCase => legalCase.Notes).HasMaxLength(2000);
            entity.Property(legalCase => legalCase.Status).HasConversion<string>().HasMaxLength(30);
            entity.Property(legalCase => legalCase.RowVersion).IsRowVersion();
            entity.HasOne(legalCase => legalCase.Person).WithMany()
                .HasForeignKey(legalCase => new { legalCase.OrganizationId, legalCase.PersonId })
                .HasPrincipalKey(person => new { person.OrganizationId, person.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ZevEntity>().WithMany().HasForeignKey(legalCase => new { legalCase.OrganizationId, legalCase.ZevId })
                .HasPrincipalKey(zev => new { zev.OrganizationId, zev.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasAlternateKey(legalCase => new { legalCase.OrganizationId, legalCase.ZevId, legalCase.Id });
            entity.HasIndex(legalCase => new { legalCase.ZevId, legalCase.Status, legalCase.FiledOn });
        });

        modelBuilder.Entity<LegalCaseFile>(entity =>
        {
            entity.ToTable("LegalCaseFiles");
            entity.HasKey(link => link.Id);
            entity.HasOne<LegalCase>().WithMany().HasForeignKey(link => new { link.OrganizationId, link.ZevId, link.LegalCaseId })
                .HasPrincipalKey(legalCase => new { legalCase.OrganizationId, legalCase.ZevId, legalCase.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<StoredFile>().WithMany().HasForeignKey(link => new { link.OrganizationId, link.ZevId, link.StoredFileId })
                .HasPrincipalKey(file => new { file.OrganizationId, file.ZevId, file.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(link => new { link.LegalCaseId, link.StoredFileId }).IsUnique();
        });

        modelBuilder.Entity<MaintenanceTicket>(entity =>
        {
            entity.ToTable("MaintenanceTickets");
            entity.HasKey(ticket => ticket.Id);
            entity.Property(ticket => ticket.Category).HasMaxLength(100).IsRequired();
            entity.Property(ticket => ticket.Description).HasMaxLength(4000).IsRequired();
            entity.Property(ticket => ticket.Location).HasMaxLength(250).IsRequired();
            entity.Property(ticket => ticket.Priority).HasConversion<string>().HasMaxLength(20);
            entity.Property(ticket => ticket.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(ticket => ticket.RowVersion).IsRowVersion();
            entity.HasOne<ZevEntity>().WithMany().HasForeignKey(ticket => new { ticket.OrganizationId, ticket.ZevId })
                .HasPrincipalKey(zev => new { zev.OrganizationId, zev.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PropertyUnit>().WithMany()
                .HasForeignKey(ticket => new { ticket.OrganizationId, ticket.ZevId, ticket.PropertyUnitId })
                .HasPrincipalKey(unit => new { unit.OrganizationId, unit.ZevId, unit.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(ticket => ticket.ReportedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasAlternateKey(ticket => new { ticket.OrganizationId, ticket.ZevId, ticket.Id });
            entity.HasIndex(ticket => new { ticket.ZevId, ticket.Status, ticket.CreatedUtc });
        });

        modelBuilder.Entity<WorkOrder>(entity =>
        {
            entity.ToTable("WorkOrders", table => table.HasCheckConstraint("CK_WorkOrders_Cost", "[CostAmount] IS NULL OR [CostAmount] >= 0"));
            entity.HasKey(order => order.Id);
            entity.Property(order => order.Title).HasMaxLength(200).IsRequired();
            entity.Property(order => order.Description).HasMaxLength(4000).IsRequired();
            entity.Property(order => order.Priority).HasConversion<string>().HasMaxLength(20);
            entity.Property(order => order.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(order => order.CostAmount).HasPrecision(18, 2);
            entity.Property(order => order.RowVersion).IsRowVersion();
            entity.HasOne<ZevEntity>().WithMany().HasForeignKey(order => new { order.OrganizationId, order.ZevId })
                .HasPrincipalKey(zev => new { zev.OrganizationId, zev.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<MaintenanceTicket>().WithMany()
                .HasForeignKey(order => new { order.OrganizationId, order.ZevId, order.TicketId })
                .HasPrincipalKey(ticket => new { ticket.OrganizationId, ticket.ZevId, ticket.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Supplier>().WithMany().HasForeignKey(order => new { order.OrganizationId, order.SupplierId })
                .HasPrincipalKey(supplier => new { supplier.OrganizationId, supplier.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasAlternateKey(order => new { order.OrganizationId, order.ZevId, order.Id });
            entity.HasIndex(order => new { order.ZevId, order.Status, order.DueOn });
        });

        modelBuilder.Entity<MaintenanceTicketFile>(entity =>
        {
            entity.ToTable("MaintenanceTicketFiles");
            entity.HasKey(link => link.Id);
            entity.HasOne<MaintenanceTicket>().WithMany()
                .HasForeignKey(link => new { link.OrganizationId, link.ZevId, link.TicketId })
                .HasPrincipalKey(ticket => new { ticket.OrganizationId, ticket.ZevId, ticket.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<StoredFile>().WithMany()
                .HasForeignKey(link => new { link.OrganizationId, link.ZevId, link.StoredFileId })
                .HasPrincipalKey(file => new { file.OrganizationId, file.ZevId, file.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(link => new { link.TicketId, link.StoredFileId }).IsUnique();
        });

        modelBuilder.Entity<WorkOrderFile>(entity =>
        {
            entity.ToTable("WorkOrderFiles");
            entity.HasKey(link => link.Id);
            entity.HasOne<WorkOrder>().WithMany()
                .HasForeignKey(link => new { link.OrganizationId, link.ZevId, link.WorkOrderId })
                .HasPrincipalKey(order => new { order.OrganizationId, order.ZevId, order.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<StoredFile>().WithMany()
                .HasForeignKey(link => new { link.OrganizationId, link.ZevId, link.StoredFileId })
                .HasPrincipalKey(file => new { file.OrganizationId, file.ZevId, file.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(link => new { link.WorkOrderId, link.StoredFileId }).IsUnique();
        });

        modelBuilder.Entity<MaintenanceStatusEvent>(entity =>
        {
            entity.ToTable("MaintenanceStatusEvents", table => table.HasCheckConstraint("CK_MaintenanceStatusEvents_Target",
                "([TicketId] IS NULL AND [WorkOrderId] IS NOT NULL) OR ([TicketId] IS NOT NULL AND [WorkOrderId] IS NULL)"));
            entity.HasKey(history => history.Id);
            entity.Property(history => history.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasOne<MaintenanceTicket>().WithMany()
                .HasForeignKey(history => new { history.OrganizationId, history.ZevId, history.TicketId })
                .HasPrincipalKey(ticket => new { ticket.OrganizationId, ticket.ZevId, ticket.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<WorkOrder>().WithMany()
                .HasForeignKey(history => new { history.OrganizationId, history.ZevId, history.WorkOrderId })
                .HasPrincipalKey(order => new { order.OrganizationId, order.ZevId, order.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(history => new { history.TicketId, history.ChangedUtc });
            entity.HasIndex(history => new { history.WorkOrderId, history.ChangedUtc });
        });

        modelBuilder.Entity<MaintenancePlan>(entity =>
        {
            entity.ToTable("MaintenancePlans", table => table.HasCheckConstraint("CK_MaintenancePlans_Frequency", "[FrequencyDays] > 0"));
            entity.HasKey(plan => plan.Id);
            entity.Property(plan => plan.Title).HasMaxLength(200).IsRequired();
            entity.Property(plan => plan.Category).HasMaxLength(100).IsRequired();
            entity.HasOne<ZevEntity>().WithMany().HasForeignKey(plan => new { plan.OrganizationId, plan.ZevId })
                .HasPrincipalKey(zev => new { zev.OrganizationId, zev.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasAlternateKey(plan => new { plan.OrganizationId, plan.ZevId, plan.Id });
            entity.HasIndex(plan => new { plan.ZevId, plan.IsActive, plan.FirstDueOn });
        });

        modelBuilder.Entity<ScheduledMaintenance>(entity =>
        {
            entity.ToTable("ScheduledMaintenances");
            entity.HasKey(schedule => schedule.Id);
            entity.Property(schedule => schedule.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasOne(schedule => schedule.Plan).WithMany()
                .HasForeignKey(schedule => new { schedule.OrganizationId, schedule.ZevId, schedule.PlanId })
                .HasPrincipalKey(plan => new { plan.OrganizationId, plan.ZevId, plan.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<WorkOrder>().WithMany()
                .HasForeignKey(schedule => new { schedule.OrganizationId, schedule.ZevId, schedule.WorkOrderId })
                .HasPrincipalKey(order => new { order.OrganizationId, order.ZevId, order.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(schedule => new { schedule.PlanId, schedule.DueOn }).IsUnique();
            entity.HasIndex(schedule => new { schedule.ZevId, schedule.Status, schedule.DueOn });
        });

        modelBuilder.Entity<Hodogram>(entity =>
        {
            entity.ToTable("Hodograms");
            entity.HasKey(hodogram => hodogram.Id);
            entity.Property(hodogram => hodogram.Name).HasMaxLength(200).IsRequired();
            entity.Property(hodogram => hodogram.Description).HasMaxLength(2000);
            entity.Property(hodogram => hodogram.Revision).IsConcurrencyToken();
            entity.HasOne<Organization>().WithMany().HasForeignKey(hodogram => hodogram.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasAlternateKey(hodogram => new { hodogram.OrganizationId, hodogram.Id });
            entity.HasIndex(hodogram => new { hodogram.OrganizationId, hodogram.Name }).IsUnique();
        });

        modelBuilder.Entity<HodogramPhase>(entity =>
        {
            entity.ToTable("HodogramPhases", table => table.HasCheckConstraint("CK_HodogramPhases_SortOrder", "[SortOrder] >= 0"));
            entity.HasKey(phase => phase.Id);
            entity.Property(phase => phase.Name).HasMaxLength(200).IsRequired();
            entity.HasOne<Hodogram>().WithMany().HasForeignKey(phase => new { phase.OrganizationId, phase.HodogramId })
                .HasPrincipalKey(hodogram => new { hodogram.OrganizationId, hodogram.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasAlternateKey(phase => new { phase.OrganizationId, phase.HodogramId, phase.Id });
            entity.HasIndex(phase => new { phase.HodogramId, phase.SortOrder });
        });

        modelBuilder.Entity<HodogramStep>(entity =>
        {
            entity.ToTable("HodogramSteps", table => table.HasCheckConstraint("CK_HodogramSteps_SortOrder", "[SortOrder] >= 0"));
            entity.HasKey(step => step.Id);
            entity.Property(step => step.Name).HasMaxLength(300).IsRequired();
            entity.Property(step => step.Description).HasMaxLength(2000);
            entity.HasOne<HodogramPhase>().WithMany()
                .HasForeignKey(step => new { step.OrganizationId, step.HodogramId, step.PhaseId })
                .HasPrincipalKey(phase => new { phase.OrganizationId, phase.HodogramId, phase.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasAlternateKey(step => new { step.OrganizationId, step.HodogramId, step.Id });
            entity.HasIndex(step => new { step.PhaseId, step.SortOrder });
        });

        modelBuilder.Entity<HodogramAssignment>(entity =>
        {
            entity.ToTable("HodogramAssignments");
            entity.HasKey(assignment => assignment.Id);
            entity.Property(assignment => assignment.Role).HasConversion<string>().HasMaxLength(40);
            entity.Property(assignment => assignment.Value).HasConversion<string>().HasMaxLength(1);
            entity.HasOne<HodogramStep>().WithMany()
                .HasForeignKey(assignment => new { assignment.OrganizationId, assignment.HodogramId, assignment.StepId })
                .HasPrincipalKey(step => new { step.OrganizationId, step.HodogramId, step.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(assignment => new { assignment.StepId, assignment.Role }).IsUnique();
            entity.HasIndex(assignment => assignment.HodogramId);
        });

        modelBuilder.Entity<Announcement>(entity =>
        {
            entity.ToTable("Announcements", table => table.HasCheckConstraint("CK_Announcements_Dates",
                "[ExpiresUtc] IS NULL OR [ExpiresUtc] > [PublishedFromUtc]"));
            entity.HasKey(announcement => announcement.Id);
            entity.Property(announcement => announcement.Scope).HasConversion<string>().HasMaxLength(20);
            entity.Property(announcement => announcement.TargetRole).HasConversion<string>().HasMaxLength(40);
            entity.Property(announcement => announcement.Title).HasMaxLength(200).IsRequired();
            entity.Property(announcement => announcement.Content).HasMaxLength(10000).IsRequired();
            entity.HasOne<ZevEntity>().WithMany().HasForeignKey(announcement => new { announcement.OrganizationId, announcement.ZevId })
                .HasPrincipalKey(zev => new { zev.OrganizationId, zev.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Building>().WithMany()
                .HasForeignKey(announcement => new { announcement.OrganizationId, announcement.ZevId, announcement.BuildingId })
                .HasPrincipalKey(building => new { building.OrganizationId, building.ZevId, building.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Entrance>().WithMany()
                .HasForeignKey(announcement => new { announcement.OrganizationId, announcement.ZevId, announcement.BuildingId, announcement.EntranceId })
                .HasPrincipalKey(entrance => new { entrance.OrganizationId, entrance.ZevId, entrance.BuildingId, entrance.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasAlternateKey(announcement => new { announcement.OrganizationId, announcement.ZevId, announcement.Id });
            entity.HasIndex(announcement => new { announcement.ZevId, announcement.Scope, announcement.PublishedFromUtc });
        });

        modelBuilder.Entity<AnnouncementFile>(entity =>
        {
            entity.ToTable("AnnouncementFiles");
            entity.HasKey(link => link.Id);
            entity.HasOne<Announcement>().WithMany()
                .HasForeignKey(link => new { link.OrganizationId, link.ZevId, link.AnnouncementId })
                .HasPrincipalKey(announcement => new { announcement.OrganizationId, announcement.ZevId, announcement.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<StoredFile>().WithMany()
                .HasForeignKey(link => new { link.OrganizationId, link.ZevId, link.StoredFileId })
                .HasPrincipalKey(file => new { file.OrganizationId, file.ZevId, file.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(link => new { link.AnnouncementId, link.StoredFileId }).IsUnique();
        });

        modelBuilder.Entity<ManagedDocument>(entity =>
        {
            entity.ToTable("ManagedDocuments");
            entity.HasKey(document => document.Id);
            entity.Property(document => document.Title).HasMaxLength(200).IsRequired();
            entity.Property(document => document.Type).HasConversion<string>().HasMaxLength(30);
            entity.Property(document => document.Visibility).HasConversion<string>().HasMaxLength(30);
            entity.Property(document => document.TargetRole).HasConversion<string>().HasMaxLength(40);
            entity.HasOne<StoredFile>().WithMany()
                .HasForeignKey(document => new { document.OrganizationId, document.ZevId, document.StoredFileId })
                .HasPrincipalKey(file => new { file.OrganizationId, file.ZevId, file.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Person>().WithMany().HasForeignKey(document => new { document.OrganizationId, document.PersonId })
                .HasPrincipalKey(person => new { person.OrganizationId, person.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PropertyUnit>().WithMany().HasForeignKey(document => new { document.OrganizationId, document.ZevId, document.PropertyUnitId })
                .HasPrincipalKey(unit => new { unit.OrganizationId, unit.ZevId, unit.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(document => new { document.ZevId, document.Visibility, document.Type });
        });

        modelBuilder.Entity<Ballot>(entity =>
        {
            entity.ToTable("Ballots", table =>
            {
                table.HasCheckConstraint("CK_Ballots_Quorum", "[QuorumPercent] >= 0 AND [QuorumPercent] <= 100");
                table.HasCheckConstraint("CK_Ballots_Approval", "[ApprovalPercent] >= 0 AND [ApprovalPercent] <= 100");
                table.HasCheckConstraint("CK_Ballots_Dates", "[ClosesUtc] > [OpensUtc]");
            });
            entity.HasKey(ballot => ballot.Id);
            entity.Property(ballot => ballot.Title).HasMaxLength(200).IsRequired();
            entity.Property(ballot => ballot.Question).HasMaxLength(4000).IsRequired();
            entity.Property(ballot => ballot.Kind).HasConversion<string>().HasMaxLength(20);
            entity.Property(ballot => ballot.Mode).HasConversion<string>().HasMaxLength(30);
            entity.Property(ballot => ballot.WeightBasis).HasConversion<string>().HasMaxLength(30);
            entity.Property(ballot => ballot.ApprovalDenominator).HasConversion<string>().HasMaxLength(30);
            entity.Property(ballot => ballot.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(ballot => ballot.QuorumPercent).HasPrecision(7, 4);
            entity.Property(ballot => ballot.ApprovalPercent).HasPrecision(7, 4);
            entity.Property(ballot => ballot.EligibleWeight).HasPrecision(18, 4);
            entity.Property(ballot => ballot.CastWeight).HasPrecision(18, 4);
            entity.Property(ballot => ballot.YesWeight).HasPrecision(18, 4);
            entity.Property(ballot => ballot.NoWeight).HasPrecision(18, 4);
            entity.Property(ballot => ballot.AbstainWeight).HasPrecision(18, 4);
            entity.Property(ballot => ballot.RowVersion).IsRowVersion();
            entity.HasOne<ZevEntity>().WithMany().HasForeignKey(ballot => new { ballot.OrganizationId, ballot.ZevId })
                .HasPrincipalKey(zev => new { zev.OrganizationId, zev.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasAlternateKey(ballot => new { ballot.OrganizationId, ballot.ZevId, ballot.Id });
            entity.HasIndex(ballot => new { ballot.ZevId, ballot.Status, ballot.OpensUtc });
        });

        modelBuilder.Entity<BallotEligibility>(entity =>
        {
            entity.ToTable("BallotEligibilities", table => table.HasCheckConstraint("CK_BallotEligibilities_Weight", "[Weight] > 0"));
            entity.HasKey(eligibility => eligibility.Id);
            entity.Property(eligibility => eligibility.Weight).HasPrecision(18, 4);
            entity.HasOne<Ballot>().WithMany()
                .HasForeignKey(eligibility => new { eligibility.OrganizationId, eligibility.ZevId, eligibility.BallotId })
                .HasPrincipalKey(ballot => new { ballot.OrganizationId, ballot.ZevId, ballot.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PropertyUnit>().WithMany()
                .HasForeignKey(eligibility => new { eligibility.OrganizationId, eligibility.ZevId, eligibility.PropertyUnitId })
                .HasPrincipalKey(unit => new { unit.OrganizationId, unit.ZevId, unit.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Person>().WithMany()
                .HasForeignKey(eligibility => new { eligibility.OrganizationId, eligibility.PersonId })
                .HasPrincipalKey(person => new { person.OrganizationId, person.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(eligibility => new { eligibility.BallotId, eligibility.PropertyUnitId, eligibility.PersonId }).IsUnique();
            entity.HasIndex(eligibility => new { eligibility.PersonId, eligibility.BallotId });
        });

        modelBuilder.Entity<Vote>(entity =>
        {
            entity.ToTable("Votes", table => table.HasCheckConstraint("CK_Votes_Weight", "[WeightSnapshot] > 0"));
            entity.HasKey(vote => vote.Id);
            entity.Property(vote => vote.Choice).HasConversion<string>().HasMaxLength(20);
            entity.Property(vote => vote.WeightSnapshot).HasPrecision(18, 4);
            entity.HasOne<Ballot>().WithMany().HasForeignKey(vote => new { vote.OrganizationId, vote.ZevId, vote.BallotId })
                .HasPrincipalKey(ballot => new { ballot.OrganizationId, ballot.ZevId, ballot.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<PropertyUnit>().WithMany()
                .HasForeignKey(vote => new { vote.OrganizationId, vote.ZevId, vote.PropertyUnitId })
                .HasPrincipalKey(unit => new { unit.OrganizationId, unit.ZevId, unit.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Person>().WithMany()
                .HasForeignKey(vote => new { vote.OrganizationId, vote.PersonId })
                .HasPrincipalKey(person => new { person.OrganizationId, person.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(vote => new { vote.BallotId, vote.PropertyUnitId }).IsUnique();
            entity.HasIndex(vote => new { vote.PersonId, vote.BallotId });
        });

        modelBuilder.Entity<Membership>(entity =>
        {
            entity.ToTable("Memberships");
            entity.HasKey(membership => membership.Id);
            entity.Property(membership => membership.Role).HasConversion<string>().HasMaxLength(40);
            entity.Property(membership => membership.RowVersion).IsRowVersion();
            entity.HasOne(membership => membership.User).WithMany().HasForeignKey(membership => membership.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(membership => membership.Organization).WithMany().HasForeignKey(membership => membership.OrganizationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(membership => membership.Zev).WithMany().HasForeignKey(membership => new { membership.OrganizationId, membership.ZevId }).HasPrincipalKey(zev => new { zev.OrganizationId, zev.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(membership => new { membership.UserId, membership.OrganizationId, membership.Role }).IsUnique().HasFilter("[ZevId] IS NULL");
            entity.HasIndex(membership => new { membership.UserId, membership.ZevId, membership.Role }).IsUnique().HasFilter("[ZevId] IS NOT NULL");
            entity.HasIndex(membership => new { membership.OrganizationId, membership.ZevId, membership.IsActive });
        });

        modelBuilder.Entity<AccessOverride>(entity =>
        {
            entity.ToTable("AccessOverrides", table =>
            {
                table.HasCheckConstraint("CK_AccessOverrides_Dates", "[ValidToUtc] IS NULL OR [ValidToUtc] > [ValidFromUtc]");
                table.HasCheckConstraint("CK_AccessOverrides_RevokeReason", "[RevokeReason] IS NULL OR [RevokedUtc] IS NOT NULL");
            });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.PermissionKey).HasMaxLength(80).IsRequired();
            entity.Property(item => item.Effect).HasConversion<string>().HasMaxLength(10);
            entity.Property(item => item.Reason).HasMaxLength(500).IsRequired();
            entity.Property(item => item.RevokeReason).HasMaxLength(500);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(item => item.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(item => item.RevokedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ZevEntity>().WithMany().HasForeignKey(item => new { item.OrganizationId, item.ZevId })
                .HasPrincipalKey(zev => new { zev.OrganizationId, zev.Id }).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(item => new { item.UserId, item.ZevId, item.PermissionKey, item.ValidFromUtc });
        });

        modelBuilder.Entity<Permission>(entity =>
        {
            entity.ToTable("Permissions");
            entity.HasKey(permission => permission.Key);
            entity.Property(permission => permission.Key).HasMaxLength(80);
            entity.Property(permission => permission.Description).HasMaxLength(200);
            entity.HasData(
                new Permission { Key = "zev.read", Description = "Read permitted ZEV resources" },
                new Permission { Key = "organization.read", Description = "Read organization resources" },
                new Permission { Key = "finance.manage", Description = "Manage ZEV finances" },
                new Permission { Key = "legal.manage", Description = "Manage ZEV registry and legal cases" },
                new Permission { Key = "maintenance.manage", Description = "Manage ZEV maintenance" });
        });

        modelBuilder.Entity<RolePermission>(entity =>
        {
            entity.ToTable("RolePermissions");
            entity.HasKey(grant => new { grant.Role, grant.PermissionKey });
            entity.Property(grant => grant.Role).HasConversion<string>().HasMaxLength(40);
            entity.Property(grant => grant.PermissionKey).HasMaxLength(80);
            entity.HasOne(grant => grant.Permission).WithMany().HasForeignKey(grant => grant.PermissionKey).OnDelete(DeleteBehavior.Restrict);
            entity.HasData(
                new RolePermission { Role = MembershipRole.Manager, PermissionKey = "zev.read" },
                new RolePermission { Role = MembershipRole.Accountant, PermissionKey = "zev.read" },
                new RolePermission { Role = MembershipRole.BuildingRepresentative, PermissionKey = "zev.read" },
                new RolePermission { Role = MembershipRole.OrganizationAdministrator, PermissionKey = "zev.read" },
                new RolePermission { Role = MembershipRole.OrganizationAdministrator, PermissionKey = "organization.read" },
                new RolePermission { Role = MembershipRole.Accountant, PermissionKey = "organization.read" },
                new RolePermission { Role = MembershipRole.OrganizationAdministrator, PermissionKey = "finance.manage" },
                new RolePermission { Role = MembershipRole.OrganizationAdministrator, PermissionKey = "legal.manage" },
                new RolePermission { Role = MembershipRole.OrganizationAdministrator, PermissionKey = "maintenance.manage" },
                new RolePermission { Role = MembershipRole.Manager, PermissionKey = "finance.manage" },
                new RolePermission { Role = MembershipRole.Manager, PermissionKey = "legal.manage" },
                new RolePermission { Role = MembershipRole.Manager, PermissionKey = "maintenance.manage" },
                new RolePermission { Role = MembershipRole.Accountant, PermissionKey = "finance.manage" },
                new RolePermission { Role = MembershipRole.BuildingRepresentative, PermissionKey = "maintenance.manage" });
        });

        modelBuilder.Entity<RefreshSession>(entity =>
        {
            entity.ToTable("RefreshSessions");
            entity.HasKey(session => session.Id);
            entity.Property(session => session.TokenHash).HasMaxLength(64).IsRequired();
            entity.Property(session => session.SecurityVersion).HasDefaultValue(0);
            entity.Property(session => session.RowVersion).IsRowVersion();
            entity.HasOne(session => session.User).WithMany().HasForeignKey(session => session.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(session => session.TokenHash).IsUnique();
            entity.HasIndex(session => new { session.UserId, session.ExpiresUtc });
        });

        modelBuilder.Entity<SystemSettings>(entity =>
        {
            entity.ToTable("SystemSettings", table =>
            {
                table.HasCheckConstraint("CK_SystemSettings_Singleton", "[Id] = 1");
                table.HasCheckConstraint("CK_SystemSettings_DecimalSeparator", "[DecimalSeparator] IN (',', '.')");
                table.HasCheckConstraint("CK_SystemSettings_DateFormat", "[DateFormat] IN ('DD.MM.YYYY', 'YYYY-MM-DD')");
                table.HasCheckConstraint("CK_SystemSettings_FontSize", "[FontSize] IN ('13px', '14px', '15px')");
            });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedNever();
            entity.Property(item => item.DecimalSeparator).HasMaxLength(1).IsRequired();
            entity.Property(item => item.DateFormat).HasMaxLength(20).IsRequired();
            entity.Property(item => item.FontSize).HasMaxLength(10).IsRequired();
            entity.Property(item => item.DefaultTheme).HasMaxLength(40).IsRequired();
            entity.HasOne<AppUser>().WithMany().HasForeignKey(item => item.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasData(new SystemSettings());
        });

        modelBuilder.Entity<SystemThemeSettings>(entity =>
        {
            entity.ToTable("SystemThemeSettings", table => table.HasCheckConstraint("CK_SystemThemeSettings_Singleton", "[Id] = 1"));
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Id).ValueGeneratedNever();
            entity.Property(item => item.ActiveThemeName).HasMaxLength(40).IsRequired();
            foreach (var color in SystemThemeSettings.ColorNames)
                entity.Property<string>(color).HasMaxLength(7).IsUnicode(false).IsRequired();
            entity.HasOne<AppUser>().WithMany().HasForeignKey(item => item.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasData(SystemThemeSettings.Factory());
        });

        modelBuilder.Entity<LookupType>(entity =>
        {
            entity.ToTable("LookupTypes");
            entity.HasKey(item => item.Code);
            entity.Property(item => item.Code).HasMaxLength(60);
            entity.Property(item => item.Name).HasMaxLength(120).IsRequired();
            entity.Property(item => item.Description).HasMaxLength(500);
            entity.HasData(LookupSeed.Types.Select(item => new { item.Code, item.Name, item.Description, item.IsSystem }));
        });

        modelBuilder.Entity<LookupItem>(entity =>
        {
            entity.ToTable("LookupItems", table =>
            {
                table.HasCheckConstraint("CK_LookupItems_SortOrder", "[SortOrder] >= 0");
                table.HasCheckConstraint("CK_LookupItems_DefaultIsActive", "[IsDefault] = 0 OR [IsActive] = 1");
            });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.LookupTypeCode).HasMaxLength(60).IsRequired();
            entity.Property(item => item.Code).HasMaxLength(60).IsRequired();
            entity.Property(item => item.Name).HasMaxLength(200).IsRequired();
            entity.HasOne(item => item.LookupType).WithMany(type => type.Items).HasForeignKey(item => item.LookupTypeCode)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ZevEntity>().WithMany().HasForeignKey(item => item.ZevId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<AppUser>().WithMany().HasForeignKey(item => item.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
            // One code per list and scope (platform-wide or one ZEV); at most one default per list and scope.
            entity.HasIndex(item => new { item.LookupTypeCode, item.ZevId, item.Code }).IsUnique();
            entity.HasIndex(item => new { item.LookupTypeCode, item.ZevId }).IsUnique().HasFilter("[IsDefault] = 1")
                .HasDatabaseName("IX_LookupItems_Default");
            entity.HasData(LookupSeed.Items.Select(item => new { item.Id, item.LookupTypeCode, item.Code, item.Name,
                item.SortOrder, item.IsActive, item.IsDefault, ZevId = (Guid?)null, item.CreatedUtc,
                UpdatedUtc = (DateTimeOffset?)null, UpdatedByUserId = (Guid?)null }));
        });

        modelBuilder.Entity<ModuleIconSetting>(entity =>
        {
            entity.ToTable("ModuleIconSettings");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.ModuleKey).HasMaxLength(60).IsRequired();
            entity.Property(item => item.IconKey).HasMaxLength(40).IsRequired();
            entity.HasIndex(item => item.ModuleKey).IsUnique();
            entity.HasOne<AppUser>().WithMany().HasForeignKey(item => item.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasData(ModuleIcons.Seed.Select(item => new { item.Id, item.ModuleKey, item.IconKey, item.UpdatedAt,
                UpdatedByUserId = (Guid?)null }));
        });

        modelBuilder.Entity<SystemErrorLog>(entity =>
        {
            entity.ToTable("SystemErrorLogs");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Title).HasMaxLength(SystemErrorLog.TitleLength).IsRequired();
            entity.Property(item => item.Source).HasConversion<string>().HasMaxLength(20);
            entity.Property(item => item.ErrorType).HasMaxLength(SystemErrorLog.ErrorTypeLength).IsRequired();
            entity.Property(item => item.Message).HasMaxLength(SystemErrorLog.MessageLength).IsRequired();
            entity.Property(item => item.StackTrace).HasMaxLength(SystemErrorLog.StackTraceLength);
            entity.Property(item => item.RequestPath).HasMaxLength(SystemErrorLog.PathLength);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(item => item.AdminNotes).HasMaxLength(SystemErrorLog.NotesLength);
            entity.HasIndex(item => new { item.Status, item.CreatedAt });
        });

        modelBuilder.Entity<AppVersionHistory>(entity =>
        {
            entity.ToTable("AppVersionHistory");
            entity.HasKey(item => item.Version);
            entity.Property(item => item.Version).HasMaxLength(20);
            entity.Property(item => item.Status).HasMaxLength(20).IsRequired();
            entity.Property(item => item.Title).HasMaxLength(200).IsRequired();
            entity.Property(item => item.InstalledBy).HasMaxLength(120).IsRequired();
            entity.Property(item => item.AdminNotes).HasMaxLength(AppVersionHistory.NotesLength);
            entity.HasData(AppVersions.Seed.Select(item => new { item.Version, item.Status, item.Title, item.InstalledAt,
                item.InstalledBy, AdminNotes = (string?)null, NotesUpdatedAt = (DateTimeOffset?)null,
                NotesUpdatedByUserId = (Guid?)null }));
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLogs");
            entity.HasKey(log => log.Id);
            entity.Property(log => log.Id).ValueGeneratedOnAdd();
            entity.Property(log => log.EntityType).HasMaxLength(100);
            entity.Property(log => log.EntityId).HasMaxLength(200);
            entity.Property(log => log.Action).HasMaxLength(40);
            entity.Property(log => log.CorrelationId).HasMaxLength(100);
            entity.HasIndex(log => new { log.OrganizationId, log.ZevId, log.OccurredUtc });
            entity.HasIndex(log => new { log.EntityType, log.EntityId, log.OccurredUtc });
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        SaveChangesAsync(true, cancellationToken);

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AddAuditEntries();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    public override int SaveChanges() => SaveChanges(true);

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AddAuditEntries();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    private void AddAuditEntries()
    {
        ChangeTracker.DetectChanges();
        if (ChangeTracker.Entries<FundTransaction>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<Invoice>().Any(entry => entry.State == EntityState.Deleted
                || entry.State == EntityState.Modified && (entry.Properties.Any(property => property.IsModified
                    && property.Metadata.Name != nameof(Invoice.DocumentStatus))
                    || entry.Property(invoice => invoice.DocumentStatus).OriginalValue is not
                        InvoiceDocumentStatus.Issued and not InvoiceDocumentStatus.Corrected
                    || entry.Property(invoice => invoice.DocumentStatus).OriginalValue == InvoiceDocumentStatus.Corrected
                        && entry.Property(invoice => invoice.DocumentStatus).CurrentValue != InvoiceDocumentStatus.Corrected
                    || entry.Property(invoice => invoice.DocumentStatus).CurrentValue is not
                        (InvoiceDocumentStatus.Cancelled or InvoiceDocumentStatus.Corrected)
                    || !ChangeTracker.Entries<FinancialDocument>().Any(document => document.State == EntityState.Added
                        && document.Entity.OriginalInvoiceId == entry.Entity.Id
                        && (entry.Entity.DocumentStatus == InvoiceDocumentStatus.Cancelled
                            ? document.Entity.Type == FinancialDocumentType.Storno
                            : document.Entity.Type is FinancialDocumentType.Credit or FinancialDocumentType.Debit))))
            || ChangeTracker.Entries<InvoiceLine>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<FinancialDocument>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<FinancialDocumentLine>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<Charge>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<PaymentAllocation>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<PaymentIdentificationHistory>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<Payment>().Any(entry => entry.State == EntityState.Deleted)
            || ChangeTracker.Entries<Payment>().Any(entry => entry.State == EntityState.Modified
                && (entry.Properties.Any(property => property.IsModified
                        && property.Metadata.Name is not nameof(Payment.PersonId) and not nameof(Payment.Status)
                            and not nameof(Payment.ReversedOn) and not nameof(Payment.ReversalReason)
                            and not nameof(Payment.ReversedByUserId))
                    || (entry.Property(payment => payment.ReversalReason).IsModified
                        || entry.Property(payment => payment.ReversedByUserId).IsModified)
                        && (!entry.Property(payment => payment.Status).IsModified
                            || entry.Property(payment => payment.ReversalReason).OriginalValue is not null
                            || entry.Property(payment => payment.ReversedByUserId).OriginalValue is not null)
                    || entry.Property(payment => payment.Status).IsModified
                        && (entry.Property(payment => payment.Status).OriginalValue != PaymentStatus.Posted
                            || entry.Property(payment => payment.Status).CurrentValue != PaymentStatus.Reversed
                            || entry.Property(payment => payment.ReversedOn).CurrentValue is null)
                    || entry.Property(payment => payment.ReversedOn).IsModified
                        && (entry.Property(payment => payment.ReversedOn).OriginalValue is not null
                            || entry.Property(payment => payment.Status).CurrentValue != PaymentStatus.Reversed)
                    || entry.Property(payment => payment.PersonId).IsModified
                        && !ChangeTracker.Entries<PaymentIdentificationHistory>().Any(history =>
                            history.State == EntityState.Added && history.Entity.PaymentId == entry.Entity.Id
                            && history.Entity.PreviousPersonId == entry.Property(payment => payment.PersonId).OriginalValue
                            && history.Entity.NewPersonId == entry.Property(payment => payment.PersonId).CurrentValue
                            && !string.IsNullOrWhiteSpace(history.Entity.Reason))))
            || ChangeTracker.Entries<AccessOverride>().Any(entry => entry.State == EntityState.Deleted
                || entry.State == EntityState.Modified && (entry.Properties.Any(property => property.IsModified
                        && property.Metadata.Name is not nameof(AccessOverride.RevokedUtc)
                            and not nameof(AccessOverride.RevokedByUserId) and not nameof(AccessOverride.RevokeReason))
                    || entry.Property(item => item.RevokedUtc).OriginalValue is not null
                    || entry.Property(item => item.RevokedUtc).CurrentValue is null
                    || entry.Property(item => item.RevokedByUserId).CurrentValue is null))
            || ChangeTracker.Entries<BankTransaction>().Any(entry => entry.State == EntityState.Deleted)
            || ChangeTracker.Entries<BankStatementImport>().Any(entry => entry.State == EntityState.Deleted)
            || ChangeTracker.Entries<Expense>().Any(entry => entry.State == EntityState.Deleted
                && entry.OriginalValues.GetValue<ExpenseStatus>(nameof(Expense.Status)) != ExpenseStatus.Draft)
            || ChangeTracker.Entries<Vote>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted)
            || ChangeTracker.Entries<BallotEligibility>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Posted financial and voting records are append-only.");
        }
        if (ChangeTracker.Entries<Ballot>().Any(entry => entry.State == EntityState.Deleted
            || (entry.State == EntityState.Modified && entry.OriginalValues.GetValue<BallotStatus>(nameof(Ballot.Status))
                == BallotStatus.Closed)))
        {
            throw new InvalidOperationException("Closed ballots are immutable.");
        }
        if (ChangeTracker.Entries<AuditLog>().Any(entry => entry.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("Audit records are immutable.");
        }

        var principal = httpContextAccessor.HttpContext?.User;
        Guid? actorId = Guid.TryParse(principal?.FindFirstValue("sub"), out var parsedId) ? parsedId : null;
        var correlationId = httpContextAccessor.HttpContext?.TraceIdentifier;
        var changes = ChangeTracker.Entries()
            .Where(entry => entry.Entity is ITenantRecord && entry.State is (EntityState.Added or EntityState.Modified or EntityState.Deleted))
            .ToList();

        foreach (var entry in changes)
        {
            var tenantRecord = (ITenantRecord)entry.Entity;
            var before = entry.State == EntityState.Added ? null : Snapshot(entry, false);
            var after = entry.State == EntityState.Deleted ? null : Snapshot(entry, true);
            AuditLogs.Add(new AuditLog
            {
                ActorUserId = actorId,
                OrganizationId = tenantRecord.OrganizationId,
                ZevId = tenantRecord.ZevId,
                EntityType = entry.Metadata.ClrType.Name,
                EntityId = string.Join("|", entry.Properties.Where(property => property.Metadata.IsPrimaryKey())
                    .Select(property => property.CurrentValue?.ToString() ?? string.Empty)),
                Action = AuditAction(entry),
                BeforeJson = before,
                AfterJson = after,
                CorrelationId = correlationId
            });
        }
    }

    // Permission overrides get business event names so the access history reads without diffing JSON.
    private static string AuditAction(EntityEntry entry) => entry.Entity switch
    {
        AccessOverride item when entry.State == EntityState.Added =>
            item.Effect == AccessEffect.Deny ? "PermissionDenied" : "PermissionGranted",
        AccessOverride when entry.State == EntityState.Modified
            && entry.Property(nameof(AccessOverride.RevokedUtc)).IsModified => "PermissionRevoked",
        ISoftDeletable item when entry.State == EntityState.Modified
            && entry.Property(nameof(ISoftDeletable.IsDeleted)).IsModified => item.IsDeleted ? "RecordDeleted" : "RecordRestored",
        _ => entry.State.ToString()
    };

    private static string Snapshot(EntityEntry entry, bool current)
    {
        var properties = entry.Properties
            .Where(property => !property.Metadata.IsShadowProperty()
                && property.Metadata.Name is not ("PasswordHash" or "TokenHash" or "RowVersion"))
            .ToDictionary(property => property.Metadata.Name, property => current ? property.CurrentValue : property.OriginalValue);
        return JsonSerializer.Serialize(properties);
    }
}
