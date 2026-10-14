using Microsoft.EntityFrameworkCore;

namespace ReportingTool.Reports.Persistence;

/// <summary>
/// The PoC's database. In the product these tables belong to three services (Catalog, Template,
/// Report; TDD v2 §2.2); here they share one database in separate schemas. The <c>src</c> schema
/// stands in for a source system that only the Query service reads.
/// </summary>
public sealed class ReportsDbContext : DbContext
{
    public const string ClientTransactionsDataset = "client-transactions";
    public const string SalariesDataset = "hr-salaries";

    public ReportsDbContext(DbContextOptions<ReportsDbContext> options) : base(options)
    {
    }

    public DbSet<Dataset> Datasets => Set<Dataset>();
    public DbSet<DatasetField> DatasetFields => Set<DatasetField>();
    public DbSet<DatasetGrant> DatasetGrants => Set<DatasetGrant>();
    public DbSet<ClientTransaction> ClientTransactions => Set<ClientTransaction>();
    public DbSet<EmployeeSalary> EmployeeSalaries => Set<EmployeeSalary>();
    public DbSet<ReportTemplate> Templates => Set<ReportTemplate>();
    public DbSet<ReportRun> Runs => Set<ReportRun>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.StoreDateTimesAsUtc();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Dataset>(e =>
        {
            e.ToTable("Datasets", "catalog");
            e.Property(static d => d.Id).HasMaxLength(64);
            e.HasMany(static d => d.Fields).WithOne().HasForeignKey(static f => f.DatasetId);
            e.HasData(
                new Dataset { Id = ClientTransactionsDataset, Name = "Client transactions", NameAr = "حركات العملاء", SourceTable = "src.ClientTransactions" },
                new Dataset { Id = SalariesDataset, Name = "Employee salaries", NameAr = "رواتب الموظفين", SourceTable = "src.EmployeeSalaries" });
        });

        modelBuilder.Entity<DatasetField>(e =>
        {
            e.ToTable("DatasetFields", "catalog");
            e.HasKey(static f => new { f.DatasetId, f.FieldKey });
            e.Property(static f => f.FieldKey).HasMaxLength(64);
            e.HasData(ClientTransactionFields().Concat(SalaryFields()));
        });

        modelBuilder.Entity<DatasetGrant>(e =>
        {
            e.ToTable("DatasetGrants", "catalog");
            e.HasKey(static g => new { g.DatasetId, g.Subject });
            e.Property(static g => g.Subject).HasMaxLength(128);
            // author.none has no grant at all; nobody in the PoC realm is granted the salaries.
            e.HasData(
                new DatasetGrant { DatasetId = ClientTransactionsDataset, Subject = "author.ar" },
                new DatasetGrant { DatasetId = ClientTransactionsDataset, Subject = "author.en" });
        });

        modelBuilder.Entity<ClientTransaction>(e =>
        {
            e.ToTable("ClientTransactions", "src");
            e.Property(static t => t.Amount).HasPrecision(18, 2);
            e.Property(static t => t.Quantity).HasPrecision(18, 2);
            e.HasIndex(static t => new { t.ClientId, t.TradeDate });
            e.HasIndex(static t => new { t.Branch, t.AccountManager });
        });

        modelBuilder.Entity<EmployeeSalary>(e =>
        {
            e.ToTable("EmployeeSalaries", "src");
            e.Property(static s => s.MonthlySalary).HasPrecision(18, 2);
        });

        modelBuilder.Entity<ReportTemplate>(e =>
        {
            e.ToTable("Templates", "tpl");
            e.Property(static t => t.OwnerSubject).HasMaxLength(128);
            e.Property(static t => t.Name).HasMaxLength(200);
            e.HasIndex(static t => new { t.OwnerSubject, t.Name }).IsUnique();
        });

        modelBuilder.Entity<ReportRun>(e =>
        {
            e.ToTable("Runs", "rpt");
            e.Property(static r => r.RequestedBy).HasMaxLength(128);
            e.Property(static r => r.Status).HasConversion<string>().HasMaxLength(16);
            e.HasIndex(static r => new { r.RequestedBy, r.CreatedAtUtc });
        });
    }

    private static IEnumerable<DatasetField> ClientTransactionFields()
    {
        var ordinal = 0;
        DatasetField F(string key, string label, string labelAr, string type, FieldVisibility visibility = FieldVisibility.Visible) =>
            new()
            {
                DatasetId = ClientTransactionsDataset,
                FieldKey = key,
                Label = label,
                LabelAr = labelAr,
                DataType = type,
                SourceColumn = key,
                Visibility = visibility,
                Ordinal = ++ordinal,
            };

        return
        [
            F("ClientId", "Client ID", "رقم العميل", "text"),
            F("ClientNameAr", "Client name (Arabic)", "اسم العميل", "text"),
            F("ClientNameEn", "Client name (English)", "اسم العميل بالإنجليزية", "text"),
            F("Branch", "Branch", "الفرع", "text"),
            F("AccountManager", "Account manager", "مدير الحساب", "text"),
            F("TradeDate", "Trade date", "تاريخ الصفقة", "date"),
            F("Instrument", "Instrument", "الأداة", "text"),
            F("Side", "Side", "الاتجاه", "text"),
            F("Quantity", "Quantity", "الكمية", "number"),
            F("Amount", "Amount", "المبلغ", "number"),
            F("Currency", "Currency", "العملة", "text"),
            F("Iban", "IBAN", "رقم الآيبان", "text", FieldVisibility.Masked),
            F("NationalId", "National ID", "الرقم الوطني", "text", FieldVisibility.Hidden),
            F("Notes", "Notes", "ملاحظات", "text"),
        ];
    }

    private static IEnumerable<DatasetField> SalaryFields() =>
    [
        new() { DatasetId = SalariesDataset, FieldKey = "EmployeeName", Label = "Employee", LabelAr = "الموظف", DataType = "text", SourceColumn = "EmployeeName", Ordinal = 1 },
        new() { DatasetId = SalariesDataset, FieldKey = "MonthlySalary", Label = "Monthly salary", LabelAr = "الراتب الشهري", DataType = "number", SourceColumn = "MonthlySalary", Ordinal = 2 },
    ];
}
