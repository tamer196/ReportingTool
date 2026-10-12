using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ReportingTool.Api.Persistence;

/// <summary>
/// Used only by <c>dotnet ef migrations add</c>. Building the model this way means adding a
/// migration needs no secrets, no vault and no running database: the placeholder connection
/// string is never opened. The service itself never uses this factory.
/// </summary>
public sealed class DesignTimeReportingDbContextFactory : IDesignTimeDbContextFactory<ReportingDbContext>
{
    public ReportingDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ReportingDbContext>()
            .UseSqlServer("Server=design-time-only;Database=ReportingTool")
            .Options);
}
