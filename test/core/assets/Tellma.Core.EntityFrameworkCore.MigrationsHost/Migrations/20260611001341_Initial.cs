using Microsoft.EntityFrameworkCore.Migrations;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.EntityFrameworkCore.TableTypes.Operations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Tellma.Core.EntityFrameworkCore.MigrationsHost.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "crm");

            migrationBuilder.EnsureSchema(
                name: "gl");

            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateSequence<int>(
                name: "sq_Customers",
                schema: "crm",
                startValue: 10000L);

            migrationBuilder.CreateSequence<int>(
                name: "sq_InvoiceLines",
                schema: "gl",
                startValue: 10000L);

            migrationBuilder.CreateSequence<int>(
                name: "sq_Invoices",
                schema: "gl",
                startValue: 10000L);

            migrationBuilder.CreateSequence<int>(
                name: "sq_Settings",
                schema: "dbo",
                startValue: 10000L);

            migrationBuilder.CreateTable(
                name: "Customers",
                schema: "crm",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    LoyaltyPoints = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    InternalNotes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                schema: "gl",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    Memo = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Total = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    TotalWithTax = table.Column<decimal>(type: "decimal(19,4)", nullable: false, computedColumnSql: "[Total] * 1.15", stored: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                schema: "dbo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceLines",
                schema: "gl",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    InvoiceId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(19,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceLines_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalSchema: "gl",
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "dbo",
                table: "Settings",
                columns: new[] { "Id", "Key", "Value" },
                values: new object[,]
                {
                    { 1, "System.Version", "1.0" },
                    { 2, "System.Locale", "en" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_InvoiceId",
                schema: "gl",
                table: "InvoiceLines",
                column: "InvoiceId");

            migrationBuilder.CreateTableType(
                name: "CustomersList",
                schema: "crm",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "LoyaltyPoints", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "Name", StoreType = "nvarchar(255)", MaxLength = 255 },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "public" });

            migrationBuilder.CreateTableType(
                name: "BigIdList",
                schema: "dbo",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "bigint" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "public" });

            migrationBuilder.CreateTableType(
                name: "GuidList",
                schema: "dbo",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "uniqueidentifier" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "public" });

            migrationBuilder.CreateTableType(
                name: "IdList",
                schema: "dbo",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "public" });

            migrationBuilder.CreateTableType(
                name: "StringList",
                schema: "dbo",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "nvarchar(450)", MaxLength = 450 },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "public" });

            migrationBuilder.CreateTableType(
                name: "InvoiceLinesList",
                schema: "gl",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "InvoiceId", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "Description", StoreType = "nvarchar(500)", MaxLength = 500 },
                    new TableTypeColumnDefinition { Name = "Quantity", StoreType = "decimal(19,4)" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "public" });

            migrationBuilder.CreateTableType(
                name: "InvoicesList",
                schema: "gl",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "CustomerId", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "Memo", StoreType = "nvarchar(255)", IsNullable = true, MaxLength = 255 },
                    new TableTypeColumnDefinition { Name = "Total", StoreType = "decimal(19,4)" },
                    new TableTypeColumnDefinition { Name = "RowVersion", StoreType = "binary(8)", IsNullable = true, MaxLength = 8, IsRowVersion = true },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "public" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTableType(name: "CustomersList", schema: "crm");

            migrationBuilder.DropTableType(name: "BigIdList", schema: "dbo");

            migrationBuilder.DropTableType(name: "GuidList", schema: "dbo");

            migrationBuilder.DropTableType(name: "IdList", schema: "dbo");

            migrationBuilder.DropTableType(name: "StringList", schema: "dbo");

            migrationBuilder.DropTableType(name: "InvoiceLinesList", schema: "gl");

            migrationBuilder.DropTableType(name: "InvoicesList", schema: "gl");

            migrationBuilder.DropTable(
                name: "Customers",
                schema: "crm");

            migrationBuilder.DropTable(
                name: "InvoiceLines",
                schema: "gl");

            migrationBuilder.DropTable(
                name: "Settings",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Invoices",
                schema: "gl");

            migrationBuilder.DropSequence(
                name: "sq_Customers",
                schema: "crm");

            migrationBuilder.DropSequence(
                name: "sq_InvoiceLines",
                schema: "gl");

            migrationBuilder.DropSequence(
                name: "sq_Invoices",
                schema: "gl");

            migrationBuilder.DropSequence(
                name: "sq_Settings",
                schema: "dbo");
        }
    }
}
