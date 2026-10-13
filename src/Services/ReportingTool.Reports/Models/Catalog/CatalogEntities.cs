namespace ReportingTool.Reports.Models.Catalog;

/// <summary>A published dataset (TDD v2 §3.4, reduced to what the PoC gates need).</summary>
public sealed class Dataset
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string NameAr { get; set; } = null!;

    /// <summary>Source table. Internal: never returned by any API, never accepted from a client.</summary>
    public string SourceTable { get; set; } = null!;

    public List<DatasetField> Fields { get; set; } = [];
}

public enum FieldVisibility
{
    Visible = 0,

    /// <summary>Listed, but values leave the Query service partially masked; cannot be filtered or sorted on (#15 AC4).</summary>
    Masked = 1,

    /// <summary>Never listed and never returned (#15 Hide).</summary>
    Hidden = 2,
}

public sealed class DatasetField
{
    public string DatasetId { get; set; } = null!;
    public string FieldKey { get; set; } = null!;
    public string Label { get; set; } = null!;
    public string LabelAr { get; set; } = null!;

    /// <summary><c>text</c>, <c>number</c> or <c>date</c>.</summary>
    public string DataType { get; set; } = null!;

    /// <summary>Source column. Internal, like <see cref="Dataset.SourceTable"/>.</summary>
    public string SourceColumn { get; set; } = null!;

    public FieldVisibility Visibility { get; set; }
    public int Ordinal { get; set; }
}

/// <summary>Who may use a dataset (BR-01). The subject is the identity provider's user name.</summary>
public sealed class DatasetGrant
{
    public string DatasetId { get; set; } = null!;
    public string Subject { get; set; } = null!;
}

/// <summary>Synthetic source rows (D-21): a broker's client transactions, Arabic and English.</summary>
public sealed class ClientTransaction
{
    public long Id { get; set; }
    public string ClientId { get; set; } = null!;
    public string ClientNameAr { get; set; } = null!;
    public string ClientNameEn { get; set; } = null!;
    public string Branch { get; set; } = null!;
    public string AccountManager { get; set; } = null!;
    public DateOnly TradeDate { get; set; }
    public string Instrument { get; set; } = null!;
    public string Side { get; set; } = null!;
    public decimal Quantity { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = null!;
    public string Iban { get; set; } = null!;
    public string NationalId { get; set; } = null!;
    public string Notes { get; set; } = null!;
}

/// <summary>A second, sensitive source that no PoC author is granted (G3 negative case).</summary>
public sealed class EmployeeSalary
{
    public long Id { get; set; }
    public string EmployeeName { get; set; } = null!;
    public decimal MonthlySalary { get; set; }
}
