using System.Text.Json;

namespace ReportingTool.Reports.Models.Layout;

/// <summary>One JSON shape for definitions everywhere: the HTTP API, storage and the render job.</summary>
public static class ReportJson
{
    public static readonly JsonSerializerOptions Options = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    /// <summary>The designer may send the "type" discriminator anywhere in a component object.</summary>
    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.AllowOutOfOrderMetadataProperties = true;
        return options;
    }
}
