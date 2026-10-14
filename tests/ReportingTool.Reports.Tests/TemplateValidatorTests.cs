using ReportingTool.Reports.Models.Layout;
using ReportingTool.Reports.Services.Templates;
using ReportingTool.Reports.Tests.Fakes;

namespace ReportingTool.Reports.Tests;

/// <summary>G3 at design time: a template can reference only what its author may see.</summary>
public sealed class TemplateValidatorTests
{
    private readonly TemplateValidator _validator = new();

    [Fact]
    public void A_template_within_the_authors_schema_is_valid()
    {
        var errors = _validator.Validate(Definitions.Report(), Definitions.AuthorSchema);

        Assert.Empty(errors);
    }

    [Fact]
    public void A_hidden_field_is_rejected_as_not_available()
    {
        var definition = Definitions.Report(body: [Definitions.Table(fields: ["ClientId", "NationalId"])]);

        var errors = _validator.Validate(definition, Definitions.AuthorSchema);

        Assert.Contains("Field not available: NationalId", errors);
    }

    [Fact]
    public void A_masked_field_may_be_shown()
    {
        var definition = Definitions.Report(body: [Definitions.Table(fields: ["ClientId", "Iban"])]);

        Assert.Empty(_validator.Validate(definition, Definitions.AuthorSchema));
    }

    [Theory]
    [InlineData("filter")]
    [InlineData("sort")]
    [InlineData("group")]
    public void A_masked_field_cannot_be_filtered_sorted_or_grouped(string use)
    {
        var definition = use switch
        {
            "filter" => Definitions.Report(bindings: [Definitions.Binding("tbl", filters: [new BindingFilter { Field = "Iban", Op = "eq", Parameter = "ToClient" }])]),
            "sort" => Definitions.Report(bindings: [Definitions.Binding("tbl", sort: ["Iban"])]),
            _ => Definitions.Report(body: [Definitions.Table(groupBy: "Iban")]),
        };

        var errors = _validator.Validate(definition, Definitions.AuthorSchema);

        Assert.Contains("Field not available for filtering, sorting or grouping: Iban", errors);
    }

    [Fact]
    public void A_dataset_without_a_grant_is_rejected()
    {
        var definition = Definitions.Report(bindings: [Definitions.Binding("tbl", dataset: "hr-salaries")]);

        var errors = _validator.Validate(definition, Definitions.AuthorSchema);

        Assert.Contains("Dataset not available: hr-salaries", errors);
    }

    [Fact]
    public void An_author_without_grants_cannot_bind_anything()
    {
        var errors = _validator.Validate(Definitions.Report(), []);

        Assert.Contains("Dataset not available: client-transactions", errors);
    }

    [Fact]
    public void A_data_component_without_a_binding_is_rejected()
    {
        var definition = Definitions.Report(bindings: []);

        var errors = _validator.Validate(definition, Definitions.AuthorSchema);

        Assert.Contains("Component tbl has no data binding.", errors);
    }

    [Fact]
    public void A_filter_must_be_fed_by_a_declared_parameter()
    {
        var definition = Definitions.Report(bindings: [Definitions.Binding("tbl", filters: [new BindingFilter { Field = "ClientId", Op = "eq", Parameter = "Sql" }])]);

        var errors = _validator.Validate(definition, Definitions.AuthorSchema);

        Assert.Contains("Unknown parameter: Sql", errors);
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("rtl\" onload=\"x")]
    public void Only_ltr_and_rtl_are_accepted_as_direction(string direction)
    {
        var errors = _validator.Validate(Definitions.Report(direction: direction), Definitions.AuthorSchema);

        Assert.Contains("Direction must be ltr or rtl.", errors);
    }
}
