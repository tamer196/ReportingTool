using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ReportingTool.Reports.HostedServices;

/// <summary>
/// Fills the synthetic source tables once (D-21: generated data, never real data): 3,000 clients
/// with 30 transactions each, in Arabic and English, with mixed numbers, dates, codes and Latin
/// product names. Several replicas start together, so the check-and-insert runs under an
/// application lock. Deterministic, so every machine renders the same evidence.
/// </summary>
public sealed class SyntheticDataSeeder : IHostedService
{
    private const int Clients = 3000;
    private const int TransactionsPerClient = 30;

    private static readonly (string Ar, string En)[] Names =
    [
        ("النور", "Al Noor"), ("الأمل", "Al Amal"), ("الفجر", "Al Fajr"), ("الرافدين", "Al Rafidain"), ("البتراء", "Petra"),
        ("الخليج", "Al Khaleej"), ("الواحة", "Al Waha"), ("السلام", "Al Salam"), ("الياسمين", "Al Yasmeen"), ("القمة", "Al Qimma"),
        ("الريادة", "Al Riyada"), ("الأفق", "Al Ofuq"),
    ];

    private static readonly (string Ar, string En)[] Kinds =
    [
        ("للتجارة", "Trading"), ("للاستثمار", "Investment"), ("القابضة", "Holding"), ("للخدمات المالية", "Financial Services"), ("للصناعات", "Industries"),
    ];

    private static readonly (string Ar, string En)[] People =
    [
        ("أحمد الخطيب", "Ahmad Al Khatib"), ("سارة العلي", "Sara Al Ali"), ("ليلى حداد", "Layla Haddad"), ("عمر النجار", "Omar Al Najjar"),
        ("يوسف الزعبي", "Yousef Al Zoubi"), ("رنا المصري", "Rana Al Masri"), ("خالد الحسيني", "Khaled Al Husseini"), ("هبة عبيدات", "Heba Obeidat"),
    ];

    private static readonly string[] Branches = ["عمّان", "دبي", "القاهرة", "الرياض"];
    private static readonly string[] Managers = ["م. طارق السعدي", "م. نور الهدى قاسم", "م. باسل عودة", "م. ديما الشريف", "م. فادي منصور", "م. لينا يعقوب", "م. سامر الكيلاني", "م. ريم البدور"];
    private static readonly string[] Instruments = ["EUR/USD", "GBP/USD", "USD/JPY", "ذهب XAU/USD", "نفط برنت Brent", "AAPL", "مؤشر داو جونز US30"];

    private readonly IServiceScopeFactory _scopes;
    private readonly ILoggerService _logger;

    public SyntheticDataSeeder(IServiceScopeFactory scopes, ILoggerService logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReportsDbContext>();
        await using var connection = new SqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(ct);

        await using (var lockCommand = new SqlCommand("EXEC sp_getapplock @Resource = 'rt108-seed', @LockMode = 'Exclusive', @LockTimeout = 120000", connection, tx))
            await lockCommand.ExecuteNonQueryAsync(ct);
        await using (var countCommand = new SqlCommand("SELECT COUNT_BIG(*) FROM src.ClientTransactions", connection, tx))
        {
            if ((long)(await countCommand.ExecuteScalarAsync(ct))! > 0)
            {
                _logger.Info("[SEED] Synthetic data already present.");
                return;
            }
        }

        using var table = BuildTransactions();
        using (var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.TableLock, tx) { DestinationTableName = "src.ClientTransactions", BatchSize = 10_000 })
        {
            foreach (DataColumn column in table.Columns)
                bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            await bulk.WriteToServerAsync(table, ct);
        }

        await using (var salaries = new SqlCommand(
                         "INSERT INTO src.EmployeeSalaries (EmployeeName, MonthlySalary) VALUES (N'موظف تجريبي', 4200.00), (N'Test Employee', 5100.00)", connection, tx))
            await salaries.ExecuteNonQueryAsync(ct);

        await tx.CommitAsync(ct);
        _logger.Info("[SEED] Inserted {Rows} synthetic client transactions.", table.Rows.Count);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private static DataTable BuildTransactions()
    {
        var random = new Random(108);
        var table = new DataTable();
        foreach (var (name, type) in new (string, Type)[]
                 {
                     ("ClientId", typeof(string)), ("ClientNameAr", typeof(string)), ("ClientNameEn", typeof(string)), ("Branch", typeof(string)),
                     ("AccountManager", typeof(string)), ("TradeDate", typeof(DateTime)), ("Instrument", typeof(string)), ("Side", typeof(string)),
                     ("Quantity", typeof(decimal)), ("Amount", typeof(decimal)), ("Currency", typeof(string)), ("Iban", typeof(string)),
                     ("NationalId", typeof(string)), ("Notes", typeof(string)),
                 })
            table.Columns.Add(name, type);

        var start = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Unspecified);
        for (var c = 1; c <= Clients; c++)
        {
            var (nameAr, nameEn) = c % 3 == 0
                ? People[random.Next(People.Length)]
                : (Pick(random, Names, Kinds, out var en), en);
            var clientId = $"CL-{c:D5}";
            var branch = Branches[c % Branches.Length];
            var manager = Managers[(c / 7) % Managers.Length];
            var iban = $"JO{random.Next(10, 99)}CBJO" + string.Concat(Enumerable.Range(0, 22).Select(_ => (char)('0' + random.Next(10))));
            var nationalId = $"9{random.Next(100_000_000, 999_999_999)}";

            for (var t = 0; t < TransactionsPerClient; t++)
            {
                var side = random.Next(2) == 0 ? "شراء" : "بيع";
                var amount = Math.Round((decimal)(random.NextDouble() * 50_000 + 10) * (random.Next(3) == 0 ? -1 : 1), 2);
                var ticket = random.Next(10_000, 99_999);
                var notes = (t % 5) switch
                {
                    0 => $"تنفيذ عبر منصة MT5 — تذكرة رقم {ticket}",
                    1 => $"إيداع نقدي عبر البنك العربي — Cash deposit #{ticket}",
                    2 => $"سحب إلى حساب IBAN ينتهي بـ {ticket % 10_000:D4}",
                    3 => $"عمولة تداول (Commission) {Math.Abs(amount) / 1000:0.00}%",
                    _ => "Swap overnight — تبييت",
                };
                table.Rows.Add(clientId, $"{nameAr}", nameEn, branch, manager, start.AddDays(random.Next(92)),
                    Instruments[random.Next(Instruments.Length)], side, Math.Round((decimal)random.NextDouble() * 10, 2),
                    amount, "USD", iban, nationalId, notes);
            }
        }

        return table;
    }

    private static string Pick(Random random, (string Ar, string En)[] names, (string Ar, string En)[] kinds, out string english)
    {
        var name = names[random.Next(names.Length)];
        var kind = kinds[random.Next(kinds.Length)];
        english = $"{name.En} {kind.En} LLC";
        return $"شركة {name.Ar} {kind.Ar}";
    }
}
