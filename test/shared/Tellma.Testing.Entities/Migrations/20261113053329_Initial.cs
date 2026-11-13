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
                name: "sq_Lookups",
                schema: "fixture",
                startValue: 1000L);

            migrationBuilder.CreateSequence<int>(
                name: "sq_Nodes",
                schema: "fixture",
                startValue: 1000L);

            migrationBuilder.CreateSequence<int>(
                name: "sq_Permissions",
                schema: "core",
                startValue: 1000L);

            migrationBuilder.CreateSequence<int>(
                name: "sq_RoleMemberships",
                schema: "core",
                startValue: 1000L);

            migrationBuilder.CreateSequence<int>(
                name: "sq_Roles",
                schema: "core",
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
                name: "Blobs",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    State = table.Column<string>(type: "varchar(9)", unicode: false, maxLength: 9, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Width = table.Column<int>(type: "int", nullable: true),
                    Height = table.Column<int>(type: "int", nullable: true),
                    Variants = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: false),
                    CommittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    Sha256 = table.Column<byte[]>(type: "binary(32)", fixedLength: true, maxLength: 32, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Blobs", x => x.Id);
                    table.CheckConstraint("CK_Blobs_Id", "[Id] > 0");
                });

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValue: "Individual")
                        .Annotation("Relational:DefaultConstraintName", "DF_Users_Kind"),
                    Subject = table.Column<string>(type: "varchar(255)", nullable: true, collation: "Latin1_General_100_BIN2"),
                    Email = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    State = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false, computedColumnSql: "ISNULL(CAST(CASE WHEN [JoinedAt] IS NOT NULL THEN 'Joined' WHEN [InvitedAt] IS NOT NULL THEN 'Invited' ELSE 'New' END AS varchar(8)), 'New')", stored: true),
                    InvitedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    InviteStatus = table.Column<string>(type: "varchar(9)", unicode: false, maxLength: 9, nullable: true),
                    LastInviteError = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    JoinedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Name2 = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Name3 = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ImageId = table.Column<int>(type: "int", nullable: true),
                    SignatureId = table.Column<int>(type: "int", nullable: true),
                    PreferredLanguage = table.Column<string>(type: "varchar(35)", nullable: true),
                    PreferredCalendar = table.Column<string>(type: "varchar(16)", nullable: true),
                    PreferredTimeZone = table.Column<string>(type: "varchar(64)", nullable: true),
                    PreferredRegion = table.Column<string>(type: "varchar(2)", nullable: true),
                    PreferredDigits = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: true),
                    Gender = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: true),
                    ContactEmail = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ContactMobile = table.Column<string>(type: "varchar(32)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                        .Annotation("Relational:DefaultConstraintName", "DF_Users_IsActive"),
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
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.CheckConstraint("CK_Users_Id", "[Id] > 0");
                    table.CheckConstraint("CK_Users_IndividualEvidence", "[Kind] <> 'Individual' OR ((([Subject] IS NULL AND [InvitedAt] IS NULL) OR ([Subject] IS NOT NULL AND [InvitedAt] IS NOT NULL)) AND ([JoinedAt] IS NULL OR [InvitedAt] IS NOT NULL))");
                    table.CheckConstraint("CK_Users_KindEmail", "([Kind] = 'Individual' AND [Email] IS NOT NULL) OR ([Kind] <> 'Individual' AND [Email] IS NULL)");
                    table.CheckConstraint("CK_Users_ServiceEvidence", "[Kind] <> 'Service' OR ([InvitedAt] IS NULL AND [InviteStatus] IS NULL AND (([Subject] IS NULL AND [JoinedAt] IS NULL) OR ([Subject] IS NOT NULL AND [JoinedAt] IS NOT NULL)))");
                    table.CheckConstraint("CK_Users_SystemHasNoSubject", "[Kind] <> 'System' OR [Subject] IS NULL");
                    table.CheckConstraint("CK_Users_SystemIsRowOne", "[Kind] <> 'System' OR [Id] = 1");
                    table.ForeignKey(
                        name: "FK_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Users_ImageId",
                        column: x => x.ImageId,
                        principalSchema: "core",
                        principalTable: "Blobs",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Users_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Users_SignatureId",
                        column: x => x.SignatureId,
                        principalSchema: "core",
                        principalTable: "Blobs",
                        principalColumn: "Id");
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "UsersHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "core")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.CreateTable(
                name: "Lookups",
                schema: "fixture",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "varchar(50)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                        .Annotation("Relational:DefaultConstraintName", "DF_Lookups_IsActive"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    ModifiedById = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lookups", x => x.Id);
                    table.CheckConstraint("CK_Lookups_Id", "[Id] > 0");
                    table.ForeignKey(
                        name: "FK_Lookups_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Lookups_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
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
                name: "Roles",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Name2 = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Name3 = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                        .Annotation("Relational:DefaultConstraintName", "DF_Roles_IsPublic"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                        .Annotation("Relational:DefaultConstraintName", "DF_Roles_IsActive"),
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
                    table.PrimaryKey("PK_Roles", x => x.Id);
                    table.CheckConstraint("CK_Roles_Id", "[Id] > 0");
                    table.ForeignKey(
                        name: "FK_Roles_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Roles_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "RolesHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "core")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

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
                name: "UserPreferences",
                schema: "core",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Key = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPreferences", x => new { x.UserId, x.Key });
                    table.ForeignKey(
                        name: "FK_UserPreferences_UserId",
                        column: x => x.UserId,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                        .Annotation("Relational:DefaultConstraintName", "DF_UserStamps_PreferencesTag"),
                    InboxSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    InboxVersion = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
                        .Annotation("Relational:DefaultConstraintName", "DF_UserStamps_InboxVersion"),
                    AssignmentsSeenAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true)
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
                    OptionalKind = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: true),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: true),
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
                name: "Permissions",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    Resource = table.Column<string>(type: "varchar(128)", nullable: false),
                    Action = table.Column<string>(type: "varchar(32)", nullable: false),
                    Filter = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    FilterQueryexVersion = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true),
                    ValidTo = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Permissions", x => x.Id);
                    table.CheckConstraint("CK_Permissions_FilterVersion", "([Filter] IS NULL AND [FilterQueryexVersion] IS NULL) OR ([Filter] IS NOT NULL AND [FilterQueryexVersion] IS NOT NULL)");
                    table.CheckConstraint("CK_Permissions_Id", "[Id] > 0");
                    table.CheckConstraint("CK_Permissions_WildcardHasNoFilter", "[Resource] <> '*' OR [Filter] IS NULL");
                    table.ForeignKey(
                        name: "FK_Permissions_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "core",
                        principalTable: "Roles",
                        principalColumn: "Id");
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "PermissionsHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "core")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.CreateTable(
                name: "RoleMemberships",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    EndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    ValidFrom = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodStartColumn", true),
                    ValidTo = table.Column<DateTime>(type: "datetime2", nullable: false)
                        .Annotation("SqlServer:TemporalIsPeriodEndColumn", true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleMemberships", x => x.Id);
                    table.CheckConstraint("CK_RoleMemberships_Id", "[Id] > 0");
                    table.CheckConstraint("CK_RoleMemberships_Window", "[EndsAt] > [StartsAt]");
                    table.ForeignKey(
                        name: "FK_RoleMemberships_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "core",
                        principalTable: "Roles",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RoleMemberships_UserId",
                        column: x => x.UserId,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "RoleMembershipsHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "core")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

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

            migrationBuilder.InsertData(
                schema: "core",
                table: "Users",
                columns: new[] { "Id", "ContactEmail", "ContactMobile", "CreatedAt", "CreatedById", "Email", "Gender", "ImageId", "InviteStatus", "InvitedAt", "IsActive", "JoinedAt", "Kind", "LastInviteError", "ModifiedAt", "ModifiedById", "Name", "Name2", "Name3", "PreferredCalendar", "PreferredDigits", "PreferredLanguage", "PreferredRegion", "PreferredTimeZone", "SignatureId", "Subject" },
                values: new object[] { 1, null, null, new DateTimeOffset(new DateTime(2000, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 1, null, null, null, null, null, true, new DateTimeOffset(new DateTime(2000, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), "System", null, new DateTimeOffset(new DateTime(2000, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 1, "System", null, null, null, null, null, null, null, null, null });

            migrationBuilder.InsertData(
                schema: "core",
                table: "Roles",
                columns: new[] { "Id", "Code", "CreatedAt", "CreatedById", "IsActive", "ModifiedAt", "ModifiedById", "Name", "Name2", "Name3" },
                values: new object[] { 1, "Administrator", new DateTimeOffset(new DateTime(2000, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 1, true, new DateTimeOffset(new DateTime(2000, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 1, "Administrator", null, null });

            migrationBuilder.InsertData(
                schema: "core",
                table: "UserStamps",
                columns: new[] { "UserId", "AssignmentsSeenAt", "InboxSeenAt", "LastActiveAt", "PermissionsTag", "PreferencesTag" },
                values: new object[] { 1, null, null, null, new Guid("00000000-0000-0000-0000-000000000001"), new Guid("00000000-0000-0000-0000-000000000002") });

            migrationBuilder.InsertData(
                schema: "core",
                table: "Permissions",
                columns: new[] { "Id", "Action", "Filter", "FilterQueryexVersion", "Notes", "Resource", "RoleId" },
                values: new object[] { 1, "*", null, null, null, "*", 1 });

            migrationBuilder.InsertData(
                schema: "core",
                table: "RoleMemberships",
                columns: new[] { "Id", "EndsAt", "Notes", "RoleId", "StartsAt", "UserId" },
                values: new object[] { 1, null, null, 1, null, 1 });

            migrationBuilder.CreateIndex(
                name: "IX_Blobs_Expiry",
                schema: "core",
                table: "Blobs",
                column: "ExpiresAt",
                filter: "[ExpiresAt] IS NOT NULL")
                .Annotation("SqlServer:Include", new[] { "Kind", "StorageKey", "Variants", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_Blobs_StagedBy",
                schema: "core",
                table: "Blobs",
                column: "CreatedById",
                filter: "[State] = 'Staged'")
                .Annotation("SqlServer:Include", new[] { "Size", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "UX_Blobs_StorageKey",
                schema: "core",
                table: "Blobs",
                column: "StorageKey",
                unique: true);

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
                name: "UX_Lookups_Code",
                schema: "fixture",
                table: "Lookups",
                column: "Code",
                unique: true);

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
                name: "IX_Permissions_RoleId",
                schema: "core",
                table: "Permissions",
                column: "RoleId")
                .Annotation("SqlServer:Include", new[] { "Resource", "Action", "Filter", "FilterQueryexVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleMemberships_RoleId_UserId",
                schema: "core",
                table: "RoleMemberships",
                columns: new[] { "RoleId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleMemberships_UserId_RoleId",
                schema: "core",
                table: "RoleMemberships",
                columns: new[] { "UserId", "RoleId" })
                .Annotation("SqlServer:Include", new[] { "StartsAt", "EndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Roles_IsPublic",
                schema: "core",
                table: "Roles",
                column: "Id",
                filter: "[IsPublic] = 1");

            migrationBuilder.CreateIndex(
                name: "UX_Roles_Code",
                schema: "core",
                table: "Roles",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Roles_Name",
                schema: "core",
                table: "Roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Roles_Name2",
                schema: "core",
                table: "Roles",
                column: "Name2",
                unique: true,
                filter: "[Name2] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Roles_Name3",
                schema: "core",
                table: "Roles",
                column: "Name3",
                unique: true,
                filter: "[Name3] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Shipments_JobId",
                schema: "fixture",
                table: "Shipments",
                column: "JobId",
                unique: true,
                filter: "[JobId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Users_IsActive_Name",
                schema: "core",
                table: "Users",
                columns: new[] { "IsActive", "Name" });

            migrationBuilder.CreateIndex(
                name: "UX_Users_Email",
                schema: "core",
                table: "Users",
                column: "Email",
                unique: true,
                filter: "[Email] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Users_ImageId",
                schema: "core",
                table: "Users",
                column: "ImageId",
                unique: true,
                filter: "[ImageId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Users_SignatureId",
                schema: "core",
                table: "Users",
                column: "SignatureId",
                unique: true,
                filter: "[SignatureId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Users_Subject",
                schema: "core",
                table: "Users",
                column: "Subject",
                unique: true,
                filter: "[Subject] IS NOT NULL")
                .Annotation("SqlServer:Include", new[] { "Kind", "IsActive", "State" });

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

            migrationBuilder.AddForeignKey(
                name: "FK_Blobs_CreatedById",
                schema: "core",
                table: "Blobs",
                column: "CreatedById",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.CreateTableType(
                name: "PermissionsList",
                physicalName: "PermissionsList_c3b8a544",
                schema: "core",
                scope: "tellma.fixture",
                definitionHash: "c3b8a5448c3ebe9abda39f7863ab8f1c51bf4febcf750494fe59e24a322ff5fa",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "RoleId", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "Resource", StoreType = "varchar(128)" },
                    new TableTypeColumnDefinition { Name = "Action", StoreType = "varchar(32)" },
                    new TableTypeColumnDefinition { Name = "Filter", StoreType = "nvarchar(2048)", IsNullable = true, MaxLength = 2048 },
                    new TableTypeColumnDefinition { Name = "FilterQueryexVersion", StoreType = "int", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "Notes", StoreType = "nvarchar(1024)", IsNullable = true, MaxLength = 1024 },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CreateTableType(
                name: "RoleMembershipsList",
                physicalName: "RoleMembershipsList_fd20e92d",
                schema: "core",
                scope: "tellma.fixture",
                definitionHash: "fd20e92d0e313d9d665c56bf58a525b09d4ad285c6a3dcbe0caf187e11768638",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "UserId", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "RoleId", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "StartsAt", StoreType = "datetimeoffset(3)", IsNullable = true, Precision = 3 },
                    new TableTypeColumnDefinition { Name = "EndsAt", StoreType = "datetimeoffset(3)", IsNullable = true, Precision = 3 },
                    new TableTypeColumnDefinition { Name = "Notes", StoreType = "nvarchar(1024)", IsNullable = true, MaxLength = 1024 },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CreateTableType(
                name: "RolesList",
                physicalName: "RolesList_77bed563",
                schema: "core",
                scope: "tellma.fixture",
                definitionHash: "77bed56318fe7519a3e1d8fc5fc6cae3bd36fb57a18186f1d045d4761f893158",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "Name", StoreType = "nvarchar(255)", MaxLength = 255 },
                    new TableTypeColumnDefinition { Name = "Name2", StoreType = "nvarchar(255)", IsNullable = true, MaxLength = 255 },
                    new TableTypeColumnDefinition { Name = "Name3", StoreType = "nvarchar(255)", IsNullable = true, MaxLength = 255 },
                    new TableTypeColumnDefinition { Name = "Code", StoreType = "nvarchar(50)", IsNullable = true, MaxLength = 50 },
                    new TableTypeColumnDefinition { Name = "IsPublic", StoreType = "bit" },
                    new TableTypeColumnDefinition { Name = "IsActive", StoreType = "bit" },
                    new TableTypeColumnDefinition { Name = "CreatedAt", StoreType = "datetimeoffset(7)", Precision = 7 },
                    new TableTypeColumnDefinition { Name = "CreatedById", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "ModifiedAt", StoreType = "datetimeoffset(7)", Precision = 7 },
                    new TableTypeColumnDefinition { Name = "ModifiedById", StoreType = "int" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CreateTableType(
                name: "UsersList",
                physicalName: "UsersList_860a025a",
                schema: "core",
                scope: "tellma.fixture",
                definitionHash: "860a025ad78c27c2ed4301feaa147b354e0063c7147d5460efb6b5478ed0b8d5",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "Kind", StoreType = "varchar(10)", MaxLength = 10 },
                    new TableTypeColumnDefinition { Name = "Subject", StoreType = "varchar(255)", IsNullable = true, Collation = "Latin1_General_100_BIN2" },
                    new TableTypeColumnDefinition { Name = "Email", StoreType = "nvarchar(255)", IsNullable = true, MaxLength = 255 },
                    new TableTypeColumnDefinition { Name = "InvitedAt", StoreType = "datetimeoffset(3)", IsNullable = true, Precision = 3 },
                    new TableTypeColumnDefinition { Name = "InviteStatus", StoreType = "varchar(9)", IsNullable = true, MaxLength = 9 },
                    new TableTypeColumnDefinition { Name = "LastInviteError", StoreType = "nvarchar(1024)", IsNullable = true, MaxLength = 1024 },
                    new TableTypeColumnDefinition { Name = "JoinedAt", StoreType = "datetimeoffset(3)", IsNullable = true, Precision = 3 },
                    new TableTypeColumnDefinition { Name = "Name", StoreType = "nvarchar(255)", MaxLength = 255 },
                    new TableTypeColumnDefinition { Name = "Name2", StoreType = "nvarchar(255)", IsNullable = true, MaxLength = 255 },
                    new TableTypeColumnDefinition { Name = "Name3", StoreType = "nvarchar(255)", IsNullable = true, MaxLength = 255 },
                    new TableTypeColumnDefinition { Name = "ImageId", StoreType = "int", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "SignatureId", StoreType = "int", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "PreferredLanguage", StoreType = "varchar(35)", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "PreferredCalendar", StoreType = "varchar(16)", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "PreferredTimeZone", StoreType = "varchar(64)", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "PreferredRegion", StoreType = "varchar(2)", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "PreferredDigits", StoreType = "varchar(8)", IsNullable = true, MaxLength = 8 },
                    new TableTypeColumnDefinition { Name = "Gender", StoreType = "varchar(8)", IsNullable = true, MaxLength = 8 },
                    new TableTypeColumnDefinition { Name = "ContactEmail", StoreType = "nvarchar(255)", IsNullable = true, MaxLength = 255 },
                    new TableTypeColumnDefinition { Name = "ContactMobile", StoreType = "varchar(32)", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "IsActive", StoreType = "bit" },
                    new TableTypeColumnDefinition { Name = "CreatedAt", StoreType = "datetimeoffset(7)", Precision = 7 },
                    new TableTypeColumnDefinition { Name = "CreatedById", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "ModifiedAt", StoreType = "datetimeoffset(7)", Precision = 7 },
                    new TableTypeColumnDefinition { Name = "ModifiedById", StoreType = "int" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "tellma_app" });

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
                name: "UserPreferenceList",
                physicalName: "UserPreferenceList_8b1ed724",
                schema: "dbo",
                scope: "tellma.fixture",
                definitionHash: "8b1ed7248545f0a9000f554af4c438d31317249cc56cd8978434aef1ae02a913",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Key", StoreType = "varchar(128)" },
                    new TableTypeColumnDefinition { Name = "Value", StoreType = "nvarchar(max)" },
                },
                primaryKey: new[] { "Key" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CreateTableType(
                name: "VersionTagList",
                physicalName: "VersionTagList_e552f6b8",
                schema: "dbo",
                scope: "tellma.fixture",
                definitionHash: "e552f6b802344fc80645c75f7e6b7b6c72af4148dac74e8172c6836a1480ae13",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Name", StoreType = "nvarchar(128)", MaxLength = 128 },
                    new TableTypeColumnDefinition { Name = "Tag", StoreType = "uniqueidentifier", IsNullable = true },
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
                name: "LookupsList",
                physicalName: "LookupsList_20dd89f0",
                schema: "fixture",
                scope: "tellma.fixture",
                definitionHash: "20dd89f04552e1bc57851ace8dd637570ca3b44691ad20315b9c27a3d456f22f",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "Code", StoreType = "varchar(50)" },
                    new TableTypeColumnDefinition { Name = "Name", StoreType = "nvarchar(255)", MaxLength = 255 },
                    new TableTypeColumnDefinition { Name = "IsActive", StoreType = "bit" },
                    new TableTypeColumnDefinition { Name = "CreatedAt", StoreType = "datetimeoffset(7)", Precision = 7 },
                    new TableTypeColumnDefinition { Name = "CreatedById", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "ModifiedAt", StoreType = "datetimeoffset(7)", Precision = 7 },
                    new TableTypeColumnDefinition { Name = "ModifiedById", StoreType = "int" },
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
                physicalName: "WidgetsList_32217e51",
                schema: "fixture",
                scope: "tellma.fixture",
                definitionHash: "32217e51a4b3ca214a93e22e6bc8fae5db03b594b54d3f1ae23ff884d51230cd",
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
                    new TableTypeColumnDefinition { Name = "OptionalKind", StoreType = "varchar(8)", IsNullable = true, MaxLength = 8 },
                    new TableTypeColumnDefinition { Name = "DueOn", StoreType = "date", IsNullable = true },
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

            migrationBuilder.CleanupTableTypes(scope: "tellma.fixture", keepList: new[] { "PermissionsList_c3b8a544", "RoleMembershipsList_fd20e92d", "RolesList_77bed563", "UsersList_860a025a", "BigIdList_7629c0a3", "DateList_566fd984", "GuidList_922aa933", "IdList_9b0dfe3c", "IdStampList_36427f71", "StringList_bb8903a2", "UserPreferenceList_8b1ed724", "VersionTagList_e552f6b8", "LinksList_da254281", "LookupsList_20dd89f0", "NodesList_133dd19b", "ShipmentsList_19cd4949", "WidgetPartNotesList_ff09ea87", "WidgetPartsList_7aefe66b", "WidgetsList_32217e51" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Blobs_CreatedById",
                schema: "core",
                table: "Blobs");

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
                name: "Lookups",
                schema: "fixture");

            migrationBuilder.DropTable(
                name: "Permissions",
                schema: "core")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "PermissionsHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "core")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.DropTable(
                name: "RoleMemberships",
                schema: "core")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "RoleMembershipsHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "core")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.DropTable(
                name: "Shipments",
                schema: "fixture");

            migrationBuilder.DropTable(
                name: "UserPreferences",
                schema: "core");

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
                name: "Roles",
                schema: "core")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "RolesHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "core")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

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
                schema: "core")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "UsersHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "core")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

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
                name: "sq_Lookups",
                schema: "fixture");

            migrationBuilder.DropSequence(
                name: "sq_Nodes",
                schema: "fixture");

            migrationBuilder.DropSequence(
                name: "sq_Permissions",
                schema: "core");

            migrationBuilder.DropSequence(
                name: "sq_RoleMemberships",
                schema: "core");

            migrationBuilder.DropSequence(
                name: "sq_Roles",
                schema: "core");

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
