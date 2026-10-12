using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ReportingTool.Platform.Extensions;

namespace ReportingTool.Platform.Tests;

/// <summary>RT-106 AC 4: secrets come from the vault mount, one file per key, never from appsettings.</summary>
public sealed class VaultSecretsTests : IDisposable
{
    private readonly string _secretsDirectory = Path.Combine(Path.GetTempPath(), "rt-secrets-" + Guid.NewGuid().ToString("N"));

    public VaultSecretsTests() => Directory.CreateDirectory(_secretsDirectory);

    public void Dispose() => Directory.Delete(_secretsDirectory, recursive: true);

    [Fact]
    public void A_mounted_secret_file_becomes_a_configuration_key_and_wins_over_appsettings()
    {
        File.WriteAllText(Path.Combine(_secretsDirectory, "Database__ConnectionString"), "Server=from-vault");
        var builder = CreateBuilder(new()
        {
            ["Secrets:Directory"] = _secretsDirectory,
            ["Secrets:Required"] = "true",
            ["Database:ConnectionString"] = "Server=from-appsettings",
        });

        builder.AddVaultSecrets();

        Assert.Equal("Server=from-vault", builder.Configuration["Database:ConnectionString"]);
    }

    [Fact]
    public void A_required_but_missing_mount_stops_the_service_from_starting()
    {
        var builder = CreateBuilder(new()
        {
            ["Secrets:Directory"] = Path.Combine(_secretsDirectory, "not-mounted"),
            ["Secrets:Required"] = "true",
        });

        var error = Assert.Throws<InvalidOperationException>(() => builder.AddVaultSecrets());
        Assert.Contains("not mounted", error.Message);
    }

    [Fact]
    public void An_optional_missing_mount_is_skipped_on_a_developer_machine()
    {
        var builder = CreateBuilder(new()
        {
            ["Secrets:Directory"] = Path.Combine(_secretsDirectory, "not-mounted"),
            ["Secrets:Required"] = "false",
        });

        builder.AddVaultSecrets();

        Assert.Null(builder.Configuration["Database:ConnectionString"]);
    }

    [Fact]
    public void A_missing_Secrets_section_fails_fast_instead_of_defaulting()
    {
        var builder = CreateBuilder(new());

        var error = Assert.Throws<OptionsValidationException>(() => builder.AddVaultSecrets());
        Assert.Contains(error.Failures, static f => f.Contains("Directory"));
        Assert.Contains(error.Failures, static f => f.Contains("Required"));
    }

    private static WebApplicationBuilder CreateBuilder(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(settings);
        return builder;
    }
}
