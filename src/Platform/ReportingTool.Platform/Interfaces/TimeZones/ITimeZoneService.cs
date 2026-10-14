namespace ReportingTool.Platform.Interfaces.TimeZones;

/// <summary>
/// The IANA time zones a user, a run or a schedule can be set to (TD-12, #95). Every service
/// validates zone IDs through this one list, and the picker in the browser is filled from it, so
/// the client and the server never disagree about which IDs are valid.
/// <para>Timestamps stay UTC (BR-14): a zone only decides how an instant is shown or how a
/// relative date is resolved. It is never applied to a stored value.</para>
/// </summary>
public interface ITimeZoneService
{
    /// <summary>
    /// The zones users can choose, <c>UTC</c> first and the rest in ordinal order: the canonical
    /// geographic zones of the time zone database. Legacy aliases (<c>Asia/Calcutta</c>), the
    /// fixed-offset <c>Etc/GMT±N</c> zones and the POSIX-style names (<c>EST5EDT</c>) are left out.
    /// </summary>
    IReadOnlyList<string> SelectableIds { get; }

    /// <summary>
    /// The selectable ID for <paramref name="id"/>, or null when there is none: a legacy alias
    /// becomes its canonical zone (<c>Asia/Calcutta</c> → <c>Asia/Kolkata</c>), every UTC or GMT
    /// spelling becomes <c>UTC</c>, and case is ignored. Values from outside the app (the
    /// identity provider's <c>zoneinfo</c> claim, the browser) go through here before they are saved.
    /// </summary>
    string? ToSelectableId(string? id);

    /// <summary>
    /// The UTC offset in force in <paramref name="selectableId"/> at <paramref name="instant"/>:
    /// the daylight-saving offset of that instant, not of today.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="selectableId"/> is not in <see cref="SelectableIds"/>.</exception>
    TimeSpan GetUtcOffset(string selectableId, DateTimeOffset instant);
}
