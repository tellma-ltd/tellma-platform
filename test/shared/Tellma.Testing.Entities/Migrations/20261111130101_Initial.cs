using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.SqlServer.Types;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.EntityFrameworkCore.TableTypes.Operations;

#nullable disable

namespace Tellma.Testing.Entities.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.EnsureSchema(
                name: "core");

            migrationBuilder.EnsureSchema(
                name: "fixture");

            migrationBuilder.CreateSequence<int>(
                name: "sq_Blobs",
                schema: "core",
                startValue: 1000L);

            migrationBuilder.CreateSequence<int>(
                name: "sq_Jobs",
                schema: "core",
                startValue: 1000L);

            migrationBuilder.CreateSequence<int>(
                name: "sq_Links",
                schema: "fixture",
                startValue: 1000L);

            migrationBuilder.CreateSequence<int>(
                name: "sq_Nodes",
                schema: "fixture",
                startValue: 1000L);

            migrationBuilder.CreateSequence(
                name: "sq_Shipments",
                schema: "fixture",
                startValue: 1000L);

            migrationBuilder.CreateSequence<int>(
                name: "sq_Users",
                schema: "core",
                startValue: 1000L);

            migrationBuilder.CreateSequence<int>(
                name: "sq_WidgetPartNotes",
                schema: "fixture",
                startValue: 1000L);

            migrationBuilder.CreateSequence<int>(
                name: "sq_WidgetParts",
                schema: "fixture",
                startValue: 1000L);

            migrationBuilder.CreateSequence<int>(
                name: "sq_Widgets",
                schema: "fixture",
                startValue: 1000L);

            migrationBuilder.CreateTable(
                name: "__TellmaProvisioning",
                schema: "dbo",
                columns: table => new
                {
                    Step = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    PlatformVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___TellmaProvisioning", x => x.Step);
                });

            migrationBuilder.CreateTable(
                name: "__TellmaSchema",
                schema: "dbo",
                columns: table => new
                {
                    Fingerprint = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    MigrationId = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    PlatformVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK___TellmaSchema", x => x.Fingerprint);
                });

            migrationBuilder.CreateTable(
                name: "Blobs",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "varchar(64)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Blobs", x => x.Id);
                    table.CheckConstraint("CK_Blobs_Id", "[Id] > 0");
                });

            migrationBuilder.CreateTable(
                name: "Jobs",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.Id);
                    table.CheckConstraint("CK_Jobs_Id", "[Id] > 0");
                });

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                        .Annotation("Relational:DefaultConstraintName", "DF_Users_IsActive")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.CheckConstraint("CK_Users_Id", "[Id] > 0");
                });

            migrationBuilder.CreateTable(
                name: "VersionTags",
                schema: "core",
                columns: table => new
                {
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Tag = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_VersionTags_Tag")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VersionTags", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "Nodes",
                schema: "fixture",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "varchar(50)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Node = table.Column<SqlHierarchyId>(type: "hierarchyid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    ModifiedById = table.Column<int>(type: "int", nullable: false),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    SubtreeCount = table.Column<int>(type: "int", nullable: false, defaultValue: 1)
                        .Annotation("Relational:DefaultConstraintName", "DF_Nodes_SubtreeCount"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                        .Annotation("Relational:DefaultConstraintName", "DF_Nodes_IsActive"),
                    ActiveSubtreeCount = table.Column<int>(type: "int", nullable: false, defaultValue: 1)
                        .Annotation("Relational:DefaultConstraintName", "DF_Nodes_ActiveSubtreeCount")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Nodes", x => x.Id);
                    table.CheckConstraint("CK_Nodes_Id", "[Id] > 0");
                    table.ForeignKey(
                        name: "FK_Nodes_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Nodes_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Nodes_ParentId",
                        column: x => x.ParentId,
                        principalSchema: "fixture",
                        principalTable: "Nodes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Shipments",
                schema: "fixture",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    JobId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    ModifiedById = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shipments", x => x.Id);
                    table.CheckConstraint("CK_Shipments_Id", "[Id] > 0");
                    table.ForeignKey(
                        name: "FK_Shipments_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Shipments_JobId",
                        column: x => x.JobId,
                        principalSchema: "core",
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Shipments_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserStamps",
                schema: "core",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    LastActiveAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    PermissionsTag = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_UserStamps_PermissionsTag"),
                    PreferencesTag = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWID()")
                        .Annotation("Relational:DefaultConstraintName", "DF_UserStamps_PreferencesTag")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserStamps", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserStamps_UserId",
                        column: x => x.UserId,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Widgets",
                schema: "fixture",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "varchar(50)", nullable: true),
                    Kind = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Name2 = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Name3 = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(19,4)", nullable: true),
                    Settings = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ImageId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                        .Annotation("Relational:DefaultConstraintName", "DF_Widgets_IsActive"),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true),
                    ValidTo = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    ModifiedById = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Widgets", x => x.Id);
                    table.CheckConstraint("CK_Widgets_Id", "[Id] > 0");
                    table.ForeignKey(
                        name: "FK_Widgets_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Widgets_ImageId",
                        column: x => x.ImageId,
                        principalSchema: "core",
                        principalTable: "Blobs",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Widgets_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "WidgetsHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "fixture")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.CreateTable(
                name: "Links",
                schema: "fixture",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    NodeId = table.Column<int>(type: "int", nullable: false),
                    OtherNodeId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Links", x => x.Id);
                    table.CheckConstraint("CK_Links_Id", "[Id] > 0");
                    table.ForeignKey(
                        name: "FK_Links_NodeId",
                        column: x => x.NodeId,
                        principalSchema: "fixture",
                        principalTable: "Nodes",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Links_OtherNodeId",
                        column: x => x.OtherNodeId,
                        principalSchema: "fixture",
                        principalTable: "Nodes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WidgetExtras",
                schema: "fixture",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WidgetExtras", x => x.Id);
                    table.CheckConstraint("CK_WidgetExtras_Id", "[Id] > 0");
                    table.ForeignKey(
                        name: "FK_WidgetExtras_Id",
                        column: x => x.Id,
                        principalSchema: "fixture",
                        principalTable: "Widgets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WidgetParts",
                schema: "fixture",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    WidgetId = table.Column<int>(type: "int", nullable: false),
                    Ordinal = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    Settings = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true),
                    ValidTo = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WidgetParts", x => x.Id);
                    table.CheckConstraint("CK_WidgetParts_Id", "[Id] > 0");
                    table.ForeignKey(
                        name: "FK_WidgetParts_WidgetId",
                        column: x => x.WidgetId,
                        principalSchema: "fixture",
                        principalTable: "Widgets",
                        principalColumn: "Id");
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "WidgetPartsHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "fixture")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.CreateTable(
                name: "WidgetPartNotes",
                schema: "fixture",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    WidgetPartId = table.Column<int>(type: "int", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true),
                    ValidTo = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WidgetPartNotes", x => x.Id);
                    table.CheckConstraint("CK_WidgetPartNotes_Id", "[Id] > 0");
                    table.ForeignKey(
                        name: "FK_WidgetPartNotes_WidgetPartId",
                        column: x => x.WidgetPartId,
                        principalSchema: "fixture",
                        principalTable: "WidgetParts",
                        principalColumn: "Id");
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "WidgetPartNotesHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "fixture")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.CreateIndex(
                name: "IX_Links_NodeId",
                schema: "fixture",
                table: "Links",
                column: "NodeId")
                .Annotation("SqlServer:Include", new[] { "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Links_OtherNodeId",
                schema: "fixture",
                table: "Links",
                column: "OtherNodeId");

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_ParentId",
                schema: "fixture",
                table: "Nodes",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "UX_Nodes_Code",
                schema: "fixture",
                table: "Nodes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Nodes_Node",
                schema: "fixture",
                table: "Nodes",
                column: "Node",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Shipments_JobId",
                schema: "fixture",
                table: "Shipments",
                column: "JobId",
                unique: true,
                filter: "[JobId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WidgetPartNotes_WidgetPartId",
                schema: "fixture",
                table: "WidgetPartNotes",
                column: "WidgetPartId")
                .Annotation("SqlServer:Include", new[] { "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_WidgetParts_WidgetId",
                schema: "fixture",
                table: "WidgetParts",
                column: "WidgetId")
                .Annotation("SqlServer:Include", new[] { "Id" });

            migrationBuilder.CreateIndex(
                name: "UX_WidgetParts_WidgetId_Ordinal",
                schema: "fixture",
                table: "WidgetParts",
                columns: new[] { "WidgetId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Widgets_Code",
                schema: "fixture",
                table: "Widgets",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Widgets_ImageId",
                schema: "fixture",
                table: "Widgets",
                column: "ImageId",
                unique: true,
                filter: "[ImageId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Widgets_Subject",
                schema: "fixture",
                table: "Widgets",
                column: "Subject",
                unique: true);

            migrationBuilder.CreateTableType(
                name: "BigIdList",
                physicalName: "BigIdList_7629c0a3",
                schema: "dbo",
                scope: "tellma.fixture",
                definitionHash: "7629c0a3a446421ff892d55dadf08542aa91e81c0cc44094c98c8b1f8f855fc0",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "bigint" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CreateTableType(
                name: "DateList",
                physicalName: "DateList_566fd984",
                schema: "dbo",
                scope: "tellma.fixture",
                definitionHash: "566fd984369b0e179c71109162547c6116f323bba59c3b751b23bd82c1e97289",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "date" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CreateTableType(
                name: "GuidList",
                physicalName: "GuidList_922aa933",
                schema: "dbo",
                scope: "tellma.fixture",
                definitionHash: "922aa9332204e9ae67927ae2859ebd2257c9d8fc661d2a7d466ee23b65a556fd",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "uniqueidentifier" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CreateTableType(
                name: "IdList",
                physicalName: "IdList_9b0dfe3c",
                schema: "dbo",
                scope: "tellma.fixture",
                definitionHash: "9b0dfe3cc224df2fa3d3bd564e819ff2393ca1b74dbf4b3ad4cb72d30bbc3cd1",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CreateTableType(
                name: "IdStampList",
                physicalName: "IdStampList_36427f71",
                schema: "dbo",
                scope: "tellma.fixture",
                definitionHash: "36427f71498d797e4fa760e904a4e497235bf4a13838e4474980e12901da7d4c",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "ModifiedAt", StoreType = "datetimeoffset(7)" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CreateTableType(
                name: "StringList",
                physicalName: "StringList_bb8903a2",
                schema: "dbo",
                scope: "tellma.fixture",
                definitionHash: "bb8903a29566afaa31e26e0a6d2da5c1c4b678e9e3696420b40579d9859653fb",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "nvarchar(450)", MaxLength = 450 },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CreateTableType(
                name: "VersionTagList",
                physicalName: "VersionTagList_8fc97fb9",
                schema: "dbo",
                scope: "tellma.fixture",
                definitionHash: "8fc97fb9559068b857bd2a5130ea70c89f68b9735bed22801db4428de7a37a56",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Name", StoreType = "nvarchar(128)", MaxLength = 128 },
                    new TableTypeColumnDefinition { Name = "Tag", StoreType = "uniqueidentifier" },
                    new TableTypeColumnDefinition { Name = "Bumped", StoreType = "bit" },
                },
                primaryKey: new[] { "Name" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CreateTableType(
                name: "LinksList",
                physicalName: "LinksList_da254281",
                schema: "fixture",
                scope: "tellma.fixture",
                definitionHash: "da2542810a5568b5b1c705c0587adfd0654874877b3254e2ee704ceffd92ca3d",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "NodeId", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "OtherNodeId", StoreType = "int" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CreateTableType(
                name: "NodesList",
                physicalName: "NodesList_133dd19b",
                schema: "fixture",
                scope: "tellma.fixture",
                definitionHash: "133dd19ba1f09c77f18ad739896c5bf041eb495d62412bd0e9855ab842ddb284",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "Code", StoreType = "varchar(50)" },
                    new TableTypeColumnDefinition { Name = "Name", StoreType = "nvarchar(255)", MaxLength = 255 },
                    new TableTypeColumnDefinition { Name = "CreatedAt", StoreType = "datetimeoffset(7)", Precision = 7 },
                    new TableTypeColumnDefinition { Name = "CreatedById", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "ModifiedAt", StoreType = "datetimeoffset(7)", Precision = 7 },
                    new TableTypeColumnDefinition { Name = "ModifiedById", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "ParentId", StoreType = "int", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "SubtreeCount", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "IsActive", StoreType = "bit" },
                    new TableTypeColumnDefinition { Name = "ActiveSubtreeCount", StoreType = "int" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CreateTableType(
                name: "ShipmentsList",
                physicalName: "ShipmentsList_19cd4949",
                schema: "fixture",
                scope: "tellma.fixture",
                definitionHash: "19cd49496e07ce230fe28a48b4fd75101b157a504a8cc49cfcb4a0e4988d3efe",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "bigint" },
                    new TableTypeColumnDefinition { Name = "State", StoreType = "varchar(8)", MaxLength = 8 },
                    new TableTypeColumnDefinition { Name = "Payload", StoreType = "nvarchar(max)", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "JobId", StoreType = "int", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "CreatedAt", StoreType = "datetimeoffset(7)", Precision = 7 },
                    new TableTypeColumnDefinition { Name = "CreatedById", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "ModifiedAt", StoreType = "datetimeoffset(7)", Precision = 7 },
                    new TableTypeColumnDefinition { Name = "ModifiedById", StoreType = "int" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CreateTableType(
                name: "WidgetPartNotesList",
                physicalName: "WidgetPartNotesList_ff09ea87",
                schema: "fixture",
                scope: "tellma.fixture",
                definitionHash: "ff09ea87ca0e0276a412127e89607542c1d8b5a575f1cd965f275cad1f26d8cf",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "WidgetPartId", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "Text", StoreType = "nvarchar(max)" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CreateTableType(
                name: "WidgetPartsList",
                physicalName: "WidgetPartsList_7aefe66b",
                schema: "fixture",
                scope: "tellma.fixture",
                definitionHash: "7aefe66b5a376b9dbe7014846a4fb4fae428d609dfab3960cb7df17ae3052cfd",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "WidgetId", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "Ordinal", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "Notes", StoreType = "nvarchar(1024)", IsNullable = true, MaxLength = 1024 },
                    new TableTypeColumnDefinition { Name = "Settings", StoreType = "nvarchar(max)", IsNullable = true },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CreateTableType(
                name: "WidgetsList",
                physicalName: "WidgetsList_5b8e141b",
                schema: "fixture",
                scope: "tellma.fixture",
                definitionHash: "5b8e141b011a59bfc830bbaa72feb9738853eeeed95fcc36085d493c79b93882",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "Code", StoreType = "varchar(50)", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "Kind", StoreType = "varchar(8)", MaxLength = 8 },
                    new TableTypeColumnDefinition { Name = "Name", StoreType = "nvarchar(255)", MaxLength = 255 },
                    new TableTypeColumnDefinition { Name = "Name2", StoreType = "nvarchar(255)", IsNullable = true, MaxLength = 255 },
                    new TableTypeColumnDefinition { Name = "Name3", StoreType = "nvarchar(255)", IsNullable = true, MaxLength = 255 },
                    new TableTypeColumnDefinition { Name = "Subject", StoreType = "nvarchar(64)", MaxLength = 64 },
                    new TableTypeColumnDefinition { Name = "Price", StoreType = "decimal(19,4)", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "Settings", StoreType = "nvarchar(max)", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "ImageId", StoreType = "int", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "IsActive", StoreType = "bit" },
                    new TableTypeColumnDefinition { Name = "CreatedAt", StoreType = "datetimeoffset(7)", Precision = 7 },
                    new TableTypeColumnDefinition { Name = "CreatedById", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "ModifiedAt", StoreType = "datetimeoffset(7)", Precision = 7 },
                    new TableTypeColumnDefinition { Name = "ModifiedById", StoreType = "int" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CleanupTableTypes(scope: "tellma.fixture", keepList: new[] { "BigIdList_7629c0a3", "DateList_566fd984", "GuidList_922aa933", "IdList_9b0dfe3c", "IdStampList_36427f71", "StringList_bb8903a2", "VersionTagList_8fc97fb9", "LinksList_da254281", "NodesList_133dd19b", "ShipmentsList_19cd4949", "WidgetPartNotesList_ff09ea87", "WidgetPartsList_7aefe66b", "WidgetsList_5b8e141b" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "__TellmaProvisioning",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "__TellmaSchema",
                schema: "dbo");

            migrationBuilder.DropTable(
                name: "Links",
                schema: "fixture");

            migrationBuilder.DropTable(
                name: "Shipments",
                schema: "fixture");

            migrationBuilder.DropTable(
                name: "UserStamps",
                schema: "core");

            migrationBuilder.DropTable(
                name: "VersionTags",
                schema: "core");

            migrationBuilder.DropTable(
                name: "WidgetExtras",
                schema: "fixture");

            migrationBuilder.DropTable(
                name: "WidgetPartNotes",
                schema: "fixture")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "WidgetPartNotesHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "fixture")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.DropTable(
                name: "Nodes",
                schema: "fixture");

            migrationBuilder.DropTable(
                name: "Jobs",
                schema: "core");

            migrationBuilder.DropTable(
                name: "WidgetParts",
                schema: "fixture")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "WidgetPartsHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "fixture")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.DropTable(
                name: "Widgets",
                schema: "fixture")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "WidgetsHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "fixture")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.DropTable(
                name: "Users",
                schema: "core");

            migrationBuilder.DropTable(
                name: "Blobs",
                schema: "core");

            migrationBuilder.DropSequence(
                name: "sq_Blobs",
                schema: "core");

            migrationBuilder.DropSequence(
                name: "sq_Jobs",
                schema: "core");

            migrationBuilder.DropSequence(
                name: "sq_Links",
                schema: "fixture");

            migrationBuilder.DropSequence(
                name: "sq_Nodes",
                schema: "fixture");

            migrationBuilder.DropSequence(
                name: "sq_Shipments",
                schema: "fixture");

            migrationBuilder.DropSequence(
                name: "sq_Users",
                schema: "core");

            migrationBuilder.DropSequence(
                name: "sq_WidgetPartNotes",
                schema: "fixture");

            migrationBuilder.DropSequence(
                name: "sq_WidgetParts",
                schema: "fixture");

            migrationBuilder.DropSequence(
                name: "sq_Widgets",
                schema: "fixture");

            migrationBuilder.CleanupTableTypes(scope: "tellma.fixture", keepList: new string[0]);
        }
    }
}
