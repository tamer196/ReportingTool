using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ReportingTool.Platform.Persistence;

/// <summary>
/// BR-14: every timestamp is stored in UTC. SQL Server's <c>datetime2</c> has no time zone, so
/// the rule is enforced at the boundary instead: a <see cref="DateTime"/> whose
/// <see cref="DateTime.Kind"/> is not <see cref="DateTimeKind.Utc"/> is rejected on write (a local
/// or unspecified time would be silently stored as if it were UTC), and every value read back is
/// marked UTC so it serializes with a trailing <c>Z</c>.
/// <para>Applied to every <see cref="DateTime"/> property by
/// <see cref="ModelConfigurationBuilderExtensions.StoreDateTimesAsUtc"/>.</para>
/// </summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(static value => EnsureUtc(value), static value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
    {
    }

    /// <summary>Returns <paramref name="value"/> unchanged when it is UTC; throws otherwise.</summary>
    public static DateTime EnsureUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : throw new InvalidOperationException(
                $"BR-14: only UTC timestamps can be stored, but a {value.Kind} DateTime was written. " +
                "Take the time from TimeProvider.GetUtcNow() or convert it with TimeZoneInfo before saving.");
}
