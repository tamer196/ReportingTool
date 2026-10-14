using System.Globalization;
using ReportingTool.Platform.Services.TimeZones;

namespace ReportingTool.Platform.Tests;

/// <summary>#95 AC3 and AC6: which IANA IDs users can choose, and the offset in force at an instant.</summary>
public sealed class TimeZoneServiceTests
{
    private readonly TimeZoneService _timeZones = new();

    [Fact]
    public void Utc_is_offered_first_and_the_rest_in_ordinal_order()
    {
        var ids = _timeZones.SelectableIds;

        Assert.Equal("UTC", ids[0]);
        Assert.Equal(ids.Skip(1).Order(StringComparer.Ordinal), ids.Skip(1));
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Theory]
    [InlineData("Asia/Dubai")]
    [InlineData("Asia/Kolkata")]
    [InlineData("Asia/Kathmandu")]
    [InlineData("America/New_York")]
    [InlineData("America/Argentina/Buenos_Aires")]
    [InlineData("Europe/London")]
    [InlineData("Europe/Kyiv")]
    public void Canonical_geographic_zones_are_offered(string id)
    {
        Assert.Contains(id, _timeZones.SelectableIds);
    }

    [Theory]
    [InlineData("Asia/Calcutta")] // legacy alias of Asia/Kolkata
    [InlineData("Europe/Kiev")] // legacy alias of Europe/Kyiv
    [InlineData("US/Eastern")] // legacy alias of America/New_York
    [InlineData("Etc/UTC")] // offered as "UTC"
    [InlineData("Etc/GMT")]
    [InlineData("Etc/GMT-4")]
    [InlineData("Etc/GMT+12")]
    [InlineData("EST5EDT")]
    [InlineData("GMT")]
    public void Aliases_fixed_offset_and_posix_style_zones_are_not_offered(string id)
    {
        Assert.DoesNotContain(id, _timeZones.SelectableIds);
    }

    [Fact]
    public void Every_offered_zone_except_utc_is_geographic()
    {
        Assert.All(_timeZones.SelectableIds.Skip(1), static id =>
        {
            Assert.Contains('/', id);
            Assert.DoesNotMatch("^Etc/", id);
        });
    }

    [Theory]
    [InlineData("Asia/Dubai", "Asia/Dubai")]
    [InlineData("Asia/Calcutta", "Asia/Kolkata")]
    [InlineData("Europe/Kiev", "Europe/Kyiv")]
    [InlineData("US/Eastern", "America/New_York")]
    [InlineData("America/Buenos_Aires", "America/Argentina/Buenos_Aires")]
    [InlineData("asia/DUBAI", "Asia/Dubai")]
    [InlineData(" Asia/Dubai ", "Asia/Dubai")]
    [InlineData("UTC", "UTC")]
    [InlineData("Etc/UTC", "UTC")]
    [InlineData("Etc/GMT", "UTC")]
    [InlineData("GMT", "UTC")]
    [InlineData("Zulu", "UTC")]
    public void Ids_and_legacy_aliases_map_to_their_selectable_id(string id, string expected)
    {
        Assert.Equal(expected, _timeZones.ToSelectableId(id));
    }

    [Theory]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("Etc/GMT-4")]
    [InlineData("EST5EDT")]
    [InlineData("+04:00")]
    [InlineData("")]
    [InlineData(null)]
    public void Unknown_and_unselectable_ids_have_no_selectable_id(string? id)
    {
        Assert.Null(_timeZones.ToSelectableId(id));
    }

    [Theory]
    // AC6: the offset in force at the instant, across the Europe/London changes in 2026.
    [InlineData("Europe/London", "2026-10-25T00:30:00Z", 60)]
    [InlineData("Europe/London", "2026-10-25T01:30:00Z", 0)]
    [InlineData("Europe/London", "2026-03-29T01:30:00Z", 60)]
    [InlineData("Europe/London", "2026-03-29T00:30:00Z", 0)]
    [InlineData("Asia/Kathmandu", "2026-10-13T05:00:00Z", 5 * 60 + 45)]
    [InlineData("Asia/Dubai", "2026-10-13T05:00:00Z", 4 * 60)]
    [InlineData("America/Sao_Paulo", "2026-10-13T05:00:00Z", -3 * 60)]
    [InlineData("UTC", "2026-10-13T05:00:00Z", 0)]
    public void The_offset_is_the_one_in_force_at_the_instant(string id, string instant, int expectedMinutes)
    {
        var offset = _timeZones.GetUtcOffset(id, DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture));

        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), offset);
    }

    [Theory]
    [InlineData("Asia/Calcutta")]
    [InlineData("asia/dubai")]
    [InlineData("Mars/Olympus_Mons")]
    public void An_offset_is_only_given_for_a_selectable_id(string id)
    {
        Assert.Throws<ArgumentException>(() => _timeZones.GetUtcOffset(id, DateTimeOffset.UnixEpoch));
    }
}
