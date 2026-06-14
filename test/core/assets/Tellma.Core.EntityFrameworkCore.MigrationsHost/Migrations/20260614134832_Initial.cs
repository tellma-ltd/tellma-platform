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
                physicalName: "DocumentStatesList_7600ec35",
                schema: null,
                scope: "MigrationsHostContext",
                definitionHash: "7600ec3548ec8d1ba9662fda9294420466ae6580253cbcf7efe577f8e059ee5e",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "State", StoreType = "smallint" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "public" });

            migrationBuilder.CreateTableType(
                name: "CustomersList",
                physicalName: "CustomersList_5ae18c00",
                schema: "crm",
                scope: "MigrationsHostContext",
                definitionHash: "5ae18c0009af8bef8ff834e27eb7ba1211fb3e7bdf1af1ff4a9745e2757993b2",
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
                physicalName: "BigIdList_ffa129cb",
                schema: "dbo",
                scope: "MigrationsHostContext",
                definitionHash: "ffa129cb156492ae0a1d1d56a5a81239b1782ef8b18ddc7408247dc7ee59a974",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "bigint" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "public" });

            migrationBuilder.CreateTableType(
                name: "GuidList",
                physicalName: "GuidList_fff8df33",
                schema: "dbo",
                scope: "MigrationsHostContext",
                definitionHash: "fff8df33a9184a1fd362397f08f12f66766f883f04aea0e2dd686b4fec8c57dd",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "uniqueidentifier" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "public" });

            migrationBuilder.CreateTableType(
                name: "IdList",
                physicalName: "IdList_8a916c20",
                schema: "dbo",
                scope: "MigrationsHostContext",
                definitionHash: "8a916c20188fa76072d0d90dcf2a4816cdc0a61ed6ecfa0187401b76ff02bbfb",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "public" });

            migrationBuilder.CreateTableType(
                name: "StringList",
                physicalName: "StringList_8ef5389c",
                schema: "dbo",
                scope: "MigrationsHostContext",
                definitionHash: "8ef5389cbe76668deefbf5094c7c20c108d49bc46dd827223af8a2d127ea105e",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "nvarchar(450)", MaxLength = 450 },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "public" });

            migrationBuilder.CreateTableType(
                name: "InvoiceLinesList",
                physicalName: "InvoiceLinesList_70705451",
                schema: "gl",
                scope: "MigrationsHostContext",
                definitionHash: "70705451df4b770921d9f9b6b8634200ed7e35765e77a413042e4a1762e6db48",
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
                physicalName: "InvoicesList_982c9e85",
                schema: "gl",
                scope: "MigrationsHostContext",
                definitionHash: "982c9e852587b852abbfff3f5e354609733147594db49063bffc6048d4f33a8e",
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

            migrationBuilder.CleanupTableTypes(scope: "MigrationsHostContext", keepList: new[] { "DocumentStatesList_7600ec35", "CustomersList_5ae18c00", "BigIdList_ffa129cb", "GuidList_fff8df33", "IdList_8a916c20", "StringList_8ef5389c", "InvoiceLinesList_70705451", "InvoicesList_982c9e85" });
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
