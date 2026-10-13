using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ReportingTool.Reports.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPoc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "src");

            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.EnsureSchema(
                name: "rpt");

            migrationBuilder.EnsureSchema(
                name: "tpl");

            migrationBuilder.CreateTable(
                name: "ClientTransactions",
                schema: "src",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClientId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ClientNameAr = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ClientNameEn = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Branch = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    AccountManager = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TradeDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Instrument = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Side = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Iban = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NationalId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientTransactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DatasetGrants",
                schema: "catalog",
                columns: table => new
                {
                    DatasetId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatasetGrants", x => new { x.DatasetId, x.Subject });
                });

            migrationBuilder.CreateTable(
                name: "Datasets",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NameAr = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceTable = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Datasets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmployeeSalaries",
                schema: "src",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EmployeeName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MonthlySalary = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeSalaries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Runs",
                schema: "rpt",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateVersion = table.Column<int>(type: "int", nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ParametersJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Formats = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Pages = table.Column<int>(type: "int", nullable: true),
                    Rows = table.Column<long>(type: "bigint", nullable: true),
                    PdfPath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    XlsxPath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RenderedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MetricsJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Templates",
                schema: "tpl",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerSubject = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DefinitionJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DatasetFields",
                schema: "catalog",
                columns: table => new
                {
                    DatasetId = table.Column<string>(type: "nvarchar(64)", nullable: false),
                    FieldKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LabelAr = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DataType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceColumn = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Visibility = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatasetFields", x => new { x.DatasetId, x.FieldKey });
                    table.ForeignKey(
                        name: "FK_DatasetFields_Datasets_DatasetId",
                        column: x => x.DatasetId,
                        principalSchema: "catalog",
                        principalTable: "Datasets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "catalog",
                table: "DatasetGrants",
                columns: new[] { "DatasetId", "Subject" },
                values: new object[,]
                {
                    { "client-transactions", "author.ar" },
                    { "client-transactions", "author.en" }
                });

            migrationBuilder.InsertData(
                schema: "catalog",
                table: "Datasets",
                columns: new[] { "Id", "Name", "NameAr", "SourceTable" },
                values: new object[,]
                {
                    { "client-transactions", "Client transactions", "حركات العملاء", "src.ClientTransactions" },
                    { "hr-salaries", "Employee salaries", "رواتب الموظفين", "src.EmployeeSalaries" }
                });

            migrationBuilder.InsertData(
                schema: "catalog",
                table: "DatasetFields",
                columns: new[] { "DatasetId", "FieldKey", "DataType", "Label", "LabelAr", "Ordinal", "SourceColumn", "Visibility" },
                values: new object[,]
                {
                    { "client-transactions", "AccountManager", "text", "Account manager", "مدير الحساب", 5, "AccountManager", 0 },
                    { "client-transactions", "Amount", "number", "Amount", "المبلغ", 10, "Amount", 0 },
                    { "client-transactions", "Branch", "text", "Branch", "الفرع", 4, "Branch", 0 },
                    { "client-transactions", "ClientId", "text", "Client ID", "رقم العميل", 1, "ClientId", 0 },
                    { "client-transactions", "ClientNameAr", "text", "Client name (Arabic)", "اسم العميل", 2, "ClientNameAr", 0 },
                    { "client-transactions", "ClientNameEn", "text", "Client name (English)", "اسم العميل بالإنجليزية", 3, "ClientNameEn", 0 },
                    { "client-transactions", "Currency", "text", "Currency", "العملة", 11, "Currency", 0 },
                    { "client-transactions", "Iban", "text", "IBAN", "رقم الآيبان", 12, "Iban", 1 },
                    { "client-transactions", "Instrument", "text", "Instrument", "الأداة", 7, "Instrument", 0 },
                    { "client-transactions", "NationalId", "text", "National ID", "الرقم الوطني", 13, "NationalId", 2 },
                    { "client-transactions", "Notes", "text", "Notes", "ملاحظات", 14, "Notes", 0 },
                    { "client-transactions", "Quantity", "number", "Quantity", "الكمية", 9, "Quantity", 0 },
                    { "client-transactions", "Side", "text", "Side", "الاتجاه", 8, "Side", 0 },
                    { "client-transactions", "TradeDate", "date", "Trade date", "تاريخ الصفقة", 6, "TradeDate", 0 },
                    { "hr-salaries", "EmployeeName", "text", "Employee", "الموظف", 1, "EmployeeName", 0 },
                    { "hr-salaries", "MonthlySalary", "number", "Monthly salary", "الراتب الشهري", 2, "MonthlySalary", 0 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientTransactions_Branch_AccountManager",
                schema: "src",
                table: "ClientTransactions",
                columns: new[] { "Branch", "AccountManager" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientTransactions_ClientId_TradeDate",
                schema: "src",
                table: "ClientTransactions",
                columns: new[] { "ClientId", "TradeDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Runs_RequestedBy_CreatedAtUtc",
                schema: "rpt",
                table: "Runs",
                columns: new[] { "RequestedBy", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Templates_OwnerSubject_Name",
                schema: "tpl",
                table: "Templates",
                columns: new[] { "OwnerSubject", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientTransactions",
                schema: "src");

            migrationBuilder.DropTable(
                name: "DatasetFields",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "DatasetGrants",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "EmployeeSalaries",
                schema: "src");

            migrationBuilder.DropTable(
                name: "Runs",
                schema: "rpt");

            migrationBuilder.DropTable(
                name: "Templates",
                schema: "tpl");

            migrationBuilder.DropTable(
                name: "Datasets",
                schema: "catalog");
        }
    }
}
