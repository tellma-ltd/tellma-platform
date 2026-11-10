using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.EntityFrameworkCore.TableTypes.Operations;

#nullable disable

namespace Tellma.Core.Migrator.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateSequence<int>(
                name: "sq_Tenants",
                schema: "catalog",
                startValue: 1000L);

            migrationBuilder.CreateTable(
                name: "CatalogState",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogState", x => x.Id);
                    table.CheckConstraint("CK_CatalogState_Id", "[Id] = 1");
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                schema: "catalog",
                columns: table => new
                {
                    Key = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Handle = table.Column<string>(type: "char(22)", unicode: false, fixedLength: true, maxLength: 22, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Subject = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: false, collation: "Latin1_General_100_BIN2"),
                    Sid = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Ticket = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    TokensVersion = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    RenewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                schema: "catalog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Name2 = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Name3 = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Language1 = table.Column<string>(type: "varchar(35)", unicode: false, maxLength: 35, nullable: false),
                    Language2 = table.Column<string>(type: "varchar(35)", unicode: false, maxLength: 35, nullable: true),
                    Language3 = table.Column<string>(type: "varchar(35)", unicode: false, maxLength: 35, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    LiveTenantId = table.Column<int>(type: "int", nullable: true),
                    State = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    StateReason = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    StateChangedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    StateChangedBy = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    Server = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Database = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CredentialProfile = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, defaultValue: "default")
                        .Annotation("Relational:DefaultConstraintName", "DF_Tenants_CredentialProfile"),
                    Properties = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                    table.CheckConstraint("CK_Tenants_Category", "[Category] IN (N'Live', N'Sandbox')");
                    table.CheckConstraint("CK_Tenants_Database", "[Database] NOT LIKE N'%[;=]%'");
                    table.CheckConstraint("CK_Tenants_LiveTenantId", "[Category] = N'Sandbox' OR [LiveTenantId] IS NULL");
                    table.CheckConstraint("CK_Tenants_Names", "((([Name2] IS NULL AND [Language2] IS NULL) OR ([Name2] IS NOT NULL AND [Language2] IS NOT NULL)) AND (([Name3] IS NULL AND [Language3] IS NULL) OR ([Name3] IS NOT NULL AND [Language3] IS NOT NULL)))");
                    table.CheckConstraint("CK_Tenants_Server", "[Server] NOT LIKE N'%[;=]%'");
                    table.CheckConstraint("CK_Tenants_State", "[State] IN (N'Provisioning', N'Active', N'ReadOnly', N'Frozen', N'Suspended', N'Retired')");
                    table.ForeignKey(
                        name: "FK_Tenants_LiveTenantId",
                        column: x => x.LiveTenantId,
                        principalSchema: "catalog",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TenantMemberships",
                schema: "catalog",
                columns: table => new
                {
                    Subject = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: false, collation: "Latin1_General_100_BIN2"),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantMemberships", x => new { x.Subject, x.TenantId });
                    table.ForeignKey(
                        name: "FK_TenantMemberships_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "catalog",
                        principalTable: "Tenants",
                        principalColumn: "Id");
                });

            migrationBuilder.InsertData(
                schema: "catalog",
                table: "CatalogState",
                columns: new[] { "Id", "ModifiedAt", "Version" },
                values: new object[] { 1, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), new Guid("00000000-0000-0000-0000-000000000001") });

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_ExpiresAt",
                schema: "catalog",
                table: "Sessions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_Sid",
                schema: "catalog",
                table: "Sessions",
                column: "Sid",
                filter: "[Sid] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_Subject",
                schema: "catalog",
                table: "Sessions",
                column: "Subject");

            migrationBuilder.CreateIndex(
                name: "IX_TenantMemberships_TenantId",
                schema: "catalog",
                table: "TenantMemberships",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_LiveTenantId",
                schema: "catalog",
                table: "Tenants",
                column: "LiveTenantId",
                filter: "[LiveTenantId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_State",
                schema: "catalog",
                table: "Tenants",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "UX_Tenants_Location",
                schema: "catalog",
                table: "Tenants",
                columns: new[] { "Server", "Database" },
                unique: true);

            migrationBuilder.CreateTableType(
                name: "TenantMembershipList",
                physicalName: "TenantMembershipList_b6a21e6c",
                schema: "catalog",
                scope: "tellma.catalog",
                definitionHash: "b6a21e6cc8e512fbc99e0b773c5597a2d72c922ef863a3cc824b7f1875c11444",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "TenantId", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "Subject", StoreType = "varchar(255)", Collation = "Latin1_General_100_BIN2" },
                    new TableTypeColumnDefinition { Name = "IsActive", StoreType = "bit" },
                },
                grants: new[] { "tellma_app" });

            migrationBuilder.CleanupTableTypes(scope: "tellma.catalog", keepList: new[] { "TenantMembershipList_b6a21e6c" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogState",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Sessions",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "TenantMemberships",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "Tenants",
                schema: "catalog");

            migrationBuilder.DropSequence(
                name: "sq_Tenants",
                schema: "catalog");

            migrationBuilder.CleanupTableTypes(scope: "tellma.catalog", keepList: new string[0]);
        }
    }
}
