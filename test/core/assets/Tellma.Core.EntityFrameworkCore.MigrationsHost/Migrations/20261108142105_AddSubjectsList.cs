using Microsoft.EntityFrameworkCore.Migrations;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.EntityFrameworkCore.TableTypes.Operations;

#nullable disable

namespace Tellma.Core.EntityFrameworkCore.MigrationsHost.Migrations
{
    /// <inheritdoc />
    public partial class AddSubjectsList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.CreateTableType(
                name: "SubjectsList",
                physicalName: "SubjectsList_48f699b9",
                schema: "dbo",
                scope: "k4Rm9Tq2Xv7Bp3N",
                definitionHash: "48f699b9ac18ccb1b62095e45b9b56cb2ced02597ea6c8fb70489efb4bb0451e",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Subject", StoreType = "nvarchar(255)", MaxLength = 255, Collation = "Latin1_General_100_BIN2" },
                    new TableTypeColumnDefinition { Name = "DisplayName", StoreType = "nvarchar(200)", IsNullable = true, MaxLength = 200 },
                },
                primaryKey: new[] { "Subject" },
                grants: new[] { "public" });

            migrationBuilder.CleanupTableTypes(scope: "k4Rm9Tq2Xv7Bp3N", keepList: new[] { "DocumentStatesList_93a0c43e", "CustomersList_db42b34d", "BigIdList_a9b13815", "GuidList_33c30b75", "IdList_056d338e", "StringList_7e92fa5c", "SubjectsList_48f699b9", "InvoiceLinesList_abfb7e4a", "InvoicesList_02bbb892" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CleanupTableTypes(scope: "k4Rm9Tq2Xv7Bp3N", keepList: new[] { "DocumentStatesList_93a0c43e", "CustomersList_db42b34d", "BigIdList_a9b13815", "GuidList_33c30b75", "IdList_056d338e", "StringList_7e92fa5c", "InvoiceLinesList_abfb7e4a", "InvoicesList_02bbb892" });
        }
    }
}
