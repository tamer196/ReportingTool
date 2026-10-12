using Microsoft.EntityFrameworkCore;
using ReportingTool.Platform.Persistence;

namespace ReportingTool.Platform.Tests;

/// <summary>BR-14: timestamps are stored in UTC and come back marked as UTC.</summary>
public sealed class UtcDateTimeConverterTests
{
    private readonly UtcDateTimeConverter _converter = new();

    [Fact]
    public void Writing_a_utc_value_stores_it_unchanged()
    {
        var utc = new DateTime(2026, 10, 12, 9, 30, 0, DateTimeKind.Utc);

        Assert.Equal(utc, _converter.ConvertToProvider(utc));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Writing_a_non_utc_value_is_rejected(DateTimeKind kind)
    {
        var value = new DateTime(2026, 10, 12, 9, 30, 0, kind);

        var error = Assert.Throws<InvalidOperationException>(() => _converter.ConvertToProvider(value));
        Assert.Contains("BR-14", error.Message);
    }

    [Fact]
    public void Reading_marks_the_value_as_utc()
    {
        var stored = new DateTime(2026, 10, 12, 9, 30, 0, DateTimeKind.Unspecified);

        var read = Assert.IsType<DateTime>(_converter.ConvertFromProvider(stored));

        Assert.Equal(DateTimeKind.Utc, read.Kind);
        Assert.Equal(stored.Ticks, read.Ticks);
    }

    [Theory]
    [InlineData(nameof(SampleEntity.HappenedAtUtc))]
    [InlineData(nameof(SampleEntity.FinishedAtUtc))]
    public void StoreDateTimesAsUtc_applies_the_converter_to_every_DateTime_property(string propertyName)
    {
        using var db = new SampleDbContext();

        var property = db.Model.FindEntityType(typeof(SampleEntity))!.FindProperty(propertyName)!;

        Assert.IsType<UtcDateTimeConverter>(property.GetValueConverter());
    }

    public sealed class SampleEntity
    {
        public int Id { get; set; }
        public DateTime HappenedAtUtc { get; set; }
        public DateTime? FinishedAtUtc { get; set; }
    }

    private sealed class SampleDbContext : DbContext
    {
        public DbSet<SampleEntity> Samples => Set<SampleEntity>();

        // Building the model never opens this connection.
        protected override void OnConfiguring(DbContextOptionsBuilder options) =>
            options.UseSqlServer("Server=unused;Database=unused");

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.StoreDateTimesAsUtc();
    }
}
