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
                name: "DocumentStatesList",
                physicalName: "DocumentStatesList_8e9d8d41",
                schema: null,
                scope: "MigrationsHostContext",
                definitionHash: "8e9d8d41c17c7aee1facb98bd205b4eb7ef59bf93aeba0e19c7f2f755f3c0079",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "State", StoreType = "smallint" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "public" });

            migrationBuilder.CreateTableType(
                name: "CustomersList",
                physicalName: "CustomersList_5ab7e59b",
                schema: "crm",
                scope: "MigrationsHostContext",
                definitionHash: "5ab7e59b3eb671b3e6cad740defc3ce355a5fcbd641115b2b32020d48db052db",
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
                physicalName: "BigIdList_4d89c60e",
                schema: "dbo",
                scope: "MigrationsHostContext",
                definitionHash: "4d89c60e1e47ace0ea8aca8651218c9c2ccbf936dbf7d4815e25fcf59879c5cc",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "bigint" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "public" });

            migrationBuilder.CreateTableType(
                name: "GuidList",
                physicalName: "GuidList_4b95d3ae",
                schema: "dbo",
                scope: "MigrationsHostContext",
                definitionHash: "4b95d3aeb947e024a9647b7d2bc25e3ffe300c5cf9d945c5d8407904eda636ea",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "uniqueidentifier" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "public" });

            migrationBuilder.CreateTableType(
                name: "IdList",
                physicalName: "IdList_971d1576",
                schema: "dbo",
                scope: "MigrationsHostContext",
                definitionHash: "971d15765cdb0fd63dd2ed32fc32d1dc1f983d2bc80c13ce308655f75b3c6c83",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "public" });

            migrationBuilder.CreateTableType(
                name: "StringList",
                physicalName: "StringList_d9623f56",
                schema: "dbo",
                scope: "MigrationsHostContext",
                definitionHash: "d9623f56321dc9c15532f9a1e24ff30bbf97481499bff53207f3b83dc97d3e3d",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "nvarchar(450)", MaxLength = 450 },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "public" });

            migrationBuilder.CreateTableType(
                name: "InvoiceLinesList",
                physicalName: "InvoiceLinesList_ec178301",
                schema: "gl",
                scope: "MigrationsHostContext",
                definitionHash: "ec1783012518b1430d517460e0465820f00eb440c53e9a93c430e9a7bf5d0bbb",
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
                physicalName: "InvoicesList_0bebc395",
                schema: "gl",
                scope: "MigrationsHostContext",
                definitionHash: "0bebc395d6ae75468d5e80a65fe616d0e44aa0efdf63c08c996b6f82dcdad4ff",
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

            migrationBuilder.CleanupTableTypes(scope: "MigrationsHostContext", keepList: new[] { "DocumentStatesList_8e9d8d41", "CustomersList_5ab7e59b", "BigIdList_4d89c60e", "GuidList_4b95d3ae", "IdList_971d1576", "StringList_d9623f56", "InvoiceLinesList_ec178301", "InvoicesList_0bebc395" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CleanupTableTypes(scope: "MigrationsHostContext", keepList: new string[0]);
        }
    }
}
