using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.EntityFrameworkCore.TableTypes.Operations;

#nullable disable

namespace Tellma.Distro.Acme.Migrator.Migrations.Tenant
{
    /// <inheritdoc />
    public partial class InitialTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "dbo");

            migrationBuilder.EnsureSchema(
                name: "core");

            migrationBuilder.CreateSequence<int>(
                name: "sq_Blobs",
                schema: "core",
                startValue: 1000L);

            migrationBuilder.CreateSequence<int>(
                name: "sq_Jobs",
                schema: "core",
                startValue: 1000L);

            migrationBuilder.CreateSequence<int>(
                name: "sq_Notifications",
                schema: "core",
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

            migrationBuilder.CreateSequence<int>(
                name: "sq_Schedules",
                schema: "core",
                startValue: 1000L);

            migrationBuilder.CreateSequence<int>(
                name: "sq_Users",
                schema: "core",
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
                name: "JobWorkerState",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    LastTickAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    LastTickOwner = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobWorkerState", x => x.Id);
                    table.CheckConstraint("CK_JobWorkerState_Id", "[Id] > 0");
                    table.CheckConstraint("CK_JobWorkerState_SingleRow", "[Id] = 1");
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
                name: "NotificationPreferences",
                schema: "core",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Channel = table.Column<string>(type: "varchar(16)", unicode: false, maxLength: 16, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationPreferences", x => new { x.UserId, x.Type, x.Channel });
                    table.ForeignKey(
                        name: "FK_NotificationPreferences_UserId",
                        column: x => x.UserId,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Severity = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false),
                    ArgumentsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    TargetResource = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    TargetId = table.Column<long>(type: "bigint", nullable: true),
                    ActorUserId = table.Column<int>(type: "int", nullable: true),
                    DedupKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.CheckConstraint("CK_Notifications_Id", "[Id] > 0");
                    table.ForeignKey(
                        name: "FK_Notifications_ActorUserId",
                        column: x => x.ActorUserId,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Notifications_UserId",
                        column: x => x.UserId,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                })
                .Annotation("Tellma:LockEscalation", "DISABLE");

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
                name: "Schedules",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Name2 = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Name3 = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    HandlerKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ArgumentsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CronExpression = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    TimeZoneId = table.Column<string>(type: "varchar(64)", nullable: true),
                    MissedPolicy = table.Column<string>(type: "varchar(9)", unicode: false, maxLength: 9, nullable: false, defaultValue: "Coalesce")
                        .Annotation("Relational:DefaultConstraintName", "DF_Schedules_MissedPolicy"),
                    OverlapPolicy = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: false, defaultValue: "Skip")
                        .Annotation("Relational:DefaultConstraintName", "DF_Schedules_OverlapPolicy"),
                    CatchUpWindowMinutes = table.Column<int>(type: "int", nullable: false, defaultValue: 1440)
                        .Annotation("Relational:DefaultConstraintName", "DF_Schedules_CatchUpWindowMinutes"),
                    RunAsUserId = table.Column<int>(type: "int", nullable: false),
                    PausedReason = table.Column<string>(type: "varchar(14)", unicode: false, maxLength: 14, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                        .Annotation("Relational:DefaultConstraintName", "DF_Schedules_IsActive"),
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
                    table.PrimaryKey("PK_Schedules", x => x.Id);
                    table.CheckConstraint("CK_Schedules_CatchUp", "[CatchUpWindowMinutes] > 0");
                    table.CheckConstraint("CK_Schedules_Id", "[Id] > 0");
                    table.ForeignKey(
                        name: "FK_Schedules_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Schedules_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Schedules_RunAsUserId",
                        column: x => x.RunAsUserId,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "SchedulesHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "core")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

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
                name: "Jobs",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    HandlerKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    LabelKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LabelArgumentsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Status = table.Column<string>(type: "varchar(19)", unicode: false, maxLength: 19, nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
                        .Annotation("Relational:DefaultConstraintName", "DF_Jobs_Attempts"),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    LeaseOwner = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    CancelRequestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    ArgumentsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProgressPercent = table.Column<byte>(type: "tinyint", nullable: true),
                    ProgressKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ProgressArgumentsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ErrorCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ErrorArgumentsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ErrorTraceId = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: true),
                    PendingConfirmationsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TraceParent = table.Column<string>(type: "nvarchar(55)", maxLength: 55, nullable: true),
                    RunAsUserId = table.Column<int>(type: "int", nullable: false),
                    RequestedById = table.Column<int>(type: "int", nullable: true),
                    ScheduleId = table.Column<int>(type: "int", nullable: true),
                    ScheduledFor = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: false),
                    ConfirmedKeys = table.Column<string>(type: "varchar(2000)", unicode: false, maxLength: 2000, nullable: true),
                    LeaseToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StateJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.Id);
                    table.CheckConstraint("CK_Jobs_Id", "[Id] > 0");
                    table.CheckConstraint("CK_Jobs_Lease", "([Status] = 'Running' AND [LeaseToken] IS NOT NULL AND [LeaseExpiresAt] IS NOT NULL) OR ([Status] <> 'Running' AND [LeaseToken] IS NULL AND [LeaseExpiresAt] IS NULL)");
                    table.CheckConstraint("CK_Jobs_PendingConfirmations", "([Status] = 'PendingConfirmation' AND [PendingConfirmationsJson] IS NOT NULL) OR ([Status] <> 'PendingConfirmation' AND [PendingConfirmationsJson] IS NULL)");
                    table.CheckConstraint("CK_Jobs_Progress", "[ProgressPercent] <= 100");
                    table.ForeignKey(
                        name: "FK_Jobs_RequestedById",
                        column: x => x.RequestedById,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Jobs_RunAsUserId",
                        column: x => x.RunAsUserId,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Jobs_ScheduleId",
                        column: x => x.ScheduleId,
                        principalSchema: "core",
                        principalTable: "Schedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("Tellma:LockEscalation", "DISABLE");

            migrationBuilder.CreateTable(
                name: "ScheduleStates",
                schema: "core",
                columns: table => new
                {
                    ScheduleId = table.Column<int>(type: "int", nullable: false),
                    NextDueAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    LastFiredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    LastScheduledFor = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    LastSkippedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true),
                    LastJobId = table.Column<int>(type: "int", nullable: true),
                    LeaseToken = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset(3)", precision: 3, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleStates", x => x.ScheduleId);
                    table.ForeignKey(
                        name: "FK_ScheduleStates_LastJobId",
                        column: x => x.LastJobId,
                        principalSchema: "core",
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ScheduleStates_ScheduleId",
                        column: x => x.ScheduleId,
                        principalSchema: "core",
                        principalTable: "Schedules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "core",
                table: "JobWorkerState",
                columns: new[] { "Id", "LastTickAt", "LastTickOwner" },
                values: new object[] { 1, null, null });

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
                name: "IX_Jobs_Available",
                schema: "core",
                table: "Jobs",
                columns: new[] { "HandlerKey", "DueAt", "Id" },
                filter: "[Status] IN ('Pending', 'Running')")
                .Annotation("SqlServer:Include", new[] { "LeaseExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_Held",
                schema: "core",
                table: "Jobs",
                column: "RequestedById",
                filter: "[Status] = 'Held'");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_Lease",
                schema: "core",
                table: "Jobs",
                column: "LeaseToken",
                filter: "[LeaseToken] IS NOT NULL")
                .Annotation("SqlServer:Include", new[] { "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_PendingConfirmation",
                schema: "core",
                table: "Jobs",
                column: "DueAt",
                filter: "[Status] = 'PendingConfirmation'");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_RequestedActive",
                schema: "core",
                table: "Jobs",
                columns: new[] { "RequestedById", "CreatedAt", "Id" },
                descending: new[] { false, true, true },
                filter: "[Status] IN ('Pending', 'Running', 'Held', 'PendingConfirmation') AND [RequestedById] IS NOT NULL")
                .Annotation("SqlServer:Include", new[] { "ScheduledFor" });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_RequestedBy",
                schema: "core",
                table: "Jobs",
                columns: new[] { "RequestedById", "CreatedAt" },
                descending: new[] { false, true },
                filter: "[RequestedById] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_Retention",
                schema: "core",
                table: "Jobs",
                columns: new[] { "Status", "CompletedAt" },
                filter: "[Status] IN ('Succeeded', 'Failed', 'Cancelled')");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_ScheduleActive",
                schema: "core",
                table: "Jobs",
                column: "ScheduleId",
                filter: "[Status] IN ('Pending', 'Running', 'Held', 'PendingConfirmation') AND [ScheduleId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Dedup",
                schema: "core",
                table: "Notifications",
                columns: new[] { "DedupKey", "UserId" },
                filter: "[DedupKey] IS NOT NULL")
                .Annotation("SqlServer:Include", new[] { "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Retention",
                schema: "core",
                table: "Notifications",
                columns: new[] { "ReadAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_Unread",
                schema: "core",
                table: "Notifications",
                column: "UserId",
                filter: "[ReadAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_User",
                schema: "core",
                table: "Notifications",
                columns: new[] { "UserId", "CreatedAt", "Id" },
                descending: new[] { false, true, true })
                .Annotation("SqlServer:Include", new[] { "ReadAt", "Type", "Severity" });

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
                name: "IX_Schedules_HandlerKey",
                schema: "core",
                table: "Schedules",
                column: "HandlerKey");

            migrationBuilder.CreateIndex(
                name: "UX_Schedules_Code",
                schema: "core",
                table: "Schedules",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleStates_Due",
                schema: "core",
                table: "ScheduleStates",
                column: "NextDueAt",
                filter: "[NextDueAt] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleStates_Lease",
                schema: "core",
                table: "ScheduleStates",
                column: "LeaseToken",
                filter: "[LeaseToken] IS NOT NULL");

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

            migrationBuilder.AddForeignKey(
                name: "FK_Blobs_CreatedById",
                schema: "core",
                table: "Blobs",
                column: "CreatedById",
                principalSchema: "core",
                principalTable: "Users",
                principalColumn: "Id");

            migrationBuilder.CreateTableType(
                name: "JobsList",
                physicalName: "JobsList_a90a5696",
                schema: "core",
                scope: "tellma.tenant",
                definitionHash: "a90a56966c12112f395c7722e739ba73fb11e69bf366db689c10ec5675dc3b19",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "HandlerKey", StoreType = "nvarchar(64)", MaxLength = 64 },
                    new TableTypeColumnDefinition { Name = "LabelKey", StoreType = "nvarchar(64)", IsNullable = true, MaxLength = 64 },
                    new TableTypeColumnDefinition { Name = "LabelArgumentsJson", StoreType = "nvarchar(4000)", IsNullable = true, MaxLength = 4000 },
                    new TableTypeColumnDefinition { Name = "Status", StoreType = "varchar(19)", MaxLength = 19 },
                    new TableTypeColumnDefinition { Name = "DueAt", StoreType = "datetimeoffset(3)", Precision = 3 },
                    new TableTypeColumnDefinition { Name = "Attempts", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "LeaseExpiresAt", StoreType = "datetimeoffset(3)", IsNullable = true, Precision = 3 },
                    new TableTypeColumnDefinition { Name = "LeaseOwner", StoreType = "nvarchar(128)", IsNullable = true, MaxLength = 128 },
                    new TableTypeColumnDefinition { Name = "StartedAt", StoreType = "datetimeoffset(3)", IsNullable = true, Precision = 3 },
                    new TableTypeColumnDefinition { Name = "CompletedAt", StoreType = "datetimeoffset(3)", IsNullable = true, Precision = 3 },
                    new TableTypeColumnDefinition { Name = "CancelRequestedAt", StoreType = "datetimeoffset(3)", IsNullable = true, Precision = 3 },
                    new TableTypeColumnDefinition { Name = "ArgumentsJson", StoreType = "nvarchar(max)", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "ProgressPercent", StoreType = "tinyint", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "ProgressKey", StoreType = "nvarchar(64)", IsNullable = true, MaxLength = 64 },
                    new TableTypeColumnDefinition { Name = "ProgressArgumentsJson", StoreType = "nvarchar(4000)", IsNullable = true, MaxLength = 4000 },
                    new TableTypeColumnDefinition { Name = "ErrorCode", StoreType = "nvarchar(64)", IsNullable = true, MaxLength = 64 },
                    new TableTypeColumnDefinition { Name = "ErrorArgumentsJson", StoreType = "nvarchar(4000)", IsNullable = true, MaxLength = 4000 },
                    new TableTypeColumnDefinition { Name = "ErrorTraceId", StoreType = "varchar(32)", IsNullable = true, MaxLength = 32 },
                    new TableTypeColumnDefinition { Name = "PendingConfirmationsJson", StoreType = "nvarchar(max)", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "TraceParent", StoreType = "nvarchar(55)", IsNullable = true, MaxLength = 55 },
                    new TableTypeColumnDefinition { Name = "RunAsUserId", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "RequestedById", StoreType = "int", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "ScheduleId", StoreType = "int", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "ScheduledFor", StoreType = "datetimeoffset(3)", IsNullable = true, Precision = 3 },
                    new TableTypeColumnDefinition { Name = "CreatedAt", StoreType = "datetimeoffset(3)", Precision = 3 },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CreateTableType(
                name: "NotificationsList",
                physicalName: "NotificationsList_45bbd83e",
                schema: "core",
                scope: "tellma.tenant",
                definitionHash: "45bbd83e1111f0cd04ee2c2dbf07eb231f4e76281f56259e03d55ea4365b4eac",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "UserId", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "Type", StoreType = "nvarchar(64)", MaxLength = 64 },
                    new TableTypeColumnDefinition { Name = "Severity", StoreType = "varchar(8)", MaxLength = 8 },
                    new TableTypeColumnDefinition { Name = "ArgumentsJson", StoreType = "nvarchar(4000)", IsNullable = true, MaxLength = 4000 },
                    new TableTypeColumnDefinition { Name = "TargetResource", StoreType = "varchar(128)", IsNullable = true, MaxLength = 128 },
                    new TableTypeColumnDefinition { Name = "TargetId", StoreType = "bigint", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "ActorUserId", StoreType = "int", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "DedupKey", StoreType = "nvarchar(128)", IsNullable = true, MaxLength = 128 },
                    new TableTypeColumnDefinition { Name = "CreatedAt", StoreType = "datetimeoffset(3)", Precision = 3 },
                    new TableTypeColumnDefinition { Name = "ReadAt", StoreType = "datetimeoffset(3)", IsNullable = true, Precision = 3 },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CreateTableType(
                name: "PermissionsList",
                physicalName: "PermissionsList_c3b8a544",
                schema: "core",
                scope: "tellma.tenant",
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
                scope: "tellma.tenant",
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
                scope: "tellma.tenant",
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
                name: "SchedulesList",
                physicalName: "SchedulesList_5acfe7bf",
                schema: "core",
                scope: "tellma.tenant",
                definitionHash: "5acfe7bf822ea1309c777630a40fb11770b76f6663b608946098200811187c11",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "Code", StoreType = "nvarchar(64)", IsNullable = true, MaxLength = 64 },
                    new TableTypeColumnDefinition { Name = "Name", StoreType = "nvarchar(255)", MaxLength = 255 },
                    new TableTypeColumnDefinition { Name = "Name2", StoreType = "nvarchar(255)", IsNullable = true, MaxLength = 255 },
                    new TableTypeColumnDefinition { Name = "Name3", StoreType = "nvarchar(255)", IsNullable = true, MaxLength = 255 },
                    new TableTypeColumnDefinition { Name = "HandlerKey", StoreType = "nvarchar(64)", MaxLength = 64 },
                    new TableTypeColumnDefinition { Name = "ArgumentsJson", StoreType = "nvarchar(max)", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "CronExpression", StoreType = "nvarchar(128)", MaxLength = 128 },
                    new TableTypeColumnDefinition { Name = "TimeZoneId", StoreType = "varchar(64)", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "MissedPolicy", StoreType = "varchar(9)", MaxLength = 9 },
                    new TableTypeColumnDefinition { Name = "OverlapPolicy", StoreType = "varchar(8)", MaxLength = 8 },
                    new TableTypeColumnDefinition { Name = "CatchUpWindowMinutes", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "RunAsUserId", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "PausedReason", StoreType = "varchar(14)", IsNullable = true, MaxLength = 14 },
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
                scope: "tellma.tenant",
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
                scope: "tellma.tenant",
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
                scope: "tellma.tenant",
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
                scope: "tellma.tenant",
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
                scope: "tellma.tenant",
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
                scope: "tellma.tenant",
                definitionHash: "36427f71498d797e4fa760e904a4e497235bf4a13838e4474980e12901da7d4c",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "ModifiedAt", StoreType = "datetimeoffset(7)" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CreateTableType(
                name: "JobLeaseList",
                physicalName: "JobLeaseList_17bf60e1",
                schema: "dbo",
                scope: "tellma.tenant",
                definitionHash: "17bf60e129631be2c38adeac1c63fd53690297ad84e850a2884ab7858e2ccab7",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "uniqueidentifier" },
                    new TableTypeColumnDefinition { Name = "LeaseSeconds", StoreType = "int" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CreateTableType(
                name: "JobOutcomeList",
                physicalName: "JobOutcomeList_6165fff7",
                schema: "dbo",
                scope: "tellma.tenant",
                definitionHash: "6165fff75c5c86dcee7220244f42cab91e268be2d8d619cd3717c8a7457e56f5",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "Status", StoreType = "varchar(19)", MaxLength = 19 },
                    new TableTypeColumnDefinition { Name = "Released", StoreType = "bit" },
                    new TableTypeColumnDefinition { Name = "RetryAfterSeconds", StoreType = "int", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "ErrorCode", StoreType = "nvarchar(64)", IsNullable = true, MaxLength = 64 },
                    new TableTypeColumnDefinition { Name = "ErrorArgumentsJson", StoreType = "nvarchar(4000)", IsNullable = true, MaxLength = 4000 },
                    new TableTypeColumnDefinition { Name = "ErrorTraceId", StoreType = "varchar(32)", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "StateJson", StoreType = "nvarchar(max)", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "PendingConfirmationsJson", StoreType = "nvarchar(max)", IsNullable = true },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CreateTableType(
                name: "JobProgressList",
                physicalName: "JobProgressList_33eeb232",
                schema: "dbo",
                scope: "tellma.tenant",
                definitionHash: "33eeb2322555eb68974c47da00b6e501a2aaf5441df793a58494c873a782c1ef",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "ProgressPercent", StoreType = "tinyint", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "ProgressKey", StoreType = "nvarchar(64)", IsNullable = true, MaxLength = 64 },
                    new TableTypeColumnDefinition { Name = "ProgressArgumentsJson", StoreType = "nvarchar(4000)", IsNullable = true, MaxLength = 4000 },
                    new TableTypeColumnDefinition { Name = "StateJson", StoreType = "nvarchar(max)", IsNullable = true },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CreateTableType(
                name: "JobRequestList",
                physicalName: "JobRequestList_f3ef6d1d",
                schema: "dbo",
                scope: "tellma.tenant",
                definitionHash: "f3ef6d1da68ff3f15b61465e85a46302ed85053ae10f6bda728e92075f5768bc",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Ordinal", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "HandlerKey", StoreType = "nvarchar(64)", MaxLength = 64 },
                    new TableTypeColumnDefinition { Name = "LabelKey", StoreType = "nvarchar(64)", IsNullable = true, MaxLength = 64 },
                    new TableTypeColumnDefinition { Name = "LabelArgumentsJson", StoreType = "nvarchar(4000)", IsNullable = true, MaxLength = 4000 },
                    new TableTypeColumnDefinition { Name = "DueAt", StoreType = "datetimeoffset(3)", IsNullable = true, Precision = 3 },
                    new TableTypeColumnDefinition { Name = "ArgumentsJson", StoreType = "nvarchar(max)", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "TraceParent", StoreType = "nvarchar(55)", IsNullable = true, MaxLength = 55 },
                    new TableTypeColumnDefinition { Name = "ConfirmedKeys", StoreType = "varchar(2000)", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "RunAsUserId", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "RequestedById", StoreType = "int", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "OwnerId", StoreType = "bigint", IsNullable = true },
                },
                primaryKey: new[] { "Ordinal" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CreateTableType(
                name: "NotificationPreferenceList",
                physicalName: "NotificationPreferenceList_61bf7917",
                schema: "dbo",
                scope: "tellma.tenant",
                definitionHash: "61bf79173291955493c1496b972fdea59abb45b742a0177d947351f1430660aa",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Type", StoreType = "nvarchar(64)", MaxLength = 64 },
                    new TableTypeColumnDefinition { Name = "Channel", StoreType = "varchar(16)" },
                    new TableTypeColumnDefinition { Name = "Enabled", StoreType = "bit" },
                },
                primaryKey: new[] { "Type", "Channel" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CreateTableType(
                name: "NotificationRowList",
                physicalName: "NotificationRowList_2c2b24d8",
                schema: "dbo",
                scope: "tellma.tenant",
                definitionHash: "2c2b24d8c87ca20cd830850e4eba8d0b1bbc7be30c3f4a9c5400fae6205b8005",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Ordinal", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "UserId", StoreType = "int" },
                },
                primaryKey: new[] { "Ordinal" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CreateTableType(
                name: "NotificationSliceList",
                physicalName: "NotificationSliceList_29aa6852",
                schema: "dbo",
                scope: "tellma.tenant",
                definitionHash: "29aa6852aae5ba536b3aa31a3a88fd10a00e27cb6e30776904f7980ec7e91ae5",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "SliceOrdinal", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "FirstOrdinal", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "LastOrdinal", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "ArgumentsJson", StoreType = "nvarchar(max)" },
                },
                primaryKey: new[] { "SliceOrdinal" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CreateTableType(
                name: "ScheduleNextList",
                physicalName: "ScheduleNextList_e422990c",
                schema: "dbo",
                scope: "tellma.tenant",
                definitionHash: "e422990c3bcda60df131b6e0b9b4d36adb90bb6f61e1f6dd0a2c48c599c25c4d",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "ScheduleId", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "NextDueAt", StoreType = "datetimeoffset(3)", IsNullable = true, Precision = 3 },
                    new TableTypeColumnDefinition { Name = "LastScheduledFor", StoreType = "datetimeoffset(3)", IsNullable = true, Precision = 3 },
                },
                primaryKey: new[] { "ScheduleId" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CreateTableType(
                name: "StringList",
                physicalName: "StringList_bb8903a2",
                schema: "dbo",
                scope: "tellma.tenant",
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
                scope: "tellma.tenant",
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
                scope: "tellma.tenant",
                definitionHash: "e552f6b802344fc80645c75f7e6b7b6c72af4148dac74e8172c6836a1480ae13",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Name", StoreType = "nvarchar(128)", MaxLength = 128 },
                    new TableTypeColumnDefinition { Name = "Tag", StoreType = "uniqueidentifier", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "Bumped", StoreType = "bit" },
                },
                primaryKey: new[] { "Name" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CleanupTableTypes(scope: "tellma.tenant", keepList: new[] { "JobsList_a90a5696", "NotificationsList_45bbd83e", "PermissionsList_c3b8a544", "RoleMembershipsList_fd20e92d", "RolesList_77bed563", "SchedulesList_5acfe7bf", "UsersList_860a025a", "BigIdList_7629c0a3", "DateList_566fd984", "GuidList_922aa933", "IdList_9b0dfe3c", "IdStampList_36427f71", "JobLeaseList_17bf60e1", "JobOutcomeList_6165fff7", "JobProgressList_33eeb232", "JobRequestList_f3ef6d1d", "NotificationPreferenceList_61bf7917", "NotificationRowList_2c2b24d8", "NotificationSliceList_29aa6852", "ScheduleNextList_e422990c", "StringList_bb8903a2", "UserPreferenceList_8b1ed724", "VersionTagList_e552f6b8" });
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
                name: "JobWorkerState",
                schema: "core");

            migrationBuilder.DropTable(
                name: "NotificationPreferences",
                schema: "core");

            migrationBuilder.DropTable(
                name: "Notifications",
                schema: "core")
                .Annotation("Tellma:LockEscalation", "DISABLE");

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
                name: "ScheduleStates",
                schema: "core");

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
                name: "Roles",
                schema: "core")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "RolesHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "core")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.DropTable(
                name: "Jobs",
                schema: "core")
                .Annotation("Tellma:LockEscalation", "DISABLE");

            migrationBuilder.DropTable(
                name: "Schedules",
                schema: "core")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "SchedulesHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "core")
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
                name: "sq_Notifications",
                schema: "core");

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
                name: "sq_Schedules",
                schema: "core");

            migrationBuilder.DropSequence(
                name: "sq_Users",
                schema: "core");

            migrationBuilder.CleanupTableTypes(scope: "tellma.tenant", keepList: new string[0]);
        }
    }
}
