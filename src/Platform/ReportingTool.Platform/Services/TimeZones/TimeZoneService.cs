using System.Collections.Frozen;
using NodaTime;
using NodaTime.TimeZones;

namespace ReportingTool.Platform.Services.TimeZones;

/// <summary>
/// <see cref="ITimeZoneService"/> over the IANA time zone database that ships inside NodaTime
/// (TD-12). The data is embedded in the package, so every service on the same NodaTime version
/// accepts exactly the same IDs, and a database update arrives as a normal package update.
/// </summary>
public sealed class TimeZoneService : ITimeZoneService
{
    /// <summary>The single ID users see for "no offset". The database's own name is <c>Etc/UTC</c>.</summary>
    public const string Utc = "UTC";

    /// <summary>Every ID and alias in the database, in any case, mapped to its selectable ID.</summary>
    private readonly FrozenDictionary<string, string> _selectableIdsByAnyId;

    public TimeZoneService()
    {
        var byAnyId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, canonicalId) in TzdbDateTimeZoneSource.Default.CanonicalIdMap)
        {
            var selectableId = ToSelectableCanonicalId(canonicalId);
            if (selectableId is not null)
                byAnyId[id] = selectableId;
        }
        byAnyId[Utc] = Utc;
        _selectableIdsByAnyId = byAnyId.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        SelectableIds = [Utc, .. byAnyId.Values.Where(static id => id != Utc).Distinct().Order(StringComparer.Ordinal)];
    }

    public IReadOnlyList<string> SelectableIds { get; }

    public string? ToSelectableId(string? id) =>
        id is not null && _selectableIdsByAnyId.TryGetValue(id.Trim(), out var selectableId) ? selectableId : null;

    public TimeSpan GetUtcOffset(string selectableId, DateTimeOffset instant)
    {
        if (ToSelectableId(selectableId) != selectableId)
            throw new ArgumentException($"'{selectableId}' is not a selectable time zone ID.", nameof(selectableId));

        // The database knows "UTC" too (an alias of Etc/UTC), so every selectable ID resolves.
        return DateTimeZoneProviders.Tzdb[selectableId].GetUtcOffset(Instant.FromDateTimeOffset(instant)).ToTimeSpan();
    }

    /// <summary>
    /// What a canonical database zone is offered as. Geographic zones (<c>Area/Location</c>) are
    /// offered as themselves. <c>Etc/UTC</c> and <c>Etc/GMT</c> both mean a zero offset for ever,
    /// so they become <c>UTC</c>. The other <c>Etc/GMT±N</c> zones have inverted signs
    /// (<c>Etc/GMT-4</c> is UTC+4) and no daylight saving, so a user who picks one gets wrong
    /// times half the year; they and the POSIX-style names without an area (<c>EST5EDT</c>) are
    /// not offered.
    /// </summary>
    private static string? ToSelectableCanonicalId(string canonicalId) =>
        canonicalId is "Etc/UTC" or "Etc/GMT" ? Utc
        : canonicalId.Contains('/') && !canonicalId.StartsWith("Etc/", StringComparison.Ordinal) ? canonicalId
        : null;
}
