namespace ReportingTool.Reports.Services.Templates;

/// <summary>
/// G3 at design time: a template can only reference what its author can see. The same check runs
/// again when a run starts, because grants can change between saving and running.
/// </summary>
public sealed class TemplateValidator : ITemplateValidator
{
    private static readonly HashSet<string> Formats = ["text", "date", "dateLong", "amount", "integer"];

    public IReadOnlyList<string> Validate(ReportDefinition definition, IReadOnlyList<DatasetSchema> schemas)
    {
        var errors = new List<string>();
        var layout = definition.Layout;

        if (definition.SchemaVersion != 1)
            errors.Add("Unsupported schemaVersion.");
        if (layout.Direction is not ("ltr" or "rtl"))
            errors.Add("Direction must be ltr or rtl.");
        if (layout.Page.Size is not ("A4" or "Letter") || layout.Page.Orientation is not ("portrait" or "landscape"))
            errors.Add("Page size must be A4 or Letter, portrait or landscape.");
        if (layout.PageNumbering is not ("document" or "perGroup"))
            errors.Add("Page numbering must be document or perGroup.");

        var componentIds = layout.Body.Select(static c => c.Id).ToHashSet();
        var parameters = definition.Parameters.Select(static p => p.Name).ToHashSet();
        var byDataset = schemas.ToDictionary(static s => s.Id);

        foreach (var binding in definition.Bindings)
        {
            if (!componentIds.Contains(binding.ComponentId))
                errors.Add($"Binding for unknown component: {binding.ComponentId}");
            if (!byDataset.TryGetValue(binding.DatasetId, out var schema))
            {
                errors.Add($"Dataset not available: {binding.DatasetId}");
                continue;
            }

            foreach (var filter in binding.Filters)
            {
                CheckClear(schema, filter.Field, errors);
                if (!parameters.Contains(filter.Parameter))
                    errors.Add($"Unknown parameter: {filter.Parameter}");
            }

            foreach (var sort in binding.Sort)
                CheckClear(schema, sort, errors);
        }

        foreach (var component in layout.Body)
        {
            var fields = component switch
            {
                TableComponent t => t.Columns.Select(static c => c.Field),
                ChartComponent c => [c.CategoryField, c.ValueField],
                KpiComponent k => [k.ValueField],
                _ => null,
            };
            if (fields is null)
                continue;

            var binding = definition.Bindings.FirstOrDefault(b => b.ComponentId == component.Id);
            if (binding is null)
            {
                errors.Add($"Component {component.Id} has no data binding.");
                continue;
            }

            if (!byDataset.TryGetValue(binding.DatasetId, out var schema))
                continue; // already reported

            foreach (var field in fields)
                CheckVisible(schema, field, errors);

            if (component is TableComponent table)
            {
                if (table.GroupBy is not null)
                    CheckClear(schema, table.GroupBy, errors);
                foreach (var column in table.Columns.Where(c => !Formats.Contains(c.Format)))
                    errors.Add($"Unknown format {column.Format} for {column.Field}.");
            }
        }

        return errors.Distinct().ToList();
    }

    private static void CheckVisible(DatasetSchema schema, string field, List<string> errors)
    {
        if (schema.Fields.All(f => f.Key != field))
            errors.Add($"Field not available: {field}");
    }

    private static void CheckClear(DatasetSchema schema, string field, List<string> errors)
    {
        var f = schema.Fields.FirstOrDefault(x => x.Key == field);
        if (f is null)
            errors.Add($"Field not available: {field}");
        else if (f.Masked)
            errors.Add($"Field not available for filtering, sorting or grouping: {field}");
    }
}
