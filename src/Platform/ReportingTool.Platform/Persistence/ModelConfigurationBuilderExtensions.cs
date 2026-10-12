using Microsoft.EntityFrameworkCore;

namespace ReportingTool.Platform.Persistence;

/// <summary>EF Core model conventions every Reporting Tool database shares.</summary>
public static class ModelConfigurationBuilderExtensions
{
    /// <summary>
    /// BR-14: stores every <see cref="DateTime"/> and <see cref="Nullable{DateTime}"/> property
    /// through <see cref="UtcDateTimeConverter"/>. Call it from each DbContext's
    /// <c>ConfigureConventions</c> so no entity can opt out by accident.
    /// </summary>
    public static ModelConfigurationBuilder StoreDateTimesAsUtc(this ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>();
        return configurationBuilder;
    }
}
