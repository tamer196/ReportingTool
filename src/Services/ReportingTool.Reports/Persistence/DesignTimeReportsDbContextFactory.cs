using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ReportingTool.Reports.Persistence;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> build the model without a database or secrets. The
/// connection string is a placeholder that is never opened.
/// </summary>
public sealed class DesignTimeReportsDbContextFactory : IDesignTimeDbContextFactory<ReportsDbContext>
{
    public ReportsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ReportsDbContext>()
            .UseSqlServer("Server=design-time-only;Database=ReportingToolReports")
            .Options);
}
