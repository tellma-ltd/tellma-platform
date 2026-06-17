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
                physicalName: "DocumentStatesList_93a0c43e",
                schema: null,
                scope: "k4Rm9Tq2Xv7Bp3N",
                definitionHash: "93a0c43e11b4c7aba6c7fdb0fa6fee54ba0194656845e40e56fcfa1ab5761a9c",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "State", StoreType = "smallint" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "public" });

            migrationBuilder.CreateTableType(
                name: "CustomersList",
                physicalName: "CustomersList_db42b34d",
                schema: "crm",
                scope: "k4Rm9Tq2Xv7Bp3N",
                definitionHash: "db42b34d88f62a69bd80b584f1dc95f90df47443649b2a627e7ad34e00bfc6aa",
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
                physicalName: "BigIdList_a9b13815",
                schema: "dbo",
                scope: "k4Rm9Tq2Xv7Bp3N",
                definitionHash: "a9b13815ab14cfe455b023d8d3121cf883e74f225e15250df202119df6c1bf4e",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "bigint" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "public" });

            migrationBuilder.CreateTableType(
                name: "GuidList",
                physicalName: "GuidList_33c30b75",
                schema: "dbo",
                scope: "k4Rm9Tq2Xv7Bp3N",
                definitionHash: "33c30b75f6e16c2ef7d051042c18aa053224c217853b1ec683ced23935cff8b7",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "uniqueidentifier" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "public" });

            migrationBuilder.CreateTableType(
                name: "IdList",
                physicalName: "IdList_056d338e",
                schema: "dbo",
                scope: "k4Rm9Tq2Xv7Bp3N",
                definitionHash: "056d338e73b7e7923fe694fd5bbf2627ffc168fb2eee2422f8a8b807b0739677",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "public" });

            migrationBuilder.CreateTableType(
                name: "StringList",
                physicalName: "StringList_7e92fa5c",
                schema: "dbo",
                scope: "k4Rm9Tq2Xv7Bp3N",
                definitionHash: "7e92fa5cd9d29f3c9188279ca5489b42f1ffc6388d43f620b854b1888db27ff3",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "nvarchar(450)", MaxLength = 450 },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "public" });

            migrationBuilder.CreateTableType(
                name: "InvoiceLinesList",
                physicalName: "InvoiceLinesList_abfb7e4a",
                schema: "gl",
                scope: "k4Rm9Tq2Xv7Bp3N",
                definitionHash: "abfb7e4a74df113259b7c25b18333b8fe1da6816a88b2ab5f3d2fa385245defb",
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
                physicalName: "InvoicesList_02bbb892",
                schema: "gl",
                scope: "k4Rm9Tq2Xv7Bp3N",
                definitionHash: "02bbb892c63d3fe5c9bc7bb0524fd5f56472502a8392b0cc65ac7fcb8a54cb27",
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

            migrationBuilder.CleanupTableTypes(scope: "k4Rm9Tq2Xv7Bp3N", keepList: new[] { "DocumentStatesList_93a0c43e", "CustomersList_db42b34d", "BigIdList_a9b13815", "GuidList_33c30b75", "IdList_056d338e", "StringList_7e92fa5c", "InvoiceLinesList_abfb7e4a", "InvoicesList_02bbb892" });
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

            migrationBuilder.CleanupTableTypes(scope: "k4Rm9Tq2Xv7Bp3N", keepList: new string[0]);
        }
    }
}
