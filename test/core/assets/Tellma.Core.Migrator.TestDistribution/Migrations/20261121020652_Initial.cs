using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.EntityFrameworkCore.TableTypes.Operations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Tellma.Core.Migrator.TestDistribution.Migrations
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
                name: "probe");

            migrationBuilder.CreateSequence<int>(
                name: "sq_Blobs",
                schema: "core",
                startValue: 1000L);

            migrationBuilder.CreateSequence<int>(
                name: "sq_Gadgets",
                schema: "probe",
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
                name: "sq_SettingEntries",
                schema: "core",
                startValue: 1000L);

            migrationBuilder.CreateSequence<int>(
                name: "sq_Settings",
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
                name: "CalendarMonths",
                schema: "core",
                columns: table => new
                {
                    Calendar = table.Column<string>(type: "varchar(2)", unicode: false, maxLength: 2, nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarMonths", x => new { x.Calendar, x.Year, x.Month });
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
                name: "Gadgets",
                schema: "probe",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    CreatedById = table.Column<int>(type: "int", nullable: false),
                    ModifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    ModifiedById = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Gadgets", x => x.Id);
                    table.CheckConstraint("CK_Gadgets_Id", "[Id] > 0");
                    table.ForeignKey(
                        name: "FK_Gadgets_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Gadgets_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                });

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
                name: "SettingEntries",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: false),
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
                    table.PrimaryKey("PK_SettingEntries", x => x.Id);
                    table.CheckConstraint("CK_SettingEntries_Id", "[Id] > 0");
                    table.ForeignKey(
                        name: "FK_SettingEntries_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SettingEntries_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "SettingEntriesHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "core")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.CreateTable(
                name: "Settings",
                schema: "core",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Name2 = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Name3 = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    PrimaryLanguage = table.Column<string>(type: "varchar(35)", nullable: false),
                    SecondaryLanguage = table.Column<string>(type: "varchar(35)", nullable: true),
                    TertiaryLanguage = table.Column<string>(type: "varchar(35)", nullable: true),
                    PrimaryCalendar = table.Column<string>(type: "varchar(16)", nullable: false, defaultValue: "gc")
                        .Annotation("Relational:DefaultConstraintName", "DF_Settings_PrimaryCalendar"),
                    SecondaryCalendar = table.Column<string>(type: "varchar(16)", nullable: true),
                    TimeZone = table.Column<string>(type: "varchar(64)", nullable: false),
                    Region = table.Column<string>(type: "varchar(2)", nullable: true),
                    Digits = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: true),
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
                    table.PrimaryKey("PK_Settings", x => x.Id);
                    table.CheckConstraint("CK_Settings_Calendars", "[SecondaryCalendar] IS NULL OR [SecondaryCalendar] <> [PrimaryCalendar]");
                    table.CheckConstraint("CK_Settings_Id", "[Id] > 0");
                    table.CheckConstraint("CK_Settings_Languages", "([SecondaryLanguage] IS NULL OR [SecondaryLanguage] <> [PrimaryLanguage]) AND ([TertiaryLanguage] IS NULL OR ([SecondaryLanguage] IS NOT NULL AND [TertiaryLanguage] <> [PrimaryLanguage] AND [TertiaryLanguage] <> [SecondaryLanguage]))");
                    table.CheckConstraint("CK_Settings_SingleRow", "[Id] = 1");
                    table.ForeignKey(
                        name: "FK_Settings_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Settings_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "core",
                        principalTable: "Users",
                        principalColumn: "Id");
                })
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "SettingsHistory")
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
                table: "CalendarMonths",
                columns: new[] { "Calendar", "Month", "Year", "EndDate", "StartDate" },
                values: new object[,]
                {
                    { "uq", 1, 1318, new DateOnly(1900, 5, 29), new DateOnly(1900, 4, 30) },
                    { "uq", 2, 1318, new DateOnly(1900, 6, 28), new DateOnly(1900, 5, 29) },
                    { "uq", 3, 1318, new DateOnly(1900, 7, 27), new DateOnly(1900, 6, 28) },
                    { "uq", 4, 1318, new DateOnly(1900, 8, 26), new DateOnly(1900, 7, 27) },
                    { "uq", 5, 1318, new DateOnly(1900, 9, 24), new DateOnly(1900, 8, 26) },
                    { "uq", 6, 1318, new DateOnly(1900, 10, 24), new DateOnly(1900, 9, 24) },
                    { "uq", 7, 1318, new DateOnly(1900, 11, 23), new DateOnly(1900, 10, 24) },
                    { "uq", 8, 1318, new DateOnly(1900, 12, 23), new DateOnly(1900, 11, 23) },
                    { "uq", 9, 1318, new DateOnly(1901, 1, 21), new DateOnly(1900, 12, 23) },
                    { "uq", 10, 1318, new DateOnly(1901, 2, 20), new DateOnly(1901, 1, 21) },
                    { "uq", 11, 1318, new DateOnly(1901, 3, 21), new DateOnly(1901, 2, 20) },
                    { "uq", 12, 1318, new DateOnly(1901, 4, 19), new DateOnly(1901, 3, 21) },
                    { "uq", 1, 1319, new DateOnly(1901, 5, 19), new DateOnly(1901, 4, 19) },
                    { "uq", 2, 1319, new DateOnly(1901, 6, 17), new DateOnly(1901, 5, 19) },
                    { "uq", 3, 1319, new DateOnly(1901, 7, 16), new DateOnly(1901, 6, 17) },
                    { "uq", 4, 1319, new DateOnly(1901, 8, 15), new DateOnly(1901, 7, 16) },
                    { "uq", 5, 1319, new DateOnly(1901, 9, 13), new DateOnly(1901, 8, 15) },
                    { "uq", 6, 1319, new DateOnly(1901, 10, 13), new DateOnly(1901, 9, 13) },
                    { "uq", 7, 1319, new DateOnly(1901, 11, 12), new DateOnly(1901, 10, 13) },
                    { "uq", 8, 1319, new DateOnly(1901, 12, 12), new DateOnly(1901, 11, 12) },
                    { "uq", 9, 1319, new DateOnly(1902, 1, 10), new DateOnly(1901, 12, 12) },
                    { "uq", 10, 1319, new DateOnly(1902, 2, 9), new DateOnly(1902, 1, 10) },
                    { "uq", 11, 1319, new DateOnly(1902, 3, 11), new DateOnly(1902, 2, 9) },
                    { "uq", 12, 1319, new DateOnly(1902, 4, 9), new DateOnly(1902, 3, 11) },
                    { "uq", 1, 1320, new DateOnly(1902, 5, 8), new DateOnly(1902, 4, 9) },
                    { "uq", 2, 1320, new DateOnly(1902, 6, 7), new DateOnly(1902, 5, 8) },
                    { "uq", 3, 1320, new DateOnly(1902, 7, 6), new DateOnly(1902, 6, 7) },
                    { "uq", 4, 1320, new DateOnly(1902, 8, 4), new DateOnly(1902, 7, 6) },
                    { "uq", 5, 1320, new DateOnly(1902, 9, 3), new DateOnly(1902, 8, 4) },
                    { "uq", 6, 1320, new DateOnly(1902, 10, 2), new DateOnly(1902, 9, 3) },
                    { "uq", 7, 1320, new DateOnly(1902, 11, 1), new DateOnly(1902, 10, 2) },
                    { "uq", 8, 1320, new DateOnly(1902, 12, 1), new DateOnly(1902, 11, 1) },
                    { "uq", 9, 1320, new DateOnly(1902, 12, 30), new DateOnly(1902, 12, 1) },
                    { "uq", 10, 1320, new DateOnly(1903, 1, 29), new DateOnly(1902, 12, 30) },
                    { "uq", 11, 1320, new DateOnly(1903, 2, 28), new DateOnly(1903, 1, 29) },
                    { "uq", 12, 1320, new DateOnly(1903, 3, 30), new DateOnly(1903, 2, 28) },
                    { "uq", 1, 1321, new DateOnly(1903, 4, 28), new DateOnly(1903, 3, 30) },
                    { "uq", 2, 1321, new DateOnly(1903, 5, 27), new DateOnly(1903, 4, 28) },
                    { "uq", 3, 1321, new DateOnly(1903, 6, 26), new DateOnly(1903, 5, 27) },
                    { "uq", 4, 1321, new DateOnly(1903, 7, 25), new DateOnly(1903, 6, 26) },
                    { "uq", 5, 1321, new DateOnly(1903, 8, 23), new DateOnly(1903, 7, 25) },
                    { "uq", 6, 1321, new DateOnly(1903, 9, 22), new DateOnly(1903, 8, 23) },
                    { "uq", 7, 1321, new DateOnly(1903, 10, 21), new DateOnly(1903, 9, 22) },
                    { "uq", 8, 1321, new DateOnly(1903, 11, 20), new DateOnly(1903, 10, 21) },
                    { "uq", 9, 1321, new DateOnly(1903, 12, 19), new DateOnly(1903, 11, 20) },
                    { "uq", 10, 1321, new DateOnly(1904, 1, 18), new DateOnly(1903, 12, 19) },
                    { "uq", 11, 1321, new DateOnly(1904, 2, 17), new DateOnly(1904, 1, 18) },
                    { "uq", 12, 1321, new DateOnly(1904, 3, 18), new DateOnly(1904, 2, 17) },
                    { "uq", 1, 1322, new DateOnly(1904, 4, 16), new DateOnly(1904, 3, 18) },
                    { "uq", 2, 1322, new DateOnly(1904, 5, 16), new DateOnly(1904, 4, 16) },
                    { "uq", 3, 1322, new DateOnly(1904, 6, 14), new DateOnly(1904, 5, 16) },
                    { "uq", 4, 1322, new DateOnly(1904, 7, 14), new DateOnly(1904, 6, 14) },
                    { "uq", 5, 1322, new DateOnly(1904, 8, 12), new DateOnly(1904, 7, 14) },
                    { "uq", 6, 1322, new DateOnly(1904, 9, 10), new DateOnly(1904, 8, 12) },
                    { "uq", 7, 1322, new DateOnly(1904, 10, 10), new DateOnly(1904, 9, 10) },
                    { "uq", 8, 1322, new DateOnly(1904, 11, 8), new DateOnly(1904, 10, 10) },
                    { "uq", 9, 1322, new DateOnly(1904, 12, 8), new DateOnly(1904, 11, 8) },
                    { "uq", 10, 1322, new DateOnly(1905, 1, 6), new DateOnly(1904, 12, 8) },
                    { "uq", 11, 1322, new DateOnly(1905, 2, 5), new DateOnly(1905, 1, 6) },
                    { "uq", 12, 1322, new DateOnly(1905, 3, 7), new DateOnly(1905, 2, 5) },
                    { "uq", 1, 1323, new DateOnly(1905, 4, 5), new DateOnly(1905, 3, 7) },
                    { "uq", 2, 1323, new DateOnly(1905, 5, 5), new DateOnly(1905, 4, 5) },
                    { "uq", 3, 1323, new DateOnly(1905, 6, 4), new DateOnly(1905, 5, 5) },
                    { "uq", 4, 1323, new DateOnly(1905, 7, 3), new DateOnly(1905, 6, 4) },
                    { "uq", 5, 1323, new DateOnly(1905, 8, 2), new DateOnly(1905, 7, 3) },
                    { "uq", 6, 1323, new DateOnly(1905, 8, 31), new DateOnly(1905, 8, 2) },
                    { "uq", 7, 1323, new DateOnly(1905, 9, 29), new DateOnly(1905, 8, 31) },
                    { "uq", 8, 1323, new DateOnly(1905, 10, 29), new DateOnly(1905, 9, 29) },
                    { "uq", 9, 1323, new DateOnly(1905, 11, 27), new DateOnly(1905, 10, 29) },
                    { "uq", 10, 1323, new DateOnly(1905, 12, 27), new DateOnly(1905, 11, 27) },
                    { "uq", 11, 1323, new DateOnly(1906, 1, 25), new DateOnly(1905, 12, 27) },
                    { "uq", 12, 1323, new DateOnly(1906, 2, 24), new DateOnly(1906, 1, 25) },
                    { "uq", 1, 1324, new DateOnly(1906, 3, 25), new DateOnly(1906, 2, 24) },
                    { "uq", 2, 1324, new DateOnly(1906, 4, 24), new DateOnly(1906, 3, 25) },
                    { "uq", 3, 1324, new DateOnly(1906, 5, 24), new DateOnly(1906, 4, 24) },
                    { "uq", 4, 1324, new DateOnly(1906, 6, 22), new DateOnly(1906, 5, 24) },
                    { "uq", 5, 1324, new DateOnly(1906, 7, 22), new DateOnly(1906, 6, 22) },
                    { "uq", 6, 1324, new DateOnly(1906, 8, 21), new DateOnly(1906, 7, 22) },
                    { "uq", 7, 1324, new DateOnly(1906, 9, 19), new DateOnly(1906, 8, 21) },
                    { "uq", 8, 1324, new DateOnly(1906, 10, 18), new DateOnly(1906, 9, 19) },
                    { "uq", 9, 1324, new DateOnly(1906, 11, 17), new DateOnly(1906, 10, 18) },
                    { "uq", 10, 1324, new DateOnly(1906, 12, 16), new DateOnly(1906, 11, 17) },
                    { "uq", 11, 1324, new DateOnly(1907, 1, 15), new DateOnly(1906, 12, 16) },
                    { "uq", 12, 1324, new DateOnly(1907, 2, 13), new DateOnly(1907, 1, 15) },
                    { "uq", 1, 1325, new DateOnly(1907, 3, 15), new DateOnly(1907, 2, 13) },
                    { "uq", 2, 1325, new DateOnly(1907, 4, 13), new DateOnly(1907, 3, 15) },
                    { "uq", 3, 1325, new DateOnly(1907, 5, 13), new DateOnly(1907, 4, 13) },
                    { "uq", 4, 1325, new DateOnly(1907, 6, 11), new DateOnly(1907, 5, 13) },
                    { "uq", 5, 1325, new DateOnly(1907, 7, 11), new DateOnly(1907, 6, 11) },
                    { "uq", 6, 1325, new DateOnly(1907, 8, 10), new DateOnly(1907, 7, 11) },
                    { "uq", 7, 1325, new DateOnly(1907, 9, 8), new DateOnly(1907, 8, 10) },
                    { "uq", 8, 1325, new DateOnly(1907, 10, 8), new DateOnly(1907, 9, 8) },
                    { "uq", 9, 1325, new DateOnly(1907, 11, 6), new DateOnly(1907, 10, 8) },
                    { "uq", 10, 1325, new DateOnly(1907, 12, 6), new DateOnly(1907, 11, 6) },
                    { "uq", 11, 1325, new DateOnly(1908, 1, 4), new DateOnly(1907, 12, 6) },
                    { "uq", 12, 1325, new DateOnly(1908, 2, 3), new DateOnly(1908, 1, 4) },
                    { "uq", 1, 1326, new DateOnly(1908, 3, 3), new DateOnly(1908, 2, 3) },
                    { "uq", 2, 1326, new DateOnly(1908, 4, 2), new DateOnly(1908, 3, 3) },
                    { "uq", 3, 1326, new DateOnly(1908, 5, 1), new DateOnly(1908, 4, 2) },
                    { "uq", 4, 1326, new DateOnly(1908, 5, 31), new DateOnly(1908, 5, 1) },
                    { "uq", 5, 1326, new DateOnly(1908, 6, 29), new DateOnly(1908, 5, 31) },
                    { "uq", 6, 1326, new DateOnly(1908, 7, 29), new DateOnly(1908, 6, 29) },
                    { "uq", 7, 1326, new DateOnly(1908, 8, 27), new DateOnly(1908, 7, 29) },
                    { "uq", 8, 1326, new DateOnly(1908, 9, 26), new DateOnly(1908, 8, 27) },
                    { "uq", 9, 1326, new DateOnly(1908, 10, 26), new DateOnly(1908, 9, 26) },
                    { "uq", 10, 1326, new DateOnly(1908, 11, 24), new DateOnly(1908, 10, 26) },
                    { "uq", 11, 1326, new DateOnly(1908, 12, 24), new DateOnly(1908, 11, 24) },
                    { "uq", 12, 1326, new DateOnly(1909, 1, 23), new DateOnly(1908, 12, 24) },
                    { "uq", 1, 1327, new DateOnly(1909, 2, 21), new DateOnly(1909, 1, 23) },
                    { "uq", 2, 1327, new DateOnly(1909, 3, 22), new DateOnly(1909, 2, 21) },
                    { "uq", 3, 1327, new DateOnly(1909, 4, 21), new DateOnly(1909, 3, 22) },
                    { "uq", 4, 1327, new DateOnly(1909, 5, 20), new DateOnly(1909, 4, 21) },
                    { "uq", 5, 1327, new DateOnly(1909, 6, 18), new DateOnly(1909, 5, 20) },
                    { "uq", 6, 1327, new DateOnly(1909, 7, 18), new DateOnly(1909, 6, 18) },
                    { "uq", 7, 1327, new DateOnly(1909, 8, 16), new DateOnly(1909, 7, 18) },
                    { "uq", 8, 1327, new DateOnly(1909, 9, 15), new DateOnly(1909, 8, 16) },
                    { "uq", 9, 1327, new DateOnly(1909, 10, 15), new DateOnly(1909, 9, 15) },
                    { "uq", 10, 1327, new DateOnly(1909, 11, 14), new DateOnly(1909, 10, 15) },
                    { "uq", 11, 1327, new DateOnly(1909, 12, 13), new DateOnly(1909, 11, 14) },
                    { "uq", 12, 1327, new DateOnly(1910, 1, 12), new DateOnly(1909, 12, 13) },
                    { "uq", 1, 1328, new DateOnly(1910, 2, 11), new DateOnly(1910, 1, 12) },
                    { "uq", 2, 1328, new DateOnly(1910, 3, 12), new DateOnly(1910, 2, 11) },
                    { "uq", 3, 1328, new DateOnly(1910, 4, 10), new DateOnly(1910, 3, 12) },
                    { "uq", 4, 1328, new DateOnly(1910, 5, 10), new DateOnly(1910, 4, 10) },
                    { "uq", 5, 1328, new DateOnly(1910, 6, 8), new DateOnly(1910, 5, 10) },
                    { "uq", 6, 1328, new DateOnly(1910, 7, 7), new DateOnly(1910, 6, 8) },
                    { "uq", 7, 1328, new DateOnly(1910, 8, 6), new DateOnly(1910, 7, 7) },
                    { "uq", 8, 1328, new DateOnly(1910, 9, 4), new DateOnly(1910, 8, 6) },
                    { "uq", 9, 1328, new DateOnly(1910, 10, 4), new DateOnly(1910, 9, 4) },
                    { "uq", 10, 1328, new DateOnly(1910, 11, 3), new DateOnly(1910, 10, 4) },
                    { "uq", 11, 1328, new DateOnly(1910, 12, 2), new DateOnly(1910, 11, 3) },
                    { "uq", 12, 1328, new DateOnly(1911, 1, 1), new DateOnly(1910, 12, 2) },
                    { "uq", 1, 1329, new DateOnly(1911, 1, 31), new DateOnly(1911, 1, 1) },
                    { "uq", 2, 1329, new DateOnly(1911, 3, 2), new DateOnly(1911, 1, 31) },
                    { "uq", 3, 1329, new DateOnly(1911, 3, 31), new DateOnly(1911, 3, 2) },
                    { "uq", 4, 1329, new DateOnly(1911, 4, 29), new DateOnly(1911, 3, 31) },
                    { "uq", 5, 1329, new DateOnly(1911, 5, 29), new DateOnly(1911, 4, 29) },
                    { "uq", 6, 1329, new DateOnly(1911, 6, 27), new DateOnly(1911, 5, 29) },
                    { "uq", 7, 1329, new DateOnly(1911, 7, 26), new DateOnly(1911, 6, 27) },
                    { "uq", 8, 1329, new DateOnly(1911, 8, 25), new DateOnly(1911, 7, 26) },
                    { "uq", 9, 1329, new DateOnly(1911, 9, 23), new DateOnly(1911, 8, 25) },
                    { "uq", 10, 1329, new DateOnly(1911, 10, 23), new DateOnly(1911, 9, 23) },
                    { "uq", 11, 1329, new DateOnly(1911, 11, 21), new DateOnly(1911, 10, 23) },
                    { "uq", 12, 1329, new DateOnly(1911, 12, 21), new DateOnly(1911, 11, 21) },
                    { "uq", 1, 1330, new DateOnly(1912, 1, 20), new DateOnly(1911, 12, 21) },
                    { "uq", 2, 1330, new DateOnly(1912, 2, 19), new DateOnly(1912, 1, 20) },
                    { "uq", 3, 1330, new DateOnly(1912, 3, 19), new DateOnly(1912, 2, 19) },
                    { "uq", 4, 1330, new DateOnly(1912, 4, 18), new DateOnly(1912, 3, 19) },
                    { "uq", 5, 1330, new DateOnly(1912, 5, 17), new DateOnly(1912, 4, 18) },
                    { "uq", 6, 1330, new DateOnly(1912, 6, 16), new DateOnly(1912, 5, 17) },
                    { "uq", 7, 1330, new DateOnly(1912, 7, 15), new DateOnly(1912, 6, 16) },
                    { "uq", 8, 1330, new DateOnly(1912, 8, 13), new DateOnly(1912, 7, 15) },
                    { "uq", 9, 1330, new DateOnly(1912, 9, 12), new DateOnly(1912, 8, 13) },
                    { "uq", 10, 1330, new DateOnly(1912, 10, 11), new DateOnly(1912, 9, 12) },
                    { "uq", 11, 1330, new DateOnly(1912, 11, 10), new DateOnly(1912, 10, 11) },
                    { "uq", 12, 1330, new DateOnly(1912, 12, 9), new DateOnly(1912, 11, 10) },
                    { "uq", 1, 1331, new DateOnly(1913, 1, 8), new DateOnly(1912, 12, 9) },
                    { "uq", 2, 1331, new DateOnly(1913, 2, 7), new DateOnly(1913, 1, 8) },
                    { "uq", 3, 1331, new DateOnly(1913, 3, 9), new DateOnly(1913, 2, 7) },
                    { "uq", 4, 1331, new DateOnly(1913, 4, 7), new DateOnly(1913, 3, 9) },
                    { "uq", 5, 1331, new DateOnly(1913, 5, 7), new DateOnly(1913, 4, 7) },
                    { "uq", 6, 1331, new DateOnly(1913, 6, 5), new DateOnly(1913, 5, 7) },
                    { "uq", 7, 1331, new DateOnly(1913, 7, 5), new DateOnly(1913, 6, 5) },
                    { "uq", 8, 1331, new DateOnly(1913, 8, 3), new DateOnly(1913, 7, 5) },
                    { "uq", 9, 1331, new DateOnly(1913, 9, 1), new DateOnly(1913, 8, 3) },
                    { "uq", 10, 1331, new DateOnly(1913, 10, 1), new DateOnly(1913, 9, 1) },
                    { "uq", 11, 1331, new DateOnly(1913, 10, 30), new DateOnly(1913, 10, 1) },
                    { "uq", 12, 1331, new DateOnly(1913, 11, 29), new DateOnly(1913, 10, 30) },
                    { "uq", 1, 1332, new DateOnly(1913, 12, 28), new DateOnly(1913, 11, 29) },
                    { "uq", 2, 1332, new DateOnly(1914, 1, 27), new DateOnly(1913, 12, 28) },
                    { "uq", 3, 1332, new DateOnly(1914, 2, 26), new DateOnly(1914, 1, 27) },
                    { "uq", 4, 1332, new DateOnly(1914, 3, 27), new DateOnly(1914, 2, 26) },
                    { "uq", 5, 1332, new DateOnly(1914, 4, 26), new DateOnly(1914, 3, 27) },
                    { "uq", 6, 1332, new DateOnly(1914, 5, 26), new DateOnly(1914, 4, 26) },
                    { "uq", 7, 1332, new DateOnly(1914, 6, 24), new DateOnly(1914, 5, 26) },
                    { "uq", 8, 1332, new DateOnly(1914, 7, 24), new DateOnly(1914, 6, 24) },
                    { "uq", 9, 1332, new DateOnly(1914, 8, 22), new DateOnly(1914, 7, 24) },
                    { "uq", 10, 1332, new DateOnly(1914, 9, 20), new DateOnly(1914, 8, 22) },
                    { "uq", 11, 1332, new DateOnly(1914, 10, 20), new DateOnly(1914, 9, 20) },
                    { "uq", 12, 1332, new DateOnly(1914, 11, 18), new DateOnly(1914, 10, 20) },
                    { "uq", 1, 1333, new DateOnly(1914, 12, 18), new DateOnly(1914, 11, 18) },
                    { "uq", 2, 1333, new DateOnly(1915, 1, 16), new DateOnly(1914, 12, 18) },
                    { "uq", 3, 1333, new DateOnly(1915, 2, 15), new DateOnly(1915, 1, 16) },
                    { "uq", 4, 1333, new DateOnly(1915, 3, 16), new DateOnly(1915, 2, 15) },
                    { "uq", 5, 1333, new DateOnly(1915, 4, 15), new DateOnly(1915, 3, 16) },
                    { "uq", 6, 1333, new DateOnly(1915, 5, 15), new DateOnly(1915, 4, 15) },
                    { "uq", 7, 1333, new DateOnly(1915, 6, 13), new DateOnly(1915, 5, 15) },
                    { "uq", 8, 1333, new DateOnly(1915, 7, 13), new DateOnly(1915, 6, 13) },
                    { "uq", 9, 1333, new DateOnly(1915, 8, 11), new DateOnly(1915, 7, 13) },
                    { "uq", 10, 1333, new DateOnly(1915, 9, 10), new DateOnly(1915, 8, 11) },
                    { "uq", 11, 1333, new DateOnly(1915, 10, 9), new DateOnly(1915, 9, 10) },
                    { "uq", 12, 1333, new DateOnly(1915, 11, 8), new DateOnly(1915, 10, 9) },
                    { "uq", 1, 1334, new DateOnly(1915, 12, 7), new DateOnly(1915, 11, 8) },
                    { "uq", 2, 1334, new DateOnly(1916, 1, 6), new DateOnly(1915, 12, 7) },
                    { "uq", 3, 1334, new DateOnly(1916, 2, 4), new DateOnly(1916, 1, 6) },
                    { "uq", 4, 1334, new DateOnly(1916, 3, 5), new DateOnly(1916, 2, 4) },
                    { "uq", 5, 1334, new DateOnly(1916, 4, 3), new DateOnly(1916, 3, 5) },
                    { "uq", 6, 1334, new DateOnly(1916, 5, 3), new DateOnly(1916, 4, 3) },
                    { "uq", 7, 1334, new DateOnly(1916, 6, 1), new DateOnly(1916, 5, 3) },
                    { "uq", 8, 1334, new DateOnly(1916, 7, 1), new DateOnly(1916, 6, 1) },
                    { "uq", 9, 1334, new DateOnly(1916, 7, 31), new DateOnly(1916, 7, 1) },
                    { "uq", 10, 1334, new DateOnly(1916, 8, 29), new DateOnly(1916, 7, 31) },
                    { "uq", 11, 1334, new DateOnly(1916, 9, 28), new DateOnly(1916, 8, 29) },
                    { "uq", 12, 1334, new DateOnly(1916, 10, 27), new DateOnly(1916, 9, 28) },
                    { "uq", 1, 1335, new DateOnly(1916, 11, 26), new DateOnly(1916, 10, 27) },
                    { "uq", 2, 1335, new DateOnly(1916, 12, 25), new DateOnly(1916, 11, 26) },
                    { "uq", 3, 1335, new DateOnly(1917, 1, 24), new DateOnly(1916, 12, 25) },
                    { "uq", 4, 1335, new DateOnly(1917, 2, 22), new DateOnly(1917, 1, 24) },
                    { "uq", 5, 1335, new DateOnly(1917, 3, 24), new DateOnly(1917, 2, 22) },
                    { "uq", 6, 1335, new DateOnly(1917, 4, 22), new DateOnly(1917, 3, 24) },
                    { "uq", 7, 1335, new DateOnly(1917, 5, 22), new DateOnly(1917, 4, 22) },
                    { "uq", 8, 1335, new DateOnly(1917, 6, 20), new DateOnly(1917, 5, 22) },
                    { "uq", 9, 1335, new DateOnly(1917, 7, 20), new DateOnly(1917, 6, 20) },
                    { "uq", 10, 1335, new DateOnly(1917, 8, 18), new DateOnly(1917, 7, 20) },
                    { "uq", 11, 1335, new DateOnly(1917, 9, 17), new DateOnly(1917, 8, 18) },
                    { "uq", 12, 1335, new DateOnly(1917, 10, 17), new DateOnly(1917, 9, 17) },
                    { "uq", 1, 1336, new DateOnly(1917, 11, 15), new DateOnly(1917, 10, 17) },
                    { "uq", 2, 1336, new DateOnly(1917, 12, 15), new DateOnly(1917, 11, 15) },
                    { "uq", 3, 1336, new DateOnly(1918, 1, 13), new DateOnly(1917, 12, 15) },
                    { "uq", 4, 1336, new DateOnly(1918, 2, 12), new DateOnly(1918, 1, 13) },
                    { "uq", 5, 1336, new DateOnly(1918, 3, 13), new DateOnly(1918, 2, 12) },
                    { "uq", 6, 1336, new DateOnly(1918, 4, 12), new DateOnly(1918, 3, 13) },
                    { "uq", 7, 1336, new DateOnly(1918, 5, 11), new DateOnly(1918, 4, 12) },
                    { "uq", 8, 1336, new DateOnly(1918, 6, 9), new DateOnly(1918, 5, 11) },
                    { "uq", 9, 1336, new DateOnly(1918, 7, 9), new DateOnly(1918, 6, 9) },
                    { "uq", 10, 1336, new DateOnly(1918, 8, 7), new DateOnly(1918, 7, 9) },
                    { "uq", 11, 1336, new DateOnly(1918, 9, 6), new DateOnly(1918, 8, 7) },
                    { "uq", 12, 1336, new DateOnly(1918, 10, 6), new DateOnly(1918, 9, 6) },
                    { "uq", 1, 1337, new DateOnly(1918, 11, 4), new DateOnly(1918, 10, 6) },
                    { "uq", 2, 1337, new DateOnly(1918, 12, 4), new DateOnly(1918, 11, 4) },
                    { "uq", 3, 1337, new DateOnly(1919, 1, 3), new DateOnly(1918, 12, 4) },
                    { "uq", 4, 1337, new DateOnly(1919, 2, 1), new DateOnly(1919, 1, 3) },
                    { "uq", 5, 1337, new DateOnly(1919, 3, 3), new DateOnly(1919, 2, 1) },
                    { "uq", 6, 1337, new DateOnly(1919, 4, 1), new DateOnly(1919, 3, 3) },
                    { "uq", 7, 1337, new DateOnly(1919, 5, 1), new DateOnly(1919, 4, 1) },
                    { "uq", 8, 1337, new DateOnly(1919, 5, 30), new DateOnly(1919, 5, 1) },
                    { "uq", 9, 1337, new DateOnly(1919, 6, 28), new DateOnly(1919, 5, 30) },
                    { "uq", 10, 1337, new DateOnly(1919, 7, 28), new DateOnly(1919, 6, 28) },
                    { "uq", 11, 1337, new DateOnly(1919, 8, 26), new DateOnly(1919, 7, 28) },
                    { "uq", 12, 1337, new DateOnly(1919, 9, 25), new DateOnly(1919, 8, 26) },
                    { "uq", 1, 1338, new DateOnly(1919, 10, 24), new DateOnly(1919, 9, 25) },
                    { "uq", 2, 1338, new DateOnly(1919, 11, 23), new DateOnly(1919, 10, 24) },
                    { "uq", 3, 1338, new DateOnly(1919, 12, 23), new DateOnly(1919, 11, 23) },
                    { "uq", 4, 1338, new DateOnly(1920, 1, 22), new DateOnly(1919, 12, 23) },
                    { "uq", 5, 1338, new DateOnly(1920, 2, 20), new DateOnly(1920, 1, 22) },
                    { "uq", 6, 1338, new DateOnly(1920, 3, 21), new DateOnly(1920, 2, 20) },
                    { "uq", 7, 1338, new DateOnly(1920, 4, 19), new DateOnly(1920, 3, 21) },
                    { "uq", 8, 1338, new DateOnly(1920, 5, 19), new DateOnly(1920, 4, 19) },
                    { "uq", 9, 1338, new DateOnly(1920, 6, 17), new DateOnly(1920, 5, 19) },
                    { "uq", 10, 1338, new DateOnly(1920, 7, 16), new DateOnly(1920, 6, 17) },
                    { "uq", 11, 1338, new DateOnly(1920, 8, 15), new DateOnly(1920, 7, 16) },
                    { "uq", 12, 1338, new DateOnly(1920, 9, 13), new DateOnly(1920, 8, 15) },
                    { "uq", 1, 1339, new DateOnly(1920, 10, 13), new DateOnly(1920, 9, 13) },
                    { "uq", 2, 1339, new DateOnly(1920, 11, 11), new DateOnly(1920, 10, 13) },
                    { "uq", 3, 1339, new DateOnly(1920, 12, 11), new DateOnly(1920, 11, 11) },
                    { "uq", 4, 1339, new DateOnly(1921, 1, 10), new DateOnly(1920, 12, 11) },
                    { "uq", 5, 1339, new DateOnly(1921, 2, 9), new DateOnly(1921, 1, 10) },
                    { "uq", 6, 1339, new DateOnly(1921, 3, 10), new DateOnly(1921, 2, 9) },
                    { "uq", 7, 1339, new DateOnly(1921, 4, 9), new DateOnly(1921, 3, 10) },
                    { "uq", 8, 1339, new DateOnly(1921, 5, 8), new DateOnly(1921, 4, 9) },
                    { "uq", 9, 1339, new DateOnly(1921, 6, 7), new DateOnly(1921, 5, 8) },
                    { "uq", 10, 1339, new DateOnly(1921, 7, 6), new DateOnly(1921, 6, 7) },
                    { "uq", 11, 1339, new DateOnly(1921, 8, 4), new DateOnly(1921, 7, 6) },
                    { "uq", 12, 1339, new DateOnly(1921, 9, 3), new DateOnly(1921, 8, 4) },
                    { "uq", 1, 1340, new DateOnly(1921, 10, 2), new DateOnly(1921, 9, 3) },
                    { "uq", 2, 1340, new DateOnly(1921, 10, 31), new DateOnly(1921, 10, 2) },
                    { "uq", 3, 1340, new DateOnly(1921, 11, 30), new DateOnly(1921, 10, 31) },
                    { "uq", 4, 1340, new DateOnly(1921, 12, 30), new DateOnly(1921, 11, 30) },
                    { "uq", 5, 1340, new DateOnly(1922, 1, 28), new DateOnly(1921, 12, 30) },
                    { "uq", 6, 1340, new DateOnly(1922, 2, 27), new DateOnly(1922, 1, 28) },
                    { "uq", 7, 1340, new DateOnly(1922, 3, 29), new DateOnly(1922, 2, 27) },
                    { "uq", 8, 1340, new DateOnly(1922, 4, 28), new DateOnly(1922, 3, 29) },
                    { "uq", 9, 1340, new DateOnly(1922, 5, 27), new DateOnly(1922, 4, 28) },
                    { "uq", 10, 1340, new DateOnly(1922, 6, 26), new DateOnly(1922, 5, 27) },
                    { "uq", 11, 1340, new DateOnly(1922, 7, 25), new DateOnly(1922, 6, 26) },
                    { "uq", 12, 1340, new DateOnly(1922, 8, 23), new DateOnly(1922, 7, 25) },
                    { "uq", 1, 1341, new DateOnly(1922, 9, 22), new DateOnly(1922, 8, 23) },
                    { "uq", 2, 1341, new DateOnly(1922, 10, 21), new DateOnly(1922, 9, 22) },
                    { "uq", 3, 1341, new DateOnly(1922, 11, 20), new DateOnly(1922, 10, 21) },
                    { "uq", 4, 1341, new DateOnly(1922, 12, 19), new DateOnly(1922, 11, 20) },
                    { "uq", 5, 1341, new DateOnly(1923, 1, 18), new DateOnly(1922, 12, 19) },
                    { "uq", 6, 1341, new DateOnly(1923, 2, 16), new DateOnly(1923, 1, 18) },
                    { "uq", 7, 1341, new DateOnly(1923, 3, 18), new DateOnly(1923, 2, 16) },
                    { "uq", 8, 1341, new DateOnly(1923, 4, 17), new DateOnly(1923, 3, 18) },
                    { "uq", 9, 1341, new DateOnly(1923, 5, 16), new DateOnly(1923, 4, 17) },
                    { "uq", 10, 1341, new DateOnly(1923, 6, 15), new DateOnly(1923, 5, 16) },
                    { "uq", 11, 1341, new DateOnly(1923, 7, 15), new DateOnly(1923, 6, 15) },
                    { "uq", 12, 1341, new DateOnly(1923, 8, 13), new DateOnly(1923, 7, 15) },
                    { "uq", 1, 1342, new DateOnly(1923, 9, 11), new DateOnly(1923, 8, 13) },
                    { "uq", 2, 1342, new DateOnly(1923, 10, 11), new DateOnly(1923, 9, 11) },
                    { "uq", 3, 1342, new DateOnly(1923, 11, 9), new DateOnly(1923, 10, 11) },
                    { "uq", 4, 1342, new DateOnly(1923, 12, 9), new DateOnly(1923, 11, 9) },
                    { "uq", 5, 1342, new DateOnly(1924, 1, 7), new DateOnly(1923, 12, 9) },
                    { "uq", 6, 1342, new DateOnly(1924, 2, 6), new DateOnly(1924, 1, 7) },
                    { "uq", 7, 1342, new DateOnly(1924, 3, 6), new DateOnly(1924, 2, 6) },
                    { "uq", 8, 1342, new DateOnly(1924, 4, 5), new DateOnly(1924, 3, 6) },
                    { "uq", 9, 1342, new DateOnly(1924, 5, 4), new DateOnly(1924, 4, 5) },
                    { "uq", 10, 1342, new DateOnly(1924, 6, 3), new DateOnly(1924, 5, 4) },
                    { "uq", 11, 1342, new DateOnly(1924, 7, 3), new DateOnly(1924, 6, 3) },
                    { "uq", 12, 1342, new DateOnly(1924, 8, 1), new DateOnly(1924, 7, 3) },
                    { "uq", 1, 1343, new DateOnly(1924, 8, 31), new DateOnly(1924, 8, 1) },
                    { "uq", 2, 1343, new DateOnly(1924, 9, 29), new DateOnly(1924, 8, 31) },
                    { "uq", 3, 1343, new DateOnly(1924, 10, 29), new DateOnly(1924, 9, 29) },
                    { "uq", 4, 1343, new DateOnly(1924, 11, 27), new DateOnly(1924, 10, 29) },
                    { "uq", 5, 1343, new DateOnly(1924, 12, 27), new DateOnly(1924, 11, 27) },
                    { "uq", 6, 1343, new DateOnly(1925, 1, 25), new DateOnly(1924, 12, 27) },
                    { "uq", 7, 1343, new DateOnly(1925, 2, 24), new DateOnly(1925, 1, 25) },
                    { "uq", 8, 1343, new DateOnly(1925, 3, 25), new DateOnly(1925, 2, 24) },
                    { "uq", 9, 1343, new DateOnly(1925, 4, 24), new DateOnly(1925, 3, 25) },
                    { "uq", 10, 1343, new DateOnly(1925, 5, 23), new DateOnly(1925, 4, 24) },
                    { "uq", 11, 1343, new DateOnly(1925, 6, 22), new DateOnly(1925, 5, 23) },
                    { "uq", 12, 1343, new DateOnly(1925, 7, 21), new DateOnly(1925, 6, 22) },
                    { "uq", 1, 1344, new DateOnly(1925, 8, 20), new DateOnly(1925, 7, 21) },
                    { "uq", 2, 1344, new DateOnly(1925, 9, 19), new DateOnly(1925, 8, 20) },
                    { "uq", 3, 1344, new DateOnly(1925, 10, 18), new DateOnly(1925, 9, 19) },
                    { "uq", 4, 1344, new DateOnly(1925, 11, 17), new DateOnly(1925, 10, 18) },
                    { "uq", 5, 1344, new DateOnly(1925, 12, 16), new DateOnly(1925, 11, 17) },
                    { "uq", 6, 1344, new DateOnly(1926, 1, 15), new DateOnly(1925, 12, 16) },
                    { "uq", 7, 1344, new DateOnly(1926, 2, 13), new DateOnly(1926, 1, 15) },
                    { "uq", 8, 1344, new DateOnly(1926, 3, 15), new DateOnly(1926, 2, 13) },
                    { "uq", 9, 1344, new DateOnly(1926, 4, 13), new DateOnly(1926, 3, 15) },
                    { "uq", 10, 1344, new DateOnly(1926, 5, 12), new DateOnly(1926, 4, 13) },
                    { "uq", 11, 1344, new DateOnly(1926, 6, 11), new DateOnly(1926, 5, 12) },
                    { "uq", 12, 1344, new DateOnly(1926, 7, 10), new DateOnly(1926, 6, 11) },
                    { "uq", 1, 1345, new DateOnly(1926, 8, 9), new DateOnly(1926, 7, 10) },
                    { "uq", 2, 1345, new DateOnly(1926, 9, 8), new DateOnly(1926, 8, 9) },
                    { "uq", 3, 1345, new DateOnly(1926, 10, 7), new DateOnly(1926, 9, 8) },
                    { "uq", 4, 1345, new DateOnly(1926, 11, 6), new DateOnly(1926, 10, 7) },
                    { "uq", 5, 1345, new DateOnly(1926, 12, 6), new DateOnly(1926, 11, 6) },
                    { "uq", 6, 1345, new DateOnly(1927, 1, 4), new DateOnly(1926, 12, 6) },
                    { "uq", 7, 1345, new DateOnly(1927, 2, 3), new DateOnly(1927, 1, 4) },
                    { "uq", 8, 1345, new DateOnly(1927, 3, 4), new DateOnly(1927, 2, 3) },
                    { "uq", 9, 1345, new DateOnly(1927, 4, 3), new DateOnly(1927, 3, 4) },
                    { "uq", 10, 1345, new DateOnly(1927, 5, 2), new DateOnly(1927, 4, 3) },
                    { "uq", 11, 1345, new DateOnly(1927, 5, 31), new DateOnly(1927, 5, 2) },
                    { "uq", 12, 1345, new DateOnly(1927, 6, 30), new DateOnly(1927, 5, 31) },
                    { "uq", 1, 1346, new DateOnly(1927, 7, 29), new DateOnly(1927, 6, 30) },
                    { "uq", 2, 1346, new DateOnly(1927, 8, 28), new DateOnly(1927, 7, 29) },
                    { "uq", 3, 1346, new DateOnly(1927, 9, 26), new DateOnly(1927, 8, 28) },
                    { "uq", 4, 1346, new DateOnly(1927, 10, 26), new DateOnly(1927, 9, 26) },
                    { "uq", 5, 1346, new DateOnly(1927, 11, 25), new DateOnly(1927, 10, 26) },
                    { "uq", 6, 1346, new DateOnly(1927, 12, 25), new DateOnly(1927, 11, 25) },
                    { "uq", 7, 1346, new DateOnly(1928, 1, 23), new DateOnly(1927, 12, 25) },
                    { "uq", 8, 1346, new DateOnly(1928, 2, 22), new DateOnly(1928, 1, 23) },
                    { "uq", 9, 1346, new DateOnly(1928, 3, 22), new DateOnly(1928, 2, 22) },
                    { "uq", 10, 1346, new DateOnly(1928, 4, 21), new DateOnly(1928, 3, 22) },
                    { "uq", 11, 1346, new DateOnly(1928, 5, 20), new DateOnly(1928, 4, 21) },
                    { "uq", 12, 1346, new DateOnly(1928, 6, 18), new DateOnly(1928, 5, 20) },
                    { "uq", 1, 1347, new DateOnly(1928, 7, 18), new DateOnly(1928, 6, 18) },
                    { "uq", 2, 1347, new DateOnly(1928, 8, 16), new DateOnly(1928, 7, 18) },
                    { "uq", 3, 1347, new DateOnly(1928, 9, 15), new DateOnly(1928, 8, 16) },
                    { "uq", 4, 1347, new DateOnly(1928, 10, 14), new DateOnly(1928, 9, 15) },
                    { "uq", 5, 1347, new DateOnly(1928, 11, 13), new DateOnly(1928, 10, 14) },
                    { "uq", 6, 1347, new DateOnly(1928, 12, 13), new DateOnly(1928, 11, 13) },
                    { "uq", 7, 1347, new DateOnly(1929, 1, 12), new DateOnly(1928, 12, 13) },
                    { "uq", 8, 1347, new DateOnly(1929, 2, 10), new DateOnly(1929, 1, 12) },
                    { "uq", 9, 1347, new DateOnly(1929, 3, 12), new DateOnly(1929, 2, 10) },
                    { "uq", 10, 1347, new DateOnly(1929, 4, 10), new DateOnly(1929, 3, 12) },
                    { "uq", 11, 1347, new DateOnly(1929, 5, 10), new DateOnly(1929, 4, 10) },
                    { "uq", 12, 1347, new DateOnly(1929, 6, 8), new DateOnly(1929, 5, 10) },
                    { "uq", 1, 1348, new DateOnly(1929, 7, 7), new DateOnly(1929, 6, 8) },
                    { "uq", 2, 1348, new DateOnly(1929, 8, 6), new DateOnly(1929, 7, 7) },
                    { "uq", 3, 1348, new DateOnly(1929, 9, 4), new DateOnly(1929, 8, 6) },
                    { "uq", 4, 1348, new DateOnly(1929, 10, 3), new DateOnly(1929, 9, 4) },
                    { "uq", 5, 1348, new DateOnly(1929, 11, 2), new DateOnly(1929, 10, 3) },
                    { "uq", 6, 1348, new DateOnly(1929, 12, 2), new DateOnly(1929, 11, 2) },
                    { "uq", 7, 1348, new DateOnly(1929, 12, 31), new DateOnly(1929, 12, 2) },
                    { "uq", 8, 1348, new DateOnly(1930, 1, 30), new DateOnly(1929, 12, 31) },
                    { "uq", 9, 1348, new DateOnly(1930, 3, 1), new DateOnly(1930, 1, 30) },
                    { "uq", 10, 1348, new DateOnly(1930, 3, 31), new DateOnly(1930, 3, 1) },
                    { "uq", 11, 1348, new DateOnly(1930, 4, 29), new DateOnly(1930, 3, 31) },
                    { "uq", 12, 1348, new DateOnly(1930, 5, 29), new DateOnly(1930, 4, 29) },
                    { "uq", 1, 1349, new DateOnly(1930, 6, 27), new DateOnly(1930, 5, 29) },
                    { "uq", 2, 1349, new DateOnly(1930, 7, 26), new DateOnly(1930, 6, 27) },
                    { "uq", 3, 1349, new DateOnly(1930, 8, 25), new DateOnly(1930, 7, 26) },
                    { "uq", 4, 1349, new DateOnly(1930, 9, 23), new DateOnly(1930, 8, 25) },
                    { "uq", 5, 1349, new DateOnly(1930, 10, 22), new DateOnly(1930, 9, 23) },
                    { "uq", 6, 1349, new DateOnly(1930, 11, 21), new DateOnly(1930, 10, 22) },
                    { "uq", 7, 1349, new DateOnly(1930, 12, 21), new DateOnly(1930, 11, 21) },
                    { "uq", 8, 1349, new DateOnly(1931, 1, 19), new DateOnly(1930, 12, 21) },
                    { "uq", 9, 1349, new DateOnly(1931, 2, 18), new DateOnly(1931, 1, 19) },
                    { "uq", 10, 1349, new DateOnly(1931, 3, 20), new DateOnly(1931, 2, 18) },
                    { "uq", 11, 1349, new DateOnly(1931, 4, 19), new DateOnly(1931, 3, 20) },
                    { "uq", 12, 1349, new DateOnly(1931, 5, 18), new DateOnly(1931, 4, 19) },
                    { "uq", 1, 1350, new DateOnly(1931, 6, 17), new DateOnly(1931, 5, 18) },
                    { "uq", 2, 1350, new DateOnly(1931, 7, 16), new DateOnly(1931, 6, 17) },
                    { "uq", 3, 1350, new DateOnly(1931, 8, 14), new DateOnly(1931, 7, 16) },
                    { "uq", 4, 1350, new DateOnly(1931, 9, 13), new DateOnly(1931, 8, 14) },
                    { "uq", 5, 1350, new DateOnly(1931, 10, 12), new DateOnly(1931, 9, 13) },
                    { "uq", 6, 1350, new DateOnly(1931, 11, 10), new DateOnly(1931, 10, 12) },
                    { "uq", 7, 1350, new DateOnly(1931, 12, 10), new DateOnly(1931, 11, 10) },
                    { "uq", 8, 1350, new DateOnly(1932, 1, 8), new DateOnly(1931, 12, 10) },
                    { "uq", 9, 1350, new DateOnly(1932, 2, 7), new DateOnly(1932, 1, 8) },
                    { "uq", 10, 1350, new DateOnly(1932, 3, 8), new DateOnly(1932, 2, 7) },
                    { "uq", 11, 1350, new DateOnly(1932, 4, 7), new DateOnly(1932, 3, 8) },
                    { "uq", 12, 1350, new DateOnly(1932, 5, 6), new DateOnly(1932, 4, 7) },
                    { "uq", 1, 1351, new DateOnly(1932, 6, 5), new DateOnly(1932, 5, 6) },
                    { "uq", 2, 1351, new DateOnly(1932, 7, 4), new DateOnly(1932, 6, 5) },
                    { "uq", 3, 1351, new DateOnly(1932, 8, 3), new DateOnly(1932, 7, 4) },
                    { "uq", 4, 1351, new DateOnly(1932, 9, 1), new DateOnly(1932, 8, 3) },
                    { "uq", 5, 1351, new DateOnly(1932, 10, 1), new DateOnly(1932, 9, 1) },
                    { "uq", 6, 1351, new DateOnly(1932, 10, 30), new DateOnly(1932, 10, 1) },
                    { "uq", 7, 1351, new DateOnly(1932, 11, 29), new DateOnly(1932, 10, 30) },
                    { "uq", 8, 1351, new DateOnly(1932, 12, 28), new DateOnly(1932, 11, 29) },
                    { "uq", 9, 1351, new DateOnly(1933, 1, 26), new DateOnly(1932, 12, 28) },
                    { "uq", 10, 1351, new DateOnly(1933, 2, 25), new DateOnly(1933, 1, 26) },
                    { "uq", 11, 1351, new DateOnly(1933, 3, 27), new DateOnly(1933, 2, 25) },
                    { "uq", 12, 1351, new DateOnly(1933, 4, 25), new DateOnly(1933, 3, 27) },
                    { "uq", 1, 1352, new DateOnly(1933, 5, 25), new DateOnly(1933, 4, 25) },
                    { "uq", 2, 1352, new DateOnly(1933, 6, 24), new DateOnly(1933, 5, 25) },
                    { "uq", 3, 1352, new DateOnly(1933, 7, 23), new DateOnly(1933, 6, 24) },
                    { "uq", 4, 1352, new DateOnly(1933, 8, 22), new DateOnly(1933, 7, 23) },
                    { "uq", 5, 1352, new DateOnly(1933, 9, 20), new DateOnly(1933, 8, 22) },
                    { "uq", 6, 1352, new DateOnly(1933, 10, 20), new DateOnly(1933, 9, 20) },
                    { "uq", 7, 1352, new DateOnly(1933, 11, 18), new DateOnly(1933, 10, 20) },
                    { "uq", 8, 1352, new DateOnly(1933, 12, 18), new DateOnly(1933, 11, 18) },
                    { "uq", 9, 1352, new DateOnly(1934, 1, 16), new DateOnly(1933, 12, 18) },
                    { "uq", 10, 1352, new DateOnly(1934, 2, 15), new DateOnly(1934, 1, 16) },
                    { "uq", 11, 1352, new DateOnly(1934, 3, 16), new DateOnly(1934, 2, 15) },
                    { "uq", 12, 1352, new DateOnly(1934, 4, 14), new DateOnly(1934, 3, 16) },
                    { "uq", 1, 1353, new DateOnly(1934, 5, 14), new DateOnly(1934, 4, 14) },
                    { "uq", 2, 1353, new DateOnly(1934, 6, 13), new DateOnly(1934, 5, 14) },
                    { "uq", 3, 1353, new DateOnly(1934, 7, 12), new DateOnly(1934, 6, 13) },
                    { "uq", 4, 1353, new DateOnly(1934, 8, 11), new DateOnly(1934, 7, 12) },
                    { "uq", 5, 1353, new DateOnly(1934, 9, 10), new DateOnly(1934, 8, 11) },
                    { "uq", 6, 1353, new DateOnly(1934, 10, 9), new DateOnly(1934, 9, 10) },
                    { "uq", 7, 1353, new DateOnly(1934, 11, 8), new DateOnly(1934, 10, 9) },
                    { "uq", 8, 1353, new DateOnly(1934, 12, 7), new DateOnly(1934, 11, 8) },
                    { "uq", 9, 1353, new DateOnly(1935, 1, 6), new DateOnly(1934, 12, 7) },
                    { "uq", 10, 1353, new DateOnly(1935, 2, 4), new DateOnly(1935, 1, 6) },
                    { "uq", 11, 1353, new DateOnly(1935, 3, 6), new DateOnly(1935, 2, 4) },
                    { "uq", 12, 1353, new DateOnly(1935, 4, 4), new DateOnly(1935, 3, 6) },
                    { "uq", 1, 1354, new DateOnly(1935, 5, 3), new DateOnly(1935, 4, 4) },
                    { "uq", 2, 1354, new DateOnly(1935, 6, 2), new DateOnly(1935, 5, 3) },
                    { "uq", 3, 1354, new DateOnly(1935, 7, 1), new DateOnly(1935, 6, 2) },
                    { "uq", 4, 1354, new DateOnly(1935, 7, 31), new DateOnly(1935, 7, 1) },
                    { "uq", 5, 1354, new DateOnly(1935, 8, 30), new DateOnly(1935, 7, 31) },
                    { "uq", 6, 1354, new DateOnly(1935, 9, 28), new DateOnly(1935, 8, 30) },
                    { "uq", 7, 1354, new DateOnly(1935, 10, 28), new DateOnly(1935, 9, 28) },
                    { "uq", 8, 1354, new DateOnly(1935, 11, 27), new DateOnly(1935, 10, 28) },
                    { "uq", 9, 1354, new DateOnly(1935, 12, 26), new DateOnly(1935, 11, 27) },
                    { "uq", 10, 1354, new DateOnly(1936, 1, 25), new DateOnly(1935, 12, 26) },
                    { "uq", 11, 1354, new DateOnly(1936, 2, 23), new DateOnly(1936, 1, 25) },
                    { "uq", 12, 1354, new DateOnly(1936, 3, 24), new DateOnly(1936, 2, 23) },
                    { "uq", 1, 1355, new DateOnly(1936, 4, 22), new DateOnly(1936, 3, 24) },
                    { "uq", 2, 1355, new DateOnly(1936, 5, 21), new DateOnly(1936, 4, 22) },
                    { "uq", 3, 1355, new DateOnly(1936, 6, 20), new DateOnly(1936, 5, 21) },
                    { "uq", 4, 1355, new DateOnly(1936, 7, 19), new DateOnly(1936, 6, 20) },
                    { "uq", 5, 1355, new DateOnly(1936, 8, 18), new DateOnly(1936, 7, 19) },
                    { "uq", 6, 1355, new DateOnly(1936, 9, 16), new DateOnly(1936, 8, 18) },
                    { "uq", 7, 1355, new DateOnly(1936, 10, 16), new DateOnly(1936, 9, 16) },
                    { "uq", 8, 1355, new DateOnly(1936, 11, 15), new DateOnly(1936, 10, 16) },
                    { "uq", 9, 1355, new DateOnly(1936, 12, 14), new DateOnly(1936, 11, 15) },
                    { "uq", 10, 1355, new DateOnly(1937, 1, 13), new DateOnly(1936, 12, 14) },
                    { "uq", 11, 1355, new DateOnly(1937, 2, 12), new DateOnly(1937, 1, 13) },
                    { "uq", 12, 1355, new DateOnly(1937, 3, 13), new DateOnly(1937, 2, 12) },
                    { "uq", 1, 1356, new DateOnly(1937, 4, 12), new DateOnly(1937, 3, 13) },
                    { "uq", 2, 1356, new DateOnly(1937, 5, 11), new DateOnly(1937, 4, 12) },
                    { "uq", 3, 1356, new DateOnly(1937, 6, 9), new DateOnly(1937, 5, 11) },
                    { "uq", 4, 1356, new DateOnly(1937, 7, 9), new DateOnly(1937, 6, 9) },
                    { "uq", 5, 1356, new DateOnly(1937, 8, 7), new DateOnly(1937, 7, 9) },
                    { "uq", 6, 1356, new DateOnly(1937, 9, 5), new DateOnly(1937, 8, 7) },
                    { "uq", 7, 1356, new DateOnly(1937, 10, 5), new DateOnly(1937, 9, 5) },
                    { "uq", 8, 1356, new DateOnly(1937, 11, 4), new DateOnly(1937, 10, 5) },
                    { "uq", 9, 1356, new DateOnly(1937, 12, 3), new DateOnly(1937, 11, 4) },
                    { "uq", 10, 1356, new DateOnly(1938, 1, 2), new DateOnly(1937, 12, 3) },
                    { "uq", 11, 1356, new DateOnly(1938, 2, 1), new DateOnly(1938, 1, 2) },
                    { "uq", 12, 1356, new DateOnly(1938, 3, 3), new DateOnly(1938, 2, 1) },
                    { "uq", 1, 1357, new DateOnly(1938, 4, 1), new DateOnly(1938, 3, 3) },
                    { "uq", 2, 1357, new DateOnly(1938, 5, 1), new DateOnly(1938, 4, 1) },
                    { "uq", 3, 1357, new DateOnly(1938, 5, 30), new DateOnly(1938, 5, 1) },
                    { "uq", 4, 1357, new DateOnly(1938, 6, 28), new DateOnly(1938, 5, 30) },
                    { "uq", 5, 1357, new DateOnly(1938, 7, 28), new DateOnly(1938, 6, 28) },
                    { "uq", 6, 1357, new DateOnly(1938, 8, 26), new DateOnly(1938, 7, 28) },
                    { "uq", 7, 1357, new DateOnly(1938, 9, 24), new DateOnly(1938, 8, 26) },
                    { "uq", 8, 1357, new DateOnly(1938, 10, 24), new DateOnly(1938, 9, 24) },
                    { "uq", 9, 1357, new DateOnly(1938, 11, 23), new DateOnly(1938, 10, 24) },
                    { "uq", 10, 1357, new DateOnly(1938, 12, 22), new DateOnly(1938, 11, 23) },
                    { "uq", 11, 1357, new DateOnly(1939, 1, 21), new DateOnly(1938, 12, 22) },
                    { "uq", 12, 1357, new DateOnly(1939, 2, 20), new DateOnly(1939, 1, 21) },
                    { "uq", 1, 1358, new DateOnly(1939, 3, 22), new DateOnly(1939, 2, 20) },
                    { "uq", 2, 1358, new DateOnly(1939, 4, 20), new DateOnly(1939, 3, 22) },
                    { "uq", 3, 1358, new DateOnly(1939, 5, 20), new DateOnly(1939, 4, 20) },
                    { "uq", 4, 1358, new DateOnly(1939, 6, 18), new DateOnly(1939, 5, 20) },
                    { "uq", 5, 1358, new DateOnly(1939, 7, 17), new DateOnly(1939, 6, 18) },
                    { "uq", 6, 1358, new DateOnly(1939, 8, 16), new DateOnly(1939, 7, 17) },
                    { "uq", 7, 1358, new DateOnly(1939, 9, 14), new DateOnly(1939, 8, 16) },
                    { "uq", 8, 1358, new DateOnly(1939, 10, 13), new DateOnly(1939, 9, 14) },
                    { "uq", 9, 1358, new DateOnly(1939, 11, 12), new DateOnly(1939, 10, 13) },
                    { "uq", 10, 1358, new DateOnly(1939, 12, 11), new DateOnly(1939, 11, 12) },
                    { "uq", 11, 1358, new DateOnly(1940, 1, 10), new DateOnly(1939, 12, 11) },
                    { "uq", 12, 1358, new DateOnly(1940, 2, 9), new DateOnly(1940, 1, 10) },
                    { "uq", 1, 1359, new DateOnly(1940, 3, 10), new DateOnly(1940, 2, 9) },
                    { "uq", 2, 1359, new DateOnly(1940, 4, 8), new DateOnly(1940, 3, 10) },
                    { "uq", 3, 1359, new DateOnly(1940, 5, 8), new DateOnly(1940, 4, 8) },
                    { "uq", 4, 1359, new DateOnly(1940, 6, 7), new DateOnly(1940, 5, 8) },
                    { "uq", 5, 1359, new DateOnly(1940, 7, 6), new DateOnly(1940, 6, 7) },
                    { "uq", 6, 1359, new DateOnly(1940, 8, 4), new DateOnly(1940, 7, 6) },
                    { "uq", 7, 1359, new DateOnly(1940, 9, 3), new DateOnly(1940, 8, 4) },
                    { "uq", 8, 1359, new DateOnly(1940, 10, 2), new DateOnly(1940, 9, 3) },
                    { "uq", 9, 1359, new DateOnly(1940, 10, 31), new DateOnly(1940, 10, 2) },
                    { "uq", 10, 1359, new DateOnly(1940, 11, 30), new DateOnly(1940, 10, 31) },
                    { "uq", 11, 1359, new DateOnly(1940, 12, 29), new DateOnly(1940, 11, 30) },
                    { "uq", 12, 1359, new DateOnly(1941, 1, 28), new DateOnly(1940, 12, 29) },
                    { "uq", 1, 1360, new DateOnly(1941, 2, 27), new DateOnly(1941, 1, 28) },
                    { "uq", 2, 1360, new DateOnly(1941, 3, 28), new DateOnly(1941, 2, 27) },
                    { "uq", 3, 1360, new DateOnly(1941, 4, 27), new DateOnly(1941, 3, 28) },
                    { "uq", 4, 1360, new DateOnly(1941, 5, 27), new DateOnly(1941, 4, 27) },
                    { "uq", 5, 1360, new DateOnly(1941, 6, 25), new DateOnly(1941, 5, 27) },
                    { "uq", 6, 1360, new DateOnly(1941, 7, 25), new DateOnly(1941, 6, 25) },
                    { "uq", 7, 1360, new DateOnly(1941, 8, 23), new DateOnly(1941, 7, 25) },
                    { "uq", 8, 1360, new DateOnly(1941, 9, 22), new DateOnly(1941, 8, 23) },
                    { "uq", 9, 1360, new DateOnly(1941, 10, 21), new DateOnly(1941, 9, 22) },
                    { "uq", 10, 1360, new DateOnly(1941, 11, 20), new DateOnly(1941, 10, 21) },
                    { "uq", 11, 1360, new DateOnly(1941, 12, 19), new DateOnly(1941, 11, 20) },
                    { "uq", 12, 1360, new DateOnly(1942, 1, 17), new DateOnly(1941, 12, 19) },
                    { "uq", 1, 1361, new DateOnly(1942, 2, 16), new DateOnly(1942, 1, 17) },
                    { "uq", 2, 1361, new DateOnly(1942, 3, 17), new DateOnly(1942, 2, 16) },
                    { "uq", 3, 1361, new DateOnly(1942, 4, 16), new DateOnly(1942, 3, 17) },
                    { "uq", 4, 1361, new DateOnly(1942, 5, 16), new DateOnly(1942, 4, 16) },
                    { "uq", 5, 1361, new DateOnly(1942, 6, 14), new DateOnly(1942, 5, 16) },
                    { "uq", 6, 1361, new DateOnly(1942, 7, 14), new DateOnly(1942, 6, 14) },
                    { "uq", 7, 1361, new DateOnly(1942, 8, 13), new DateOnly(1942, 7, 14) },
                    { "uq", 8, 1361, new DateOnly(1942, 9, 11), new DateOnly(1942, 8, 13) },
                    { "uq", 9, 1361, new DateOnly(1942, 10, 11), new DateOnly(1942, 9, 11) },
                    { "uq", 10, 1361, new DateOnly(1942, 11, 9), new DateOnly(1942, 10, 11) },
                    { "uq", 11, 1361, new DateOnly(1942, 12, 9), new DateOnly(1942, 11, 9) },
                    { "uq", 12, 1361, new DateOnly(1943, 1, 7), new DateOnly(1942, 12, 9) },
                    { "uq", 1, 1362, new DateOnly(1943, 2, 5), new DateOnly(1943, 1, 7) },
                    { "uq", 2, 1362, new DateOnly(1943, 3, 7), new DateOnly(1943, 2, 5) },
                    { "uq", 3, 1362, new DateOnly(1943, 4, 5), new DateOnly(1943, 3, 7) },
                    { "uq", 4, 1362, new DateOnly(1943, 5, 5), new DateOnly(1943, 4, 5) },
                    { "uq", 5, 1362, new DateOnly(1943, 6, 3), new DateOnly(1943, 5, 5) },
                    { "uq", 6, 1362, new DateOnly(1943, 7, 3), new DateOnly(1943, 6, 3) },
                    { "uq", 7, 1362, new DateOnly(1943, 8, 2), new DateOnly(1943, 7, 3) },
                    { "uq", 8, 1362, new DateOnly(1943, 8, 31), new DateOnly(1943, 8, 2) },
                    { "uq", 9, 1362, new DateOnly(1943, 9, 30), new DateOnly(1943, 8, 31) },
                    { "uq", 10, 1362, new DateOnly(1943, 10, 30), new DateOnly(1943, 9, 30) },
                    { "uq", 11, 1362, new DateOnly(1943, 11, 28), new DateOnly(1943, 10, 30) },
                    { "uq", 12, 1362, new DateOnly(1943, 12, 28), new DateOnly(1943, 11, 28) },
                    { "uq", 1, 1363, new DateOnly(1944, 1, 26), new DateOnly(1943, 12, 28) },
                    { "uq", 2, 1363, new DateOnly(1944, 2, 25), new DateOnly(1944, 1, 26) },
                    { "uq", 3, 1363, new DateOnly(1944, 3, 25), new DateOnly(1944, 2, 25) },
                    { "uq", 4, 1363, new DateOnly(1944, 4, 23), new DateOnly(1944, 3, 25) },
                    { "uq", 5, 1363, new DateOnly(1944, 5, 23), new DateOnly(1944, 4, 23) },
                    { "uq", 6, 1363, new DateOnly(1944, 6, 21), new DateOnly(1944, 5, 23) },
                    { "uq", 7, 1363, new DateOnly(1944, 7, 21), new DateOnly(1944, 6, 21) },
                    { "uq", 8, 1363, new DateOnly(1944, 8, 19), new DateOnly(1944, 7, 21) },
                    { "uq", 9, 1363, new DateOnly(1944, 9, 18), new DateOnly(1944, 8, 19) },
                    { "uq", 10, 1363, new DateOnly(1944, 10, 18), new DateOnly(1944, 9, 18) },
                    { "uq", 11, 1363, new DateOnly(1944, 11, 16), new DateOnly(1944, 10, 18) },
                    { "uq", 12, 1363, new DateOnly(1944, 12, 16), new DateOnly(1944, 11, 16) },
                    { "uq", 1, 1364, new DateOnly(1945, 1, 15), new DateOnly(1944, 12, 16) },
                    { "uq", 2, 1364, new DateOnly(1945, 2, 13), new DateOnly(1945, 1, 15) },
                    { "uq", 3, 1364, new DateOnly(1945, 3, 15), new DateOnly(1945, 2, 13) },
                    { "uq", 4, 1364, new DateOnly(1945, 4, 13), new DateOnly(1945, 3, 15) },
                    { "uq", 5, 1364, new DateOnly(1945, 5, 12), new DateOnly(1945, 4, 13) },
                    { "uq", 6, 1364, new DateOnly(1945, 6, 11), new DateOnly(1945, 5, 12) },
                    { "uq", 7, 1364, new DateOnly(1945, 7, 10), new DateOnly(1945, 6, 11) },
                    { "uq", 8, 1364, new DateOnly(1945, 8, 9), new DateOnly(1945, 7, 10) },
                    { "uq", 9, 1364, new DateOnly(1945, 9, 7), new DateOnly(1945, 8, 9) },
                    { "uq", 10, 1364, new DateOnly(1945, 10, 7), new DateOnly(1945, 9, 7) },
                    { "uq", 11, 1364, new DateOnly(1945, 11, 5), new DateOnly(1945, 10, 7) },
                    { "uq", 12, 1364, new DateOnly(1945, 12, 5), new DateOnly(1945, 11, 5) },
                    { "uq", 1, 1365, new DateOnly(1946, 1, 4), new DateOnly(1945, 12, 5) },
                    { "uq", 2, 1365, new DateOnly(1946, 2, 3), new DateOnly(1946, 1, 4) },
                    { "uq", 3, 1365, new DateOnly(1946, 3, 4), new DateOnly(1946, 2, 3) },
                    { "uq", 4, 1365, new DateOnly(1946, 4, 3), new DateOnly(1946, 3, 4) },
                    { "uq", 5, 1365, new DateOnly(1946, 5, 2), new DateOnly(1946, 4, 3) },
                    { "uq", 6, 1365, new DateOnly(1946, 5, 31), new DateOnly(1946, 5, 2) },
                    { "uq", 7, 1365, new DateOnly(1946, 6, 30), new DateOnly(1946, 5, 31) },
                    { "uq", 8, 1365, new DateOnly(1946, 7, 29), new DateOnly(1946, 6, 30) },
                    { "uq", 9, 1365, new DateOnly(1946, 8, 27), new DateOnly(1946, 7, 29) },
                    { "uq", 10, 1365, new DateOnly(1946, 9, 26), new DateOnly(1946, 8, 27) },
                    { "uq", 11, 1365, new DateOnly(1946, 10, 25), new DateOnly(1946, 9, 26) },
                    { "uq", 12, 1365, new DateOnly(1946, 11, 24), new DateOnly(1946, 10, 25) },
                    { "uq", 1, 1366, new DateOnly(1946, 12, 24), new DateOnly(1946, 11, 24) },
                    { "uq", 2, 1366, new DateOnly(1947, 1, 23), new DateOnly(1946, 12, 24) },
                    { "uq", 3, 1366, new DateOnly(1947, 2, 22), new DateOnly(1947, 1, 23) },
                    { "uq", 4, 1366, new DateOnly(1947, 3, 23), new DateOnly(1947, 2, 22) },
                    { "uq", 5, 1366, new DateOnly(1947, 4, 22), new DateOnly(1947, 3, 23) },
                    { "uq", 6, 1366, new DateOnly(1947, 5, 21), new DateOnly(1947, 4, 22) },
                    { "uq", 7, 1366, new DateOnly(1947, 6, 19), new DateOnly(1947, 5, 21) },
                    { "uq", 8, 1366, new DateOnly(1947, 7, 19), new DateOnly(1947, 6, 19) },
                    { "uq", 9, 1366, new DateOnly(1947, 8, 17), new DateOnly(1947, 7, 19) },
                    { "uq", 10, 1366, new DateOnly(1947, 9, 15), new DateOnly(1947, 8, 17) },
                    { "uq", 11, 1366, new DateOnly(1947, 10, 15), new DateOnly(1947, 9, 15) },
                    { "uq", 12, 1366, new DateOnly(1947, 11, 13), new DateOnly(1947, 10, 15) },
                    { "uq", 1, 1367, new DateOnly(1947, 12, 13), new DateOnly(1947, 11, 13) },
                    { "uq", 2, 1367, new DateOnly(1948, 1, 12), new DateOnly(1947, 12, 13) },
                    { "uq", 3, 1367, new DateOnly(1948, 2, 11), new DateOnly(1948, 1, 12) },
                    { "uq", 4, 1367, new DateOnly(1948, 3, 11), new DateOnly(1948, 2, 11) },
                    { "uq", 5, 1367, new DateOnly(1948, 4, 10), new DateOnly(1948, 3, 11) },
                    { "uq", 6, 1367, new DateOnly(1948, 5, 10), new DateOnly(1948, 4, 10) },
                    { "uq", 7, 1367, new DateOnly(1948, 6, 8), new DateOnly(1948, 5, 10) },
                    { "uq", 8, 1367, new DateOnly(1948, 7, 7), new DateOnly(1948, 6, 8) },
                    { "uq", 9, 1367, new DateOnly(1948, 8, 6), new DateOnly(1948, 7, 7) },
                    { "uq", 10, 1367, new DateOnly(1948, 9, 4), new DateOnly(1948, 8, 6) },
                    { "uq", 11, 1367, new DateOnly(1948, 10, 3), new DateOnly(1948, 9, 4) },
                    { "uq", 12, 1367, new DateOnly(1948, 11, 2), new DateOnly(1948, 10, 3) },
                    { "uq", 1, 1368, new DateOnly(1948, 12, 1), new DateOnly(1948, 11, 2) },
                    { "uq", 2, 1368, new DateOnly(1948, 12, 31), new DateOnly(1948, 12, 1) },
                    { "uq", 3, 1368, new DateOnly(1949, 1, 30), new DateOnly(1948, 12, 31) },
                    { "uq", 4, 1368, new DateOnly(1949, 2, 28), new DateOnly(1949, 1, 30) },
                    { "uq", 5, 1368, new DateOnly(1949, 3, 30), new DateOnly(1949, 2, 28) },
                    { "uq", 6, 1368, new DateOnly(1949, 4, 29), new DateOnly(1949, 3, 30) },
                    { "uq", 7, 1368, new DateOnly(1949, 5, 28), new DateOnly(1949, 4, 29) },
                    { "uq", 8, 1368, new DateOnly(1949, 6, 27), new DateOnly(1949, 5, 28) },
                    { "uq", 9, 1368, new DateOnly(1949, 7, 26), new DateOnly(1949, 6, 27) },
                    { "uq", 10, 1368, new DateOnly(1949, 8, 25), new DateOnly(1949, 7, 26) },
                    { "uq", 11, 1368, new DateOnly(1949, 9, 23), new DateOnly(1949, 8, 25) },
                    { "uq", 12, 1368, new DateOnly(1949, 10, 22), new DateOnly(1949, 9, 23) },
                    { "uq", 1, 1369, new DateOnly(1949, 11, 21), new DateOnly(1949, 10, 22) },
                    { "uq", 2, 1369, new DateOnly(1949, 12, 20), new DateOnly(1949, 11, 21) },
                    { "uq", 3, 1369, new DateOnly(1950, 1, 19), new DateOnly(1949, 12, 20) },
                    { "uq", 4, 1369, new DateOnly(1950, 2, 17), new DateOnly(1950, 1, 19) },
                    { "uq", 5, 1369, new DateOnly(1950, 3, 19), new DateOnly(1950, 2, 17) },
                    { "uq", 6, 1369, new DateOnly(1950, 4, 18), new DateOnly(1950, 3, 19) },
                    { "uq", 7, 1369, new DateOnly(1950, 5, 18), new DateOnly(1950, 4, 18) },
                    { "uq", 8, 1369, new DateOnly(1950, 6, 16), new DateOnly(1950, 5, 18) },
                    { "uq", 9, 1369, new DateOnly(1950, 7, 16), new DateOnly(1950, 6, 16) },
                    { "uq", 10, 1369, new DateOnly(1950, 8, 14), new DateOnly(1950, 7, 16) },
                    { "uq", 11, 1369, new DateOnly(1950, 9, 13), new DateOnly(1950, 8, 14) },
                    { "uq", 12, 1369, new DateOnly(1950, 10, 12), new DateOnly(1950, 9, 13) },
                    { "uq", 1, 1370, new DateOnly(1950, 11, 10), new DateOnly(1950, 10, 12) },
                    { "uq", 2, 1370, new DateOnly(1950, 12, 10), new DateOnly(1950, 11, 10) },
                    { "uq", 3, 1370, new DateOnly(1951, 1, 8), new DateOnly(1950, 12, 10) },
                    { "uq", 4, 1370, new DateOnly(1951, 2, 7), new DateOnly(1951, 1, 8) },
                    { "uq", 5, 1370, new DateOnly(1951, 3, 8), new DateOnly(1951, 2, 7) },
                    { "uq", 6, 1370, new DateOnly(1951, 4, 7), new DateOnly(1951, 3, 8) },
                    { "uq", 7, 1370, new DateOnly(1951, 5, 7), new DateOnly(1951, 4, 7) },
                    { "uq", 8, 1370, new DateOnly(1951, 6, 5), new DateOnly(1951, 5, 7) },
                    { "uq", 9, 1370, new DateOnly(1951, 7, 5), new DateOnly(1951, 6, 5) },
                    { "uq", 10, 1370, new DateOnly(1951, 8, 3), new DateOnly(1951, 7, 5) },
                    { "uq", 11, 1370, new DateOnly(1951, 9, 2), new DateOnly(1951, 8, 3) },
                    { "uq", 12, 1370, new DateOnly(1951, 10, 2), new DateOnly(1951, 9, 2) },
                    { "uq", 1, 1371, new DateOnly(1951, 10, 31), new DateOnly(1951, 10, 2) },
                    { "uq", 2, 1371, new DateOnly(1951, 11, 30), new DateOnly(1951, 10, 31) },
                    { "uq", 3, 1371, new DateOnly(1951, 12, 29), new DateOnly(1951, 11, 30) },
                    { "uq", 4, 1371, new DateOnly(1952, 1, 27), new DateOnly(1951, 12, 29) },
                    { "uq", 5, 1371, new DateOnly(1952, 2, 26), new DateOnly(1952, 1, 27) },
                    { "uq", 6, 1371, new DateOnly(1952, 3, 26), new DateOnly(1952, 2, 26) },
                    { "uq", 7, 1371, new DateOnly(1952, 4, 25), new DateOnly(1952, 3, 26) },
                    { "uq", 8, 1371, new DateOnly(1952, 5, 24), new DateOnly(1952, 4, 25) },
                    { "uq", 9, 1371, new DateOnly(1952, 6, 23), new DateOnly(1952, 5, 24) },
                    { "uq", 10, 1371, new DateOnly(1952, 7, 22), new DateOnly(1952, 6, 23) },
                    { "uq", 11, 1371, new DateOnly(1952, 8, 21), new DateOnly(1952, 7, 22) },
                    { "uq", 12, 1371, new DateOnly(1952, 9, 20), new DateOnly(1952, 8, 21) },
                    { "uq", 1, 1372, new DateOnly(1952, 10, 19), new DateOnly(1952, 9, 20) },
                    { "uq", 2, 1372, new DateOnly(1952, 11, 18), new DateOnly(1952, 10, 19) },
                    { "uq", 3, 1372, new DateOnly(1952, 12, 18), new DateOnly(1952, 11, 18) },
                    { "uq", 4, 1372, new DateOnly(1953, 1, 16), new DateOnly(1952, 12, 18) },
                    { "uq", 5, 1372, new DateOnly(1953, 2, 15), new DateOnly(1953, 1, 16) },
                    { "uq", 6, 1372, new DateOnly(1953, 3, 16), new DateOnly(1953, 2, 15) },
                    { "uq", 7, 1372, new DateOnly(1953, 4, 14), new DateOnly(1953, 3, 16) },
                    { "uq", 8, 1372, new DateOnly(1953, 5, 14), new DateOnly(1953, 4, 14) },
                    { "uq", 9, 1372, new DateOnly(1953, 6, 12), new DateOnly(1953, 5, 14) },
                    { "uq", 10, 1372, new DateOnly(1953, 7, 12), new DateOnly(1953, 6, 12) },
                    { "uq", 11, 1372, new DateOnly(1953, 8, 10), new DateOnly(1953, 7, 12) },
                    { "uq", 12, 1372, new DateOnly(1953, 9, 9), new DateOnly(1953, 8, 10) },
                    { "uq", 1, 1373, new DateOnly(1953, 10, 9), new DateOnly(1953, 9, 9) },
                    { "uq", 2, 1373, new DateOnly(1953, 11, 7), new DateOnly(1953, 10, 9) },
                    { "uq", 3, 1373, new DateOnly(1953, 12, 7), new DateOnly(1953, 11, 7) },
                    { "uq", 4, 1373, new DateOnly(1954, 1, 6), new DateOnly(1953, 12, 7) },
                    { "uq", 5, 1373, new DateOnly(1954, 2, 4), new DateOnly(1954, 1, 6) },
                    { "uq", 6, 1373, new DateOnly(1954, 3, 6), new DateOnly(1954, 2, 4) },
                    { "uq", 7, 1373, new DateOnly(1954, 4, 4), new DateOnly(1954, 3, 6) },
                    { "uq", 8, 1373, new DateOnly(1954, 5, 3), new DateOnly(1954, 4, 4) },
                    { "uq", 9, 1373, new DateOnly(1954, 6, 2), new DateOnly(1954, 5, 3) },
                    { "uq", 10, 1373, new DateOnly(1954, 7, 1), new DateOnly(1954, 6, 2) },
                    { "uq", 11, 1373, new DateOnly(1954, 7, 30), new DateOnly(1954, 7, 1) },
                    { "uq", 12, 1373, new DateOnly(1954, 8, 29), new DateOnly(1954, 7, 30) },
                    { "uq", 1, 1374, new DateOnly(1954, 9, 28), new DateOnly(1954, 8, 29) },
                    { "uq", 2, 1374, new DateOnly(1954, 10, 27), new DateOnly(1954, 9, 28) },
                    { "uq", 3, 1374, new DateOnly(1954, 11, 26), new DateOnly(1954, 10, 27) },
                    { "uq", 4, 1374, new DateOnly(1954, 12, 26), new DateOnly(1954, 11, 26) },
                    { "uq", 5, 1374, new DateOnly(1955, 1, 25), new DateOnly(1954, 12, 26) },
                    { "uq", 6, 1374, new DateOnly(1955, 2, 23), new DateOnly(1955, 1, 25) },
                    { "uq", 7, 1374, new DateOnly(1955, 3, 25), new DateOnly(1955, 2, 23) },
                    { "uq", 8, 1374, new DateOnly(1955, 4, 23), new DateOnly(1955, 3, 25) },
                    { "uq", 9, 1374, new DateOnly(1955, 5, 22), new DateOnly(1955, 4, 23) },
                    { "uq", 10, 1374, new DateOnly(1955, 6, 21), new DateOnly(1955, 5, 22) },
                    { "uq", 11, 1374, new DateOnly(1955, 7, 20), new DateOnly(1955, 6, 21) },
                    { "uq", 12, 1374, new DateOnly(1955, 8, 18), new DateOnly(1955, 7, 20) },
                    { "uq", 1, 1375, new DateOnly(1955, 9, 17), new DateOnly(1955, 8, 18) },
                    { "uq", 2, 1375, new DateOnly(1955, 10, 16), new DateOnly(1955, 9, 17) },
                    { "uq", 3, 1375, new DateOnly(1955, 11, 15), new DateOnly(1955, 10, 16) },
                    { "uq", 4, 1375, new DateOnly(1955, 12, 15), new DateOnly(1955, 11, 15) },
                    { "uq", 5, 1375, new DateOnly(1956, 1, 14), new DateOnly(1955, 12, 15) },
                    { "uq", 6, 1375, new DateOnly(1956, 2, 12), new DateOnly(1956, 1, 14) },
                    { "uq", 7, 1375, new DateOnly(1956, 3, 13), new DateOnly(1956, 2, 12) },
                    { "uq", 8, 1375, new DateOnly(1956, 4, 12), new DateOnly(1956, 3, 13) },
                    { "uq", 9, 1375, new DateOnly(1956, 5, 11), new DateOnly(1956, 4, 12) },
                    { "uq", 10, 1375, new DateOnly(1956, 6, 9), new DateOnly(1956, 5, 11) },
                    { "uq", 11, 1375, new DateOnly(1956, 7, 9), new DateOnly(1956, 6, 9) },
                    { "uq", 12, 1375, new DateOnly(1956, 8, 7), new DateOnly(1956, 7, 9) },
                    { "uq", 1, 1376, new DateOnly(1956, 9, 5), new DateOnly(1956, 8, 7) },
                    { "uq", 2, 1376, new DateOnly(1956, 10, 5), new DateOnly(1956, 9, 5) },
                    { "uq", 3, 1376, new DateOnly(1956, 11, 3), new DateOnly(1956, 10, 5) },
                    { "uq", 4, 1376, new DateOnly(1956, 12, 3), new DateOnly(1956, 11, 3) },
                    { "uq", 5, 1376, new DateOnly(1957, 1, 2), new DateOnly(1956, 12, 3) },
                    { "uq", 6, 1376, new DateOnly(1957, 1, 31), new DateOnly(1957, 1, 2) },
                    { "uq", 7, 1376, new DateOnly(1957, 3, 2), new DateOnly(1957, 1, 31) },
                    { "uq", 8, 1376, new DateOnly(1957, 4, 1), new DateOnly(1957, 3, 2) },
                    { "uq", 9, 1376, new DateOnly(1957, 4, 30), new DateOnly(1957, 4, 1) },
                    { "uq", 10, 1376, new DateOnly(1957, 5, 30), new DateOnly(1957, 4, 30) },
                    { "uq", 11, 1376, new DateOnly(1957, 6, 28), new DateOnly(1957, 5, 30) },
                    { "uq", 12, 1376, new DateOnly(1957, 7, 28), new DateOnly(1957, 6, 28) },
                    { "uq", 1, 1377, new DateOnly(1957, 8, 26), new DateOnly(1957, 7, 28) },
                    { "uq", 2, 1377, new DateOnly(1957, 9, 24), new DateOnly(1957, 8, 26) },
                    { "uq", 3, 1377, new DateOnly(1957, 10, 24), new DateOnly(1957, 9, 24) },
                    { "uq", 4, 1377, new DateOnly(1957, 11, 22), new DateOnly(1957, 10, 24) },
                    { "uq", 5, 1377, new DateOnly(1957, 12, 22), new DateOnly(1957, 11, 22) },
                    { "uq", 6, 1377, new DateOnly(1958, 1, 20), new DateOnly(1957, 12, 22) },
                    { "uq", 7, 1377, new DateOnly(1958, 2, 19), new DateOnly(1958, 1, 20) },
                    { "uq", 8, 1377, new DateOnly(1958, 3, 21), new DateOnly(1958, 2, 19) },
                    { "uq", 9, 1377, new DateOnly(1958, 4, 20), new DateOnly(1958, 3, 21) },
                    { "uq", 10, 1377, new DateOnly(1958, 5, 19), new DateOnly(1958, 4, 20) },
                    { "uq", 11, 1377, new DateOnly(1958, 6, 18), new DateOnly(1958, 5, 19) },
                    { "uq", 12, 1377, new DateOnly(1958, 7, 17), new DateOnly(1958, 6, 18) },
                    { "uq", 1, 1378, new DateOnly(1958, 8, 16), new DateOnly(1958, 7, 17) },
                    { "uq", 2, 1378, new DateOnly(1958, 9, 14), new DateOnly(1958, 8, 16) },
                    { "uq", 3, 1378, new DateOnly(1958, 10, 13), new DateOnly(1958, 9, 14) },
                    { "uq", 4, 1378, new DateOnly(1958, 11, 12), new DateOnly(1958, 10, 13) },
                    { "uq", 5, 1378, new DateOnly(1958, 12, 11), new DateOnly(1958, 11, 12) },
                    { "uq", 6, 1378, new DateOnly(1959, 1, 10), new DateOnly(1958, 12, 11) },
                    { "uq", 7, 1378, new DateOnly(1959, 2, 8), new DateOnly(1959, 1, 10) },
                    { "uq", 8, 1378, new DateOnly(1959, 3, 10), new DateOnly(1959, 2, 8) },
                    { "uq", 9, 1378, new DateOnly(1959, 4, 9), new DateOnly(1959, 3, 10) },
                    { "uq", 10, 1378, new DateOnly(1959, 5, 8), new DateOnly(1959, 4, 9) },
                    { "uq", 11, 1378, new DateOnly(1959, 6, 7), new DateOnly(1959, 5, 8) },
                    { "uq", 12, 1378, new DateOnly(1959, 7, 7), new DateOnly(1959, 6, 7) },
                    { "uq", 1, 1379, new DateOnly(1959, 8, 5), new DateOnly(1959, 7, 7) },
                    { "uq", 2, 1379, new DateOnly(1959, 9, 4), new DateOnly(1959, 8, 5) },
                    { "uq", 3, 1379, new DateOnly(1959, 10, 3), new DateOnly(1959, 9, 4) },
                    { "uq", 4, 1379, new DateOnly(1959, 11, 1), new DateOnly(1959, 10, 3) },
                    { "uq", 5, 1379, new DateOnly(1959, 12, 1), new DateOnly(1959, 11, 1) },
                    { "uq", 6, 1379, new DateOnly(1959, 12, 30), new DateOnly(1959, 12, 1) },
                    { "uq", 7, 1379, new DateOnly(1960, 1, 29), new DateOnly(1959, 12, 30) },
                    { "uq", 8, 1379, new DateOnly(1960, 2, 27), new DateOnly(1960, 1, 29) },
                    { "uq", 9, 1379, new DateOnly(1960, 3, 28), new DateOnly(1960, 2, 27) },
                    { "uq", 10, 1379, new DateOnly(1960, 4, 26), new DateOnly(1960, 3, 28) },
                    { "uq", 11, 1379, new DateOnly(1960, 5, 26), new DateOnly(1960, 4, 26) },
                    { "uq", 12, 1379, new DateOnly(1960, 6, 25), new DateOnly(1960, 5, 26) },
                    { "uq", 1, 1380, new DateOnly(1960, 7, 24), new DateOnly(1960, 6, 25) },
                    { "uq", 2, 1380, new DateOnly(1960, 8, 23), new DateOnly(1960, 7, 24) },
                    { "uq", 3, 1380, new DateOnly(1960, 9, 21), new DateOnly(1960, 8, 23) },
                    { "uq", 4, 1380, new DateOnly(1960, 10, 21), new DateOnly(1960, 9, 21) },
                    { "uq", 5, 1380, new DateOnly(1960, 11, 19), new DateOnly(1960, 10, 21) },
                    { "uq", 6, 1380, new DateOnly(1960, 12, 19), new DateOnly(1960, 11, 19) },
                    { "uq", 7, 1380, new DateOnly(1961, 1, 17), new DateOnly(1960, 12, 19) },
                    { "uq", 8, 1380, new DateOnly(1961, 2, 16), new DateOnly(1961, 1, 17) },
                    { "uq", 9, 1380, new DateOnly(1961, 3, 17), new DateOnly(1961, 2, 16) },
                    { "uq", 10, 1380, new DateOnly(1961, 4, 16), new DateOnly(1961, 3, 17) },
                    { "uq", 11, 1380, new DateOnly(1961, 5, 15), new DateOnly(1961, 4, 16) },
                    { "uq", 12, 1380, new DateOnly(1961, 6, 14), new DateOnly(1961, 5, 15) },
                    { "uq", 1, 1381, new DateOnly(1961, 7, 13), new DateOnly(1961, 6, 14) },
                    { "uq", 2, 1381, new DateOnly(1961, 8, 12), new DateOnly(1961, 7, 13) },
                    { "uq", 3, 1381, new DateOnly(1961, 9, 11), new DateOnly(1961, 8, 12) },
                    { "uq", 4, 1381, new DateOnly(1961, 10, 10), new DateOnly(1961, 9, 11) },
                    { "uq", 5, 1381, new DateOnly(1961, 11, 9), new DateOnly(1961, 10, 10) },
                    { "uq", 6, 1381, new DateOnly(1961, 12, 8), new DateOnly(1961, 11, 9) },
                    { "uq", 7, 1381, new DateOnly(1962, 1, 7), new DateOnly(1961, 12, 8) },
                    { "uq", 8, 1381, new DateOnly(1962, 2, 6), new DateOnly(1962, 1, 7) },
                    { "uq", 9, 1381, new DateOnly(1962, 3, 7), new DateOnly(1962, 2, 6) },
                    { "uq", 10, 1381, new DateOnly(1962, 4, 5), new DateOnly(1962, 3, 7) },
                    { "uq", 11, 1381, new DateOnly(1962, 5, 5), new DateOnly(1962, 4, 5) },
                    { "uq", 12, 1381, new DateOnly(1962, 6, 3), new DateOnly(1962, 5, 5) },
                    { "uq", 1, 1382, new DateOnly(1962, 7, 2), new DateOnly(1962, 6, 3) },
                    { "uq", 2, 1382, new DateOnly(1962, 8, 1), new DateOnly(1962, 7, 2) },
                    { "uq", 3, 1382, new DateOnly(1962, 8, 31), new DateOnly(1962, 8, 1) },
                    { "uq", 4, 1382, new DateOnly(1962, 9, 29), new DateOnly(1962, 8, 31) },
                    { "uq", 5, 1382, new DateOnly(1962, 10, 29), new DateOnly(1962, 9, 29) },
                    { "uq", 6, 1382, new DateOnly(1962, 11, 28), new DateOnly(1962, 10, 29) },
                    { "uq", 7, 1382, new DateOnly(1962, 12, 27), new DateOnly(1962, 11, 28) },
                    { "uq", 8, 1382, new DateOnly(1963, 1, 26), new DateOnly(1962, 12, 27) },
                    { "uq", 9, 1382, new DateOnly(1963, 2, 25), new DateOnly(1963, 1, 26) },
                    { "uq", 10, 1382, new DateOnly(1963, 3, 26), new DateOnly(1963, 2, 25) },
                    { "uq", 11, 1382, new DateOnly(1963, 4, 24), new DateOnly(1963, 3, 26) },
                    { "uq", 12, 1382, new DateOnly(1963, 5, 24), new DateOnly(1963, 4, 24) },
                    { "uq", 1, 1383, new DateOnly(1963, 6, 22), new DateOnly(1963, 5, 24) },
                    { "uq", 2, 1383, new DateOnly(1963, 7, 21), new DateOnly(1963, 6, 22) },
                    { "uq", 3, 1383, new DateOnly(1963, 8, 20), new DateOnly(1963, 7, 21) },
                    { "uq", 4, 1383, new DateOnly(1963, 9, 18), new DateOnly(1963, 8, 20) },
                    { "uq", 5, 1383, new DateOnly(1963, 10, 18), new DateOnly(1963, 9, 18) },
                    { "uq", 6, 1383, new DateOnly(1963, 11, 17), new DateOnly(1963, 10, 18) },
                    { "uq", 7, 1383, new DateOnly(1963, 12, 17), new DateOnly(1963, 11, 17) },
                    { "uq", 8, 1383, new DateOnly(1964, 1, 15), new DateOnly(1963, 12, 17) },
                    { "uq", 9, 1383, new DateOnly(1964, 2, 14), new DateOnly(1964, 1, 15) },
                    { "uq", 10, 1383, new DateOnly(1964, 3, 15), new DateOnly(1964, 2, 14) },
                    { "uq", 11, 1383, new DateOnly(1964, 4, 13), new DateOnly(1964, 3, 15) },
                    { "uq", 12, 1383, new DateOnly(1964, 5, 12), new DateOnly(1964, 4, 13) },
                    { "uq", 1, 1384, new DateOnly(1964, 6, 11), new DateOnly(1964, 5, 12) },
                    { "uq", 2, 1384, new DateOnly(1964, 7, 10), new DateOnly(1964, 6, 11) },
                    { "uq", 3, 1384, new DateOnly(1964, 8, 8), new DateOnly(1964, 7, 10) },
                    { "uq", 4, 1384, new DateOnly(1964, 9, 7), new DateOnly(1964, 8, 8) },
                    { "uq", 5, 1384, new DateOnly(1964, 10, 6), new DateOnly(1964, 9, 7) },
                    { "uq", 6, 1384, new DateOnly(1964, 11, 5), new DateOnly(1964, 10, 6) },
                    { "uq", 7, 1384, new DateOnly(1964, 12, 5), new DateOnly(1964, 11, 5) },
                    { "uq", 8, 1384, new DateOnly(1965, 1, 3), new DateOnly(1964, 12, 5) },
                    { "uq", 9, 1384, new DateOnly(1965, 2, 2), new DateOnly(1965, 1, 3) },
                    { "uq", 10, 1384, new DateOnly(1965, 3, 4), new DateOnly(1965, 2, 2) },
                    { "uq", 11, 1384, new DateOnly(1965, 4, 3), new DateOnly(1965, 3, 4) },
                    { "uq", 12, 1384, new DateOnly(1965, 5, 2), new DateOnly(1965, 4, 3) },
                    { "uq", 1, 1385, new DateOnly(1965, 5, 31), new DateOnly(1965, 5, 2) },
                    { "uq", 2, 1385, new DateOnly(1965, 6, 30), new DateOnly(1965, 5, 31) },
                    { "uq", 3, 1385, new DateOnly(1965, 7, 29), new DateOnly(1965, 6, 30) },
                    { "uq", 4, 1385, new DateOnly(1965, 8, 27), new DateOnly(1965, 7, 29) },
                    { "uq", 5, 1385, new DateOnly(1965, 9, 26), new DateOnly(1965, 8, 27) },
                    { "uq", 6, 1385, new DateOnly(1965, 10, 25), new DateOnly(1965, 9, 26) },
                    { "uq", 7, 1385, new DateOnly(1965, 11, 24), new DateOnly(1965, 10, 25) },
                    { "uq", 8, 1385, new DateOnly(1965, 12, 23), new DateOnly(1965, 11, 24) },
                    { "uq", 9, 1385, new DateOnly(1966, 1, 22), new DateOnly(1965, 12, 23) },
                    { "uq", 10, 1385, new DateOnly(1966, 2, 21), new DateOnly(1966, 1, 22) },
                    { "uq", 11, 1385, new DateOnly(1966, 3, 23), new DateOnly(1966, 2, 21) },
                    { "uq", 12, 1385, new DateOnly(1966, 4, 21), new DateOnly(1966, 3, 23) },
                    { "uq", 1, 1386, new DateOnly(1966, 5, 21), new DateOnly(1966, 4, 21) },
                    { "uq", 2, 1386, new DateOnly(1966, 6, 19), new DateOnly(1966, 5, 21) },
                    { "uq", 3, 1386, new DateOnly(1966, 7, 19), new DateOnly(1966, 6, 19) },
                    { "uq", 4, 1386, new DateOnly(1966, 8, 17), new DateOnly(1966, 7, 19) },
                    { "uq", 5, 1386, new DateOnly(1966, 9, 15), new DateOnly(1966, 8, 17) },
                    { "uq", 6, 1386, new DateOnly(1966, 10, 15), new DateOnly(1966, 9, 15) },
                    { "uq", 7, 1386, new DateOnly(1966, 11, 13), new DateOnly(1966, 10, 15) },
                    { "uq", 8, 1386, new DateOnly(1966, 12, 13), new DateOnly(1966, 11, 13) },
                    { "uq", 9, 1386, new DateOnly(1967, 1, 11), new DateOnly(1966, 12, 13) },
                    { "uq", 10, 1386, new DateOnly(1967, 2, 10), new DateOnly(1967, 1, 11) },
                    { "uq", 11, 1386, new DateOnly(1967, 3, 12), new DateOnly(1967, 2, 10) },
                    { "uq", 12, 1386, new DateOnly(1967, 4, 10), new DateOnly(1967, 3, 12) },
                    { "uq", 1, 1387, new DateOnly(1967, 5, 10), new DateOnly(1967, 4, 10) },
                    { "uq", 2, 1387, new DateOnly(1967, 6, 9), new DateOnly(1967, 5, 10) },
                    { "uq", 3, 1387, new DateOnly(1967, 7, 8), new DateOnly(1967, 6, 9) },
                    { "uq", 4, 1387, new DateOnly(1967, 8, 7), new DateOnly(1967, 7, 8) },
                    { "uq", 5, 1387, new DateOnly(1967, 9, 5), new DateOnly(1967, 8, 7) },
                    { "uq", 6, 1387, new DateOnly(1967, 10, 4), new DateOnly(1967, 9, 5) },
                    { "uq", 7, 1387, new DateOnly(1967, 11, 3), new DateOnly(1967, 10, 4) },
                    { "uq", 8, 1387, new DateOnly(1967, 12, 2), new DateOnly(1967, 11, 3) },
                    { "uq", 9, 1387, new DateOnly(1968, 1, 1), new DateOnly(1967, 12, 2) },
                    { "uq", 10, 1387, new DateOnly(1968, 1, 30), new DateOnly(1968, 1, 1) },
                    { "uq", 11, 1387, new DateOnly(1968, 2, 29), new DateOnly(1968, 1, 30) },
                    { "uq", 12, 1387, new DateOnly(1968, 3, 29), new DateOnly(1968, 2, 29) },
                    { "uq", 1, 1388, new DateOnly(1968, 4, 28), new DateOnly(1968, 3, 29) },
                    { "uq", 2, 1388, new DateOnly(1968, 5, 28), new DateOnly(1968, 4, 28) },
                    { "uq", 3, 1388, new DateOnly(1968, 6, 26), new DateOnly(1968, 5, 28) },
                    { "uq", 4, 1388, new DateOnly(1968, 7, 26), new DateOnly(1968, 6, 26) },
                    { "uq", 5, 1388, new DateOnly(1968, 8, 24), new DateOnly(1968, 7, 26) },
                    { "uq", 6, 1388, new DateOnly(1968, 9, 23), new DateOnly(1968, 8, 24) },
                    { "uq", 7, 1388, new DateOnly(1968, 10, 22), new DateOnly(1968, 9, 23) },
                    { "uq", 8, 1388, new DateOnly(1968, 11, 21), new DateOnly(1968, 10, 22) },
                    { "uq", 9, 1388, new DateOnly(1968, 12, 20), new DateOnly(1968, 11, 21) },
                    { "uq", 10, 1388, new DateOnly(1969, 1, 19), new DateOnly(1968, 12, 20) },
                    { "uq", 11, 1388, new DateOnly(1969, 2, 17), new DateOnly(1969, 1, 19) },
                    { "uq", 12, 1388, new DateOnly(1969, 3, 19), new DateOnly(1969, 2, 17) },
                    { "uq", 1, 1389, new DateOnly(1969, 4, 17), new DateOnly(1969, 3, 19) },
                    { "uq", 2, 1389, new DateOnly(1969, 5, 17), new DateOnly(1969, 4, 17) },
                    { "uq", 3, 1389, new DateOnly(1969, 6, 15), new DateOnly(1969, 5, 17) },
                    { "uq", 4, 1389, new DateOnly(1969, 7, 15), new DateOnly(1969, 6, 15) },
                    { "uq", 5, 1389, new DateOnly(1969, 8, 14), new DateOnly(1969, 7, 15) },
                    { "uq", 6, 1389, new DateOnly(1969, 9, 12), new DateOnly(1969, 8, 14) },
                    { "uq", 7, 1389, new DateOnly(1969, 10, 12), new DateOnly(1969, 9, 12) },
                    { "uq", 8, 1389, new DateOnly(1969, 11, 10), new DateOnly(1969, 10, 12) },
                    { "uq", 9, 1389, new DateOnly(1969, 12, 10), new DateOnly(1969, 11, 10) },
                    { "uq", 10, 1389, new DateOnly(1970, 1, 8), new DateOnly(1969, 12, 10) },
                    { "uq", 11, 1389, new DateOnly(1970, 2, 7), new DateOnly(1970, 1, 8) },
                    { "uq", 12, 1389, new DateOnly(1970, 3, 8), new DateOnly(1970, 2, 7) },
                    { "uq", 1, 1390, new DateOnly(1970, 4, 7), new DateOnly(1970, 3, 8) },
                    { "uq", 2, 1390, new DateOnly(1970, 5, 6), new DateOnly(1970, 4, 7) },
                    { "uq", 3, 1390, new DateOnly(1970, 6, 5), new DateOnly(1970, 5, 6) },
                    { "uq", 4, 1390, new DateOnly(1970, 7, 4), new DateOnly(1970, 6, 5) },
                    { "uq", 5, 1390, new DateOnly(1970, 8, 3), new DateOnly(1970, 7, 4) },
                    { "uq", 6, 1390, new DateOnly(1970, 9, 1), new DateOnly(1970, 8, 3) },
                    { "uq", 7, 1390, new DateOnly(1970, 10, 1), new DateOnly(1970, 9, 1) },
                    { "uq", 8, 1390, new DateOnly(1970, 10, 31), new DateOnly(1970, 10, 1) },
                    { "uq", 9, 1390, new DateOnly(1970, 11, 29), new DateOnly(1970, 10, 31) },
                    { "uq", 10, 1390, new DateOnly(1970, 12, 29), new DateOnly(1970, 11, 29) },
                    { "uq", 11, 1390, new DateOnly(1971, 1, 27), new DateOnly(1970, 12, 29) },
                    { "uq", 12, 1390, new DateOnly(1971, 2, 26), new DateOnly(1971, 1, 27) },
                    { "uq", 1, 1391, new DateOnly(1971, 3, 27), new DateOnly(1971, 2, 26) },
                    { "uq", 2, 1391, new DateOnly(1971, 4, 26), new DateOnly(1971, 3, 27) },
                    { "uq", 3, 1391, new DateOnly(1971, 5, 25), new DateOnly(1971, 4, 26) },
                    { "uq", 4, 1391, new DateOnly(1971, 6, 23), new DateOnly(1971, 5, 25) },
                    { "uq", 5, 1391, new DateOnly(1971, 7, 23), new DateOnly(1971, 6, 23) },
                    { "uq", 6, 1391, new DateOnly(1971, 8, 21), new DateOnly(1971, 7, 23) },
                    { "uq", 7, 1391, new DateOnly(1971, 9, 20), new DateOnly(1971, 8, 21) },
                    { "uq", 8, 1391, new DateOnly(1971, 10, 20), new DateOnly(1971, 9, 20) },
                    { "uq", 9, 1391, new DateOnly(1971, 11, 19), new DateOnly(1971, 10, 20) },
                    { "uq", 10, 1391, new DateOnly(1971, 12, 18), new DateOnly(1971, 11, 19) },
                    { "uq", 11, 1391, new DateOnly(1972, 1, 17), new DateOnly(1971, 12, 18) },
                    { "uq", 12, 1391, new DateOnly(1972, 2, 16), new DateOnly(1972, 1, 17) },
                    { "uq", 1, 1392, new DateOnly(1972, 3, 16), new DateOnly(1972, 2, 16) },
                    { "uq", 2, 1392, new DateOnly(1972, 4, 14), new DateOnly(1972, 3, 16) },
                    { "uq", 3, 1392, new DateOnly(1972, 5, 14), new DateOnly(1972, 4, 14) },
                    { "uq", 4, 1392, new DateOnly(1972, 6, 12), new DateOnly(1972, 5, 14) },
                    { "uq", 5, 1392, new DateOnly(1972, 7, 11), new DateOnly(1972, 6, 12) },
                    { "uq", 6, 1392, new DateOnly(1972, 8, 10), new DateOnly(1972, 7, 11) },
                    { "uq", 7, 1392, new DateOnly(1972, 9, 8), new DateOnly(1972, 8, 10) },
                    { "uq", 8, 1392, new DateOnly(1972, 10, 8), new DateOnly(1972, 9, 8) },
                    { "uq", 9, 1392, new DateOnly(1972, 11, 7), new DateOnly(1972, 10, 8) },
                    { "uq", 10, 1392, new DateOnly(1972, 12, 6), new DateOnly(1972, 11, 7) },
                    { "uq", 11, 1392, new DateOnly(1973, 1, 5), new DateOnly(1972, 12, 6) },
                    { "uq", 12, 1392, new DateOnly(1973, 2, 4), new DateOnly(1973, 1, 5) },
                    { "uq", 1, 1393, new DateOnly(1973, 3, 6), new DateOnly(1973, 2, 4) },
                    { "uq", 2, 1393, new DateOnly(1973, 4, 4), new DateOnly(1973, 3, 6) },
                    { "uq", 3, 1393, new DateOnly(1973, 5, 3), new DateOnly(1973, 4, 4) },
                    { "uq", 4, 1393, new DateOnly(1973, 6, 2), new DateOnly(1973, 5, 3) },
                    { "uq", 5, 1393, new DateOnly(1973, 7, 1), new DateOnly(1973, 6, 2) },
                    { "uq", 6, 1393, new DateOnly(1973, 7, 30), new DateOnly(1973, 7, 1) },
                    { "uq", 7, 1393, new DateOnly(1973, 8, 29), new DateOnly(1973, 7, 30) },
                    { "uq", 8, 1393, new DateOnly(1973, 9, 27), new DateOnly(1973, 8, 29) },
                    { "uq", 9, 1393, new DateOnly(1973, 10, 27), new DateOnly(1973, 9, 27) },
                    { "uq", 10, 1393, new DateOnly(1973, 11, 25), new DateOnly(1973, 10, 27) },
                    { "uq", 11, 1393, new DateOnly(1973, 12, 25), new DateOnly(1973, 11, 25) },
                    { "uq", 12, 1393, new DateOnly(1974, 1, 24), new DateOnly(1973, 12, 25) },
                    { "uq", 1, 1394, new DateOnly(1974, 2, 23), new DateOnly(1974, 1, 24) },
                    { "uq", 2, 1394, new DateOnly(1974, 3, 24), new DateOnly(1974, 2, 23) },
                    { "uq", 3, 1394, new DateOnly(1974, 4, 23), new DateOnly(1974, 3, 24) },
                    { "uq", 4, 1394, new DateOnly(1974, 5, 22), new DateOnly(1974, 4, 23) },
                    { "uq", 5, 1394, new DateOnly(1974, 6, 21), new DateOnly(1974, 5, 22) },
                    { "uq", 6, 1394, new DateOnly(1974, 7, 20), new DateOnly(1974, 6, 21) },
                    { "uq", 7, 1394, new DateOnly(1974, 8, 18), new DateOnly(1974, 7, 20) },
                    { "uq", 8, 1394, new DateOnly(1974, 9, 17), new DateOnly(1974, 8, 18) },
                    { "uq", 9, 1394, new DateOnly(1974, 10, 16), new DateOnly(1974, 9, 17) },
                    { "uq", 10, 1394, new DateOnly(1974, 11, 15), new DateOnly(1974, 10, 16) },
                    { "uq", 11, 1394, new DateOnly(1974, 12, 14), new DateOnly(1974, 11, 15) },
                    { "uq", 12, 1394, new DateOnly(1975, 1, 13), new DateOnly(1974, 12, 14) },
                    { "uq", 1, 1395, new DateOnly(1975, 2, 12), new DateOnly(1975, 1, 13) },
                    { "uq", 2, 1395, new DateOnly(1975, 3, 13), new DateOnly(1975, 2, 12) },
                    { "uq", 3, 1395, new DateOnly(1975, 4, 12), new DateOnly(1975, 3, 13) },
                    { "uq", 4, 1395, new DateOnly(1975, 5, 12), new DateOnly(1975, 4, 12) },
                    { "uq", 5, 1395, new DateOnly(1975, 6, 10), new DateOnly(1975, 5, 12) },
                    { "uq", 6, 1395, new DateOnly(1975, 7, 10), new DateOnly(1975, 6, 10) },
                    { "uq", 7, 1395, new DateOnly(1975, 8, 8), new DateOnly(1975, 7, 10) },
                    { "uq", 8, 1395, new DateOnly(1975, 9, 6), new DateOnly(1975, 8, 8) },
                    { "uq", 9, 1395, new DateOnly(1975, 10, 6), new DateOnly(1975, 9, 6) },
                    { "uq", 10, 1395, new DateOnly(1975, 11, 4), new DateOnly(1975, 10, 6) },
                    { "uq", 11, 1395, new DateOnly(1975, 12, 4), new DateOnly(1975, 11, 4) },
                    { "uq", 12, 1395, new DateOnly(1976, 1, 2), new DateOnly(1975, 12, 4) },
                    { "uq", 1, 1396, new DateOnly(1976, 2, 1), new DateOnly(1976, 1, 2) },
                    { "uq", 2, 1396, new DateOnly(1976, 3, 1), new DateOnly(1976, 2, 1) },
                    { "uq", 3, 1396, new DateOnly(1976, 3, 31), new DateOnly(1976, 3, 1) },
                    { "uq", 4, 1396, new DateOnly(1976, 4, 30), new DateOnly(1976, 3, 31) },
                    { "uq", 5, 1396, new DateOnly(1976, 5, 30), new DateOnly(1976, 4, 30) },
                    { "uq", 6, 1396, new DateOnly(1976, 6, 28), new DateOnly(1976, 5, 30) },
                    { "uq", 7, 1396, new DateOnly(1976, 7, 28), new DateOnly(1976, 6, 28) },
                    { "uq", 8, 1396, new DateOnly(1976, 8, 26), new DateOnly(1976, 7, 28) },
                    { "uq", 9, 1396, new DateOnly(1976, 9, 24), new DateOnly(1976, 8, 26) },
                    { "uq", 10, 1396, new DateOnly(1976, 10, 24), new DateOnly(1976, 9, 24) },
                    { "uq", 11, 1396, new DateOnly(1976, 11, 22), new DateOnly(1976, 10, 24) },
                    { "uq", 12, 1396, new DateOnly(1976, 12, 22), new DateOnly(1976, 11, 22) },
                    { "uq", 1, 1397, new DateOnly(1977, 1, 20), new DateOnly(1976, 12, 22) },
                    { "uq", 2, 1397, new DateOnly(1977, 2, 19), new DateOnly(1977, 1, 20) },
                    { "uq", 3, 1397, new DateOnly(1977, 3, 20), new DateOnly(1977, 2, 19) },
                    { "uq", 4, 1397, new DateOnly(1977, 4, 19), new DateOnly(1977, 3, 20) },
                    { "uq", 5, 1397, new DateOnly(1977, 5, 19), new DateOnly(1977, 4, 19) },
                    { "uq", 6, 1397, new DateOnly(1977, 6, 17), new DateOnly(1977, 5, 19) },
                    { "uq", 7, 1397, new DateOnly(1977, 7, 17), new DateOnly(1977, 6, 17) },
                    { "uq", 8, 1397, new DateOnly(1977, 8, 15), new DateOnly(1977, 7, 17) },
                    { "uq", 9, 1397, new DateOnly(1977, 9, 14), new DateOnly(1977, 8, 15) },
                    { "uq", 10, 1397, new DateOnly(1977, 10, 13), new DateOnly(1977, 9, 14) },
                    { "uq", 11, 1397, new DateOnly(1977, 11, 12), new DateOnly(1977, 10, 13) },
                    { "uq", 12, 1397, new DateOnly(1977, 12, 11), new DateOnly(1977, 11, 12) },
                    { "uq", 1, 1398, new DateOnly(1978, 1, 10), new DateOnly(1977, 12, 11) },
                    { "uq", 2, 1398, new DateOnly(1978, 2, 8), new DateOnly(1978, 1, 10) },
                    { "uq", 3, 1398, new DateOnly(1978, 3, 10), new DateOnly(1978, 2, 8) },
                    { "uq", 4, 1398, new DateOnly(1978, 4, 8), new DateOnly(1978, 3, 10) },
                    { "uq", 5, 1398, new DateOnly(1978, 5, 8), new DateOnly(1978, 4, 8) },
                    { "uq", 6, 1398, new DateOnly(1978, 6, 6), new DateOnly(1978, 5, 8) },
                    { "uq", 7, 1398, new DateOnly(1978, 7, 6), new DateOnly(1978, 6, 6) },
                    { "uq", 8, 1398, new DateOnly(1978, 8, 5), new DateOnly(1978, 7, 6) },
                    { "uq", 9, 1398, new DateOnly(1978, 9, 3), new DateOnly(1978, 8, 5) },
                    { "uq", 10, 1398, new DateOnly(1978, 10, 3), new DateOnly(1978, 9, 3) },
                    { "uq", 11, 1398, new DateOnly(1978, 11, 1), new DateOnly(1978, 10, 3) },
                    { "uq", 12, 1398, new DateOnly(1978, 12, 1), new DateOnly(1978, 11, 1) },
                    { "uq", 1, 1399, new DateOnly(1978, 12, 30), new DateOnly(1978, 12, 1) },
                    { "uq", 2, 1399, new DateOnly(1979, 1, 29), new DateOnly(1978, 12, 30) },
                    { "uq", 3, 1399, new DateOnly(1979, 2, 27), new DateOnly(1979, 1, 29) },
                    { "uq", 4, 1399, new DateOnly(1979, 3, 29), new DateOnly(1979, 2, 27) },
                    { "uq", 5, 1399, new DateOnly(1979, 4, 27), new DateOnly(1979, 3, 29) },
                    { "uq", 6, 1399, new DateOnly(1979, 5, 27), new DateOnly(1979, 4, 27) },
                    { "uq", 7, 1399, new DateOnly(1979, 6, 25), new DateOnly(1979, 5, 27) },
                    { "uq", 8, 1399, new DateOnly(1979, 7, 25), new DateOnly(1979, 6, 25) },
                    { "uq", 9, 1399, new DateOnly(1979, 8, 23), new DateOnly(1979, 7, 25) },
                    { "uq", 10, 1399, new DateOnly(1979, 9, 22), new DateOnly(1979, 8, 23) },
                    { "uq", 11, 1399, new DateOnly(1979, 10, 22), new DateOnly(1979, 9, 22) },
                    { "uq", 12, 1399, new DateOnly(1979, 11, 20), new DateOnly(1979, 10, 22) },
                    { "uq", 1, 1400, new DateOnly(1979, 12, 20), new DateOnly(1979, 11, 20) },
                    { "uq", 2, 1400, new DateOnly(1980, 1, 18), new DateOnly(1979, 12, 20) },
                    { "uq", 3, 1400, new DateOnly(1980, 2, 17), new DateOnly(1980, 1, 18) },
                    { "uq", 4, 1400, new DateOnly(1980, 3, 17), new DateOnly(1980, 2, 17) },
                    { "uq", 5, 1400, new DateOnly(1980, 4, 16), new DateOnly(1980, 3, 17) },
                    { "uq", 6, 1400, new DateOnly(1980, 5, 15), new DateOnly(1980, 4, 16) },
                    { "uq", 7, 1400, new DateOnly(1980, 6, 13), new DateOnly(1980, 5, 15) },
                    { "uq", 8, 1400, new DateOnly(1980, 7, 13), new DateOnly(1980, 6, 13) },
                    { "uq", 9, 1400, new DateOnly(1980, 8, 11), new DateOnly(1980, 7, 13) },
                    { "uq", 10, 1400, new DateOnly(1980, 9, 10), new DateOnly(1980, 8, 11) },
                    { "uq", 11, 1400, new DateOnly(1980, 10, 10), new DateOnly(1980, 9, 10) },
                    { "uq", 12, 1400, new DateOnly(1980, 11, 8), new DateOnly(1980, 10, 10) },
                    { "uq", 1, 1401, new DateOnly(1980, 12, 8), new DateOnly(1980, 11, 8) },
                    { "uq", 2, 1401, new DateOnly(1981, 1, 7), new DateOnly(1980, 12, 8) },
                    { "uq", 3, 1401, new DateOnly(1981, 2, 5), new DateOnly(1981, 1, 7) },
                    { "uq", 4, 1401, new DateOnly(1981, 3, 7), new DateOnly(1981, 2, 5) },
                    { "uq", 5, 1401, new DateOnly(1981, 4, 5), new DateOnly(1981, 3, 7) },
                    { "uq", 6, 1401, new DateOnly(1981, 5, 5), new DateOnly(1981, 4, 5) },
                    { "uq", 7, 1401, new DateOnly(1981, 6, 3), new DateOnly(1981, 5, 5) },
                    { "uq", 8, 1401, new DateOnly(1981, 7, 2), new DateOnly(1981, 6, 3) },
                    { "uq", 9, 1401, new DateOnly(1981, 8, 1), new DateOnly(1981, 7, 2) },
                    { "uq", 10, 1401, new DateOnly(1981, 8, 30), new DateOnly(1981, 8, 1) },
                    { "uq", 11, 1401, new DateOnly(1981, 9, 29), new DateOnly(1981, 8, 30) },
                    { "uq", 12, 1401, new DateOnly(1981, 10, 28), new DateOnly(1981, 9, 29) },
                    { "uq", 1, 1402, new DateOnly(1981, 11, 27), new DateOnly(1981, 10, 28) },
                    { "uq", 2, 1402, new DateOnly(1981, 12, 27), new DateOnly(1981, 11, 27) },
                    { "uq", 3, 1402, new DateOnly(1982, 1, 26), new DateOnly(1981, 12, 27) },
                    { "uq", 4, 1402, new DateOnly(1982, 2, 24), new DateOnly(1982, 1, 26) },
                    { "uq", 5, 1402, new DateOnly(1982, 3, 26), new DateOnly(1982, 2, 24) },
                    { "uq", 6, 1402, new DateOnly(1982, 4, 24), new DateOnly(1982, 3, 26) },
                    { "uq", 7, 1402, new DateOnly(1982, 5, 24), new DateOnly(1982, 4, 24) },
                    { "uq", 8, 1402, new DateOnly(1982, 6, 22), new DateOnly(1982, 5, 24) },
                    { "uq", 9, 1402, new DateOnly(1982, 7, 21), new DateOnly(1982, 6, 22) },
                    { "uq", 10, 1402, new DateOnly(1982, 8, 20), new DateOnly(1982, 7, 21) },
                    { "uq", 11, 1402, new DateOnly(1982, 9, 18), new DateOnly(1982, 8, 20) },
                    { "uq", 12, 1402, new DateOnly(1982, 10, 18), new DateOnly(1982, 9, 18) },
                    { "uq", 1, 1403, new DateOnly(1982, 11, 16), new DateOnly(1982, 10, 18) },
                    { "uq", 2, 1403, new DateOnly(1982, 12, 16), new DateOnly(1982, 11, 16) },
                    { "uq", 3, 1403, new DateOnly(1983, 1, 15), new DateOnly(1982, 12, 16) },
                    { "uq", 4, 1403, new DateOnly(1983, 2, 14), new DateOnly(1983, 1, 15) },
                    { "uq", 5, 1403, new DateOnly(1983, 3, 15), new DateOnly(1983, 2, 14) },
                    { "uq", 6, 1403, new DateOnly(1983, 4, 14), new DateOnly(1983, 3, 15) },
                    { "uq", 7, 1403, new DateOnly(1983, 5, 13), new DateOnly(1983, 4, 14) },
                    { "uq", 8, 1403, new DateOnly(1983, 6, 12), new DateOnly(1983, 5, 13) },
                    { "uq", 9, 1403, new DateOnly(1983, 7, 11), new DateOnly(1983, 6, 12) },
                    { "uq", 10, 1403, new DateOnly(1983, 8, 9), new DateOnly(1983, 7, 11) },
                    { "uq", 11, 1403, new DateOnly(1983, 9, 8), new DateOnly(1983, 8, 9) },
                    { "uq", 12, 1403, new DateOnly(1983, 10, 7), new DateOnly(1983, 9, 8) },
                    { "uq", 1, 1404, new DateOnly(1983, 11, 5), new DateOnly(1983, 10, 7) },
                    { "uq", 2, 1404, new DateOnly(1983, 12, 5), new DateOnly(1983, 11, 5) },
                    { "uq", 3, 1404, new DateOnly(1984, 1, 4), new DateOnly(1983, 12, 5) },
                    { "uq", 4, 1404, new DateOnly(1984, 2, 2), new DateOnly(1984, 1, 4) },
                    { "uq", 5, 1404, new DateOnly(1984, 3, 3), new DateOnly(1984, 2, 2) },
                    { "uq", 6, 1404, new DateOnly(1984, 4, 2), new DateOnly(1984, 3, 3) },
                    { "uq", 7, 1404, new DateOnly(1984, 5, 2), new DateOnly(1984, 4, 2) },
                    { "uq", 8, 1404, new DateOnly(1984, 5, 31), new DateOnly(1984, 5, 2) },
                    { "uq", 9, 1404, new DateOnly(1984, 6, 30), new DateOnly(1984, 5, 31) },
                    { "uq", 10, 1404, new DateOnly(1984, 7, 29), new DateOnly(1984, 6, 30) },
                    { "uq", 11, 1404, new DateOnly(1984, 8, 27), new DateOnly(1984, 7, 29) },
                    { "uq", 12, 1404, new DateOnly(1984, 9, 26), new DateOnly(1984, 8, 27) },
                    { "uq", 1, 1405, new DateOnly(1984, 10, 25), new DateOnly(1984, 9, 26) },
                    { "uq", 2, 1405, new DateOnly(1984, 11, 23), new DateOnly(1984, 10, 25) },
                    { "uq", 3, 1405, new DateOnly(1984, 12, 23), new DateOnly(1984, 11, 23) },
                    { "uq", 4, 1405, new DateOnly(1985, 1, 22), new DateOnly(1984, 12, 23) },
                    { "uq", 5, 1405, new DateOnly(1985, 2, 20), new DateOnly(1985, 1, 22) },
                    { "uq", 6, 1405, new DateOnly(1985, 3, 22), new DateOnly(1985, 2, 20) },
                    { "uq", 7, 1405, new DateOnly(1985, 4, 21), new DateOnly(1985, 3, 22) },
                    { "uq", 8, 1405, new DateOnly(1985, 5, 20), new DateOnly(1985, 4, 21) },
                    { "uq", 9, 1405, new DateOnly(1985, 6, 19), new DateOnly(1985, 5, 20) },
                    { "uq", 10, 1405, new DateOnly(1985, 7, 18), new DateOnly(1985, 6, 19) },
                    { "uq", 11, 1405, new DateOnly(1985, 8, 17), new DateOnly(1985, 7, 18) },
                    { "uq", 12, 1405, new DateOnly(1985, 9, 15), new DateOnly(1985, 8, 17) },
                    { "uq", 1, 1406, new DateOnly(1985, 10, 15), new DateOnly(1985, 9, 15) },
                    { "uq", 2, 1406, new DateOnly(1985, 11, 13), new DateOnly(1985, 10, 15) },
                    { "uq", 3, 1406, new DateOnly(1985, 12, 13), new DateOnly(1985, 11, 13) },
                    { "uq", 4, 1406, new DateOnly(1986, 1, 11), new DateOnly(1985, 12, 13) },
                    { "uq", 5, 1406, new DateOnly(1986, 2, 10), new DateOnly(1986, 1, 11) },
                    { "uq", 6, 1406, new DateOnly(1986, 3, 11), new DateOnly(1986, 2, 10) },
                    { "uq", 7, 1406, new DateOnly(1986, 4, 10), new DateOnly(1986, 3, 11) },
                    { "uq", 8, 1406, new DateOnly(1986, 5, 9), new DateOnly(1986, 4, 10) },
                    { "uq", 9, 1406, new DateOnly(1986, 6, 8), new DateOnly(1986, 5, 9) },
                    { "uq", 10, 1406, new DateOnly(1986, 7, 8), new DateOnly(1986, 6, 8) },
                    { "uq", 11, 1406, new DateOnly(1986, 8, 6), new DateOnly(1986, 7, 8) },
                    { "uq", 12, 1406, new DateOnly(1986, 9, 5), new DateOnly(1986, 8, 6) },
                    { "uq", 1, 1407, new DateOnly(1986, 10, 4), new DateOnly(1986, 9, 5) },
                    { "uq", 2, 1407, new DateOnly(1986, 11, 3), new DateOnly(1986, 10, 4) },
                    { "uq", 3, 1407, new DateOnly(1986, 12, 2), new DateOnly(1986, 11, 3) },
                    { "uq", 4, 1407, new DateOnly(1987, 1, 1), new DateOnly(1986, 12, 2) },
                    { "uq", 5, 1407, new DateOnly(1987, 1, 30), new DateOnly(1987, 1, 1) },
                    { "uq", 6, 1407, new DateOnly(1987, 3, 1), new DateOnly(1987, 1, 30) },
                    { "uq", 7, 1407, new DateOnly(1987, 3, 30), new DateOnly(1987, 3, 1) },
                    { "uq", 8, 1407, new DateOnly(1987, 4, 29), new DateOnly(1987, 3, 30) },
                    { "uq", 9, 1407, new DateOnly(1987, 5, 28), new DateOnly(1987, 4, 29) },
                    { "uq", 10, 1407, new DateOnly(1987, 6, 27), new DateOnly(1987, 5, 28) },
                    { "uq", 11, 1407, new DateOnly(1987, 7, 26), new DateOnly(1987, 6, 27) },
                    { "uq", 12, 1407, new DateOnly(1987, 8, 25), new DateOnly(1987, 7, 26) },
                    { "uq", 1, 1408, new DateOnly(1987, 9, 24), new DateOnly(1987, 8, 25) },
                    { "uq", 2, 1408, new DateOnly(1987, 10, 23), new DateOnly(1987, 9, 24) },
                    { "uq", 3, 1408, new DateOnly(1987, 11, 22), new DateOnly(1987, 10, 23) },
                    { "uq", 4, 1408, new DateOnly(1987, 12, 21), new DateOnly(1987, 11, 22) },
                    { "uq", 5, 1408, new DateOnly(1988, 1, 20), new DateOnly(1987, 12, 21) },
                    { "uq", 6, 1408, new DateOnly(1988, 2, 18), new DateOnly(1988, 1, 20) },
                    { "uq", 7, 1408, new DateOnly(1988, 3, 19), new DateOnly(1988, 2, 18) },
                    { "uq", 8, 1408, new DateOnly(1988, 4, 17), new DateOnly(1988, 3, 19) },
                    { "uq", 9, 1408, new DateOnly(1988, 5, 16), new DateOnly(1988, 4, 17) },
                    { "uq", 10, 1408, new DateOnly(1988, 6, 15), new DateOnly(1988, 5, 16) },
                    { "uq", 11, 1408, new DateOnly(1988, 7, 14), new DateOnly(1988, 6, 15) },
                    { "uq", 12, 1408, new DateOnly(1988, 8, 13), new DateOnly(1988, 7, 14) },
                    { "uq", 1, 1409, new DateOnly(1988, 9, 12), new DateOnly(1988, 8, 13) },
                    { "uq", 2, 1409, new DateOnly(1988, 10, 11), new DateOnly(1988, 9, 12) },
                    { "uq", 3, 1409, new DateOnly(1988, 11, 10), new DateOnly(1988, 10, 11) },
                    { "uq", 4, 1409, new DateOnly(1988, 12, 10), new DateOnly(1988, 11, 10) },
                    { "uq", 5, 1409, new DateOnly(1989, 1, 8), new DateOnly(1988, 12, 10) },
                    { "uq", 6, 1409, new DateOnly(1989, 2, 7), new DateOnly(1989, 1, 8) },
                    { "uq", 7, 1409, new DateOnly(1989, 3, 8), new DateOnly(1989, 2, 7) },
                    { "uq", 8, 1409, new DateOnly(1989, 4, 7), new DateOnly(1989, 3, 8) },
                    { "uq", 9, 1409, new DateOnly(1989, 5, 6), new DateOnly(1989, 4, 7) },
                    { "uq", 10, 1409, new DateOnly(1989, 6, 4), new DateOnly(1989, 5, 6) },
                    { "uq", 11, 1409, new DateOnly(1989, 7, 4), new DateOnly(1989, 6, 4) },
                    { "uq", 12, 1409, new DateOnly(1989, 8, 2), new DateOnly(1989, 7, 4) },
                    { "uq", 1, 1410, new DateOnly(1989, 9, 1), new DateOnly(1989, 8, 2) },
                    { "uq", 2, 1410, new DateOnly(1989, 9, 30), new DateOnly(1989, 9, 1) },
                    { "uq", 3, 1410, new DateOnly(1989, 10, 30), new DateOnly(1989, 9, 30) },
                    { "uq", 4, 1410, new DateOnly(1989, 11, 29), new DateOnly(1989, 10, 30) },
                    { "uq", 5, 1410, new DateOnly(1989, 12, 29), new DateOnly(1989, 11, 29) },
                    { "uq", 6, 1410, new DateOnly(1990, 1, 27), new DateOnly(1989, 12, 29) },
                    { "uq", 7, 1410, new DateOnly(1990, 2, 26), new DateOnly(1990, 1, 27) },
                    { "uq", 8, 1410, new DateOnly(1990, 3, 27), new DateOnly(1990, 2, 26) },
                    { "uq", 9, 1410, new DateOnly(1990, 4, 26), new DateOnly(1990, 3, 27) },
                    { "uq", 10, 1410, new DateOnly(1990, 5, 25), new DateOnly(1990, 4, 26) },
                    { "uq", 11, 1410, new DateOnly(1990, 6, 23), new DateOnly(1990, 5, 25) },
                    { "uq", 12, 1410, new DateOnly(1990, 7, 23), new DateOnly(1990, 6, 23) },
                    { "uq", 1, 1411, new DateOnly(1990, 8, 21), new DateOnly(1990, 7, 23) },
                    { "uq", 2, 1411, new DateOnly(1990, 9, 20), new DateOnly(1990, 8, 21) },
                    { "uq", 3, 1411, new DateOnly(1990, 10, 19), new DateOnly(1990, 9, 20) },
                    { "uq", 4, 1411, new DateOnly(1990, 11, 18), new DateOnly(1990, 10, 19) },
                    { "uq", 5, 1411, new DateOnly(1990, 12, 18), new DateOnly(1990, 11, 18) },
                    { "uq", 6, 1411, new DateOnly(1991, 1, 16), new DateOnly(1990, 12, 18) },
                    { "uq", 7, 1411, new DateOnly(1991, 2, 15), new DateOnly(1991, 1, 16) },
                    { "uq", 8, 1411, new DateOnly(1991, 3, 17), new DateOnly(1991, 2, 15) },
                    { "uq", 9, 1411, new DateOnly(1991, 4, 15), new DateOnly(1991, 3, 17) },
                    { "uq", 10, 1411, new DateOnly(1991, 5, 15), new DateOnly(1991, 4, 15) },
                    { "uq", 11, 1411, new DateOnly(1991, 6, 13), new DateOnly(1991, 5, 15) },
                    { "uq", 12, 1411, new DateOnly(1991, 7, 12), new DateOnly(1991, 6, 13) },
                    { "uq", 1, 1412, new DateOnly(1991, 8, 11), new DateOnly(1991, 7, 12) },
                    { "uq", 2, 1412, new DateOnly(1991, 9, 9), new DateOnly(1991, 8, 11) },
                    { "uq", 3, 1412, new DateOnly(1991, 10, 8), new DateOnly(1991, 9, 9) },
                    { "uq", 4, 1412, new DateOnly(1991, 11, 7), new DateOnly(1991, 10, 8) },
                    { "uq", 5, 1412, new DateOnly(1991, 12, 7), new DateOnly(1991, 11, 7) },
                    { "uq", 6, 1412, new DateOnly(1992, 1, 5), new DateOnly(1991, 12, 7) },
                    { "uq", 7, 1412, new DateOnly(1992, 2, 4), new DateOnly(1992, 1, 5) },
                    { "uq", 8, 1412, new DateOnly(1992, 3, 5), new DateOnly(1992, 2, 4) },
                    { "uq", 9, 1412, new DateOnly(1992, 4, 4), new DateOnly(1992, 3, 5) },
                    { "uq", 10, 1412, new DateOnly(1992, 5, 3), new DateOnly(1992, 4, 4) },
                    { "uq", 11, 1412, new DateOnly(1992, 6, 2), new DateOnly(1992, 5, 3) },
                    { "uq", 12, 1412, new DateOnly(1992, 7, 1), new DateOnly(1992, 6, 2) },
                    { "uq", 1, 1413, new DateOnly(1992, 7, 30), new DateOnly(1992, 7, 1) },
                    { "uq", 2, 1413, new DateOnly(1992, 8, 29), new DateOnly(1992, 7, 30) },
                    { "uq", 3, 1413, new DateOnly(1992, 9, 27), new DateOnly(1992, 8, 29) },
                    { "uq", 4, 1413, new DateOnly(1992, 10, 26), new DateOnly(1992, 9, 27) },
                    { "uq", 5, 1413, new DateOnly(1992, 11, 25), new DateOnly(1992, 10, 26) },
                    { "uq", 6, 1413, new DateOnly(1992, 12, 25), new DateOnly(1992, 11, 25) },
                    { "uq", 7, 1413, new DateOnly(1993, 1, 23), new DateOnly(1992, 12, 25) },
                    { "uq", 8, 1413, new DateOnly(1993, 2, 22), new DateOnly(1993, 1, 23) },
                    { "uq", 9, 1413, new DateOnly(1993, 3, 24), new DateOnly(1993, 2, 22) },
                    { "uq", 10, 1413, new DateOnly(1993, 4, 22), new DateOnly(1993, 3, 24) },
                    { "uq", 11, 1413, new DateOnly(1993, 5, 22), new DateOnly(1993, 4, 22) },
                    { "uq", 12, 1413, new DateOnly(1993, 6, 21), new DateOnly(1993, 5, 22) },
                    { "uq", 1, 1414, new DateOnly(1993, 7, 20), new DateOnly(1993, 6, 21) },
                    { "uq", 2, 1414, new DateOnly(1993, 8, 18), new DateOnly(1993, 7, 20) },
                    { "uq", 3, 1414, new DateOnly(1993, 9, 17), new DateOnly(1993, 8, 18) },
                    { "uq", 4, 1414, new DateOnly(1993, 10, 16), new DateOnly(1993, 9, 17) },
                    { "uq", 5, 1414, new DateOnly(1993, 11, 14), new DateOnly(1993, 10, 16) },
                    { "uq", 6, 1414, new DateOnly(1993, 12, 14), new DateOnly(1993, 11, 14) },
                    { "uq", 7, 1414, new DateOnly(1994, 1, 12), new DateOnly(1993, 12, 14) },
                    { "uq", 8, 1414, new DateOnly(1994, 2, 11), new DateOnly(1994, 1, 12) },
                    { "uq", 9, 1414, new DateOnly(1994, 3, 13), new DateOnly(1994, 2, 11) },
                    { "uq", 10, 1414, new DateOnly(1994, 4, 12), new DateOnly(1994, 3, 13) },
                    { "uq", 11, 1414, new DateOnly(1994, 5, 11), new DateOnly(1994, 4, 12) },
                    { "uq", 12, 1414, new DateOnly(1994, 6, 10), new DateOnly(1994, 5, 11) },
                    { "uq", 1, 1415, new DateOnly(1994, 7, 9), new DateOnly(1994, 6, 10) },
                    { "uq", 2, 1415, new DateOnly(1994, 8, 8), new DateOnly(1994, 7, 9) },
                    { "uq", 3, 1415, new DateOnly(1994, 9, 6), new DateOnly(1994, 8, 8) },
                    { "uq", 4, 1415, new DateOnly(1994, 10, 6), new DateOnly(1994, 9, 6) },
                    { "uq", 5, 1415, new DateOnly(1994, 11, 4), new DateOnly(1994, 10, 6) },
                    { "uq", 6, 1415, new DateOnly(1994, 12, 3), new DateOnly(1994, 11, 4) },
                    { "uq", 7, 1415, new DateOnly(1995, 1, 2), new DateOnly(1994, 12, 3) },
                    { "uq", 8, 1415, new DateOnly(1995, 1, 31), new DateOnly(1995, 1, 2) },
                    { "uq", 9, 1415, new DateOnly(1995, 3, 2), new DateOnly(1995, 1, 31) },
                    { "uq", 10, 1415, new DateOnly(1995, 4, 1), new DateOnly(1995, 3, 2) },
                    { "uq", 11, 1415, new DateOnly(1995, 4, 30), new DateOnly(1995, 4, 1) },
                    { "uq", 12, 1415, new DateOnly(1995, 5, 30), new DateOnly(1995, 4, 30) },
                    { "uq", 1, 1416, new DateOnly(1995, 6, 29), new DateOnly(1995, 5, 30) },
                    { "uq", 2, 1416, new DateOnly(1995, 7, 28), new DateOnly(1995, 6, 29) },
                    { "uq", 3, 1416, new DateOnly(1995, 8, 27), new DateOnly(1995, 7, 28) },
                    { "uq", 4, 1416, new DateOnly(1995, 9, 25), new DateOnly(1995, 8, 27) },
                    { "uq", 5, 1416, new DateOnly(1995, 10, 25), new DateOnly(1995, 9, 25) },
                    { "uq", 6, 1416, new DateOnly(1995, 11, 23), new DateOnly(1995, 10, 25) },
                    { "uq", 7, 1416, new DateOnly(1995, 12, 23), new DateOnly(1995, 11, 23) },
                    { "uq", 8, 1416, new DateOnly(1996, 1, 21), new DateOnly(1995, 12, 23) },
                    { "uq", 9, 1416, new DateOnly(1996, 2, 19), new DateOnly(1996, 1, 21) },
                    { "uq", 10, 1416, new DateOnly(1996, 3, 20), new DateOnly(1996, 2, 19) },
                    { "uq", 11, 1416, new DateOnly(1996, 4, 18), new DateOnly(1996, 3, 20) },
                    { "uq", 12, 1416, new DateOnly(1996, 5, 18), new DateOnly(1996, 4, 18) },
                    { "uq", 1, 1417, new DateOnly(1996, 6, 17), new DateOnly(1996, 5, 18) },
                    { "uq", 2, 1417, new DateOnly(1996, 7, 16), new DateOnly(1996, 6, 17) },
                    { "uq", 3, 1417, new DateOnly(1996, 8, 15), new DateOnly(1996, 7, 16) },
                    { "uq", 4, 1417, new DateOnly(1996, 9, 13), new DateOnly(1996, 8, 15) },
                    { "uq", 5, 1417, new DateOnly(1996, 10, 13), new DateOnly(1996, 9, 13) },
                    { "uq", 6, 1417, new DateOnly(1996, 11, 12), new DateOnly(1996, 10, 13) },
                    { "uq", 7, 1417, new DateOnly(1996, 12, 11), new DateOnly(1996, 11, 12) },
                    { "uq", 8, 1417, new DateOnly(1997, 1, 10), new DateOnly(1996, 12, 11) },
                    { "uq", 9, 1417, new DateOnly(1997, 2, 8), new DateOnly(1997, 1, 10) },
                    { "uq", 10, 1417, new DateOnly(1997, 3, 10), new DateOnly(1997, 2, 8) },
                    { "uq", 11, 1417, new DateOnly(1997, 4, 8), new DateOnly(1997, 3, 10) },
                    { "uq", 12, 1417, new DateOnly(1997, 5, 7), new DateOnly(1997, 4, 8) },
                    { "uq", 1, 1418, new DateOnly(1997, 6, 6), new DateOnly(1997, 5, 7) },
                    { "uq", 2, 1418, new DateOnly(1997, 7, 5), new DateOnly(1997, 6, 6) },
                    { "uq", 3, 1418, new DateOnly(1997, 8, 4), new DateOnly(1997, 7, 5) },
                    { "uq", 4, 1418, new DateOnly(1997, 9, 2), new DateOnly(1997, 8, 4) },
                    { "uq", 5, 1418, new DateOnly(1997, 10, 2), new DateOnly(1997, 9, 2) },
                    { "uq", 6, 1418, new DateOnly(1997, 11, 1), new DateOnly(1997, 10, 2) },
                    { "uq", 7, 1418, new DateOnly(1997, 12, 1), new DateOnly(1997, 11, 1) },
                    { "uq", 8, 1418, new DateOnly(1997, 12, 30), new DateOnly(1997, 12, 1) },
                    { "uq", 9, 1418, new DateOnly(1998, 1, 29), new DateOnly(1997, 12, 30) },
                    { "uq", 10, 1418, new DateOnly(1998, 2, 27), new DateOnly(1998, 1, 29) },
                    { "uq", 11, 1418, new DateOnly(1998, 3, 29), new DateOnly(1998, 2, 27) },
                    { "uq", 12, 1418, new DateOnly(1998, 4, 27), new DateOnly(1998, 3, 29) },
                    { "uq", 1, 1419, new DateOnly(1998, 5, 26), new DateOnly(1998, 4, 27) },
                    { "uq", 2, 1419, new DateOnly(1998, 6, 25), new DateOnly(1998, 5, 26) },
                    { "uq", 3, 1419, new DateOnly(1998, 7, 24), new DateOnly(1998, 6, 25) },
                    { "uq", 4, 1419, new DateOnly(1998, 8, 23), new DateOnly(1998, 7, 24) },
                    { "uq", 5, 1419, new DateOnly(1998, 9, 21), new DateOnly(1998, 8, 23) },
                    { "uq", 6, 1419, new DateOnly(1998, 10, 21), new DateOnly(1998, 9, 21) },
                    { "uq", 7, 1419, new DateOnly(1998, 11, 20), new DateOnly(1998, 10, 21) },
                    { "uq", 8, 1419, new DateOnly(1998, 12, 19), new DateOnly(1998, 11, 20) },
                    { "uq", 9, 1419, new DateOnly(1999, 1, 18), new DateOnly(1998, 12, 19) },
                    { "uq", 10, 1419, new DateOnly(1999, 2, 17), new DateOnly(1999, 1, 18) },
                    { "uq", 11, 1419, new DateOnly(1999, 3, 18), new DateOnly(1999, 2, 17) },
                    { "uq", 12, 1419, new DateOnly(1999, 4, 17), new DateOnly(1999, 3, 18) },
                    { "uq", 1, 1420, new DateOnly(1999, 5, 16), new DateOnly(1999, 4, 17) },
                    { "uq", 2, 1420, new DateOnly(1999, 6, 15), new DateOnly(1999, 5, 16) },
                    { "uq", 3, 1420, new DateOnly(1999, 7, 14), new DateOnly(1999, 6, 15) },
                    { "uq", 4, 1420, new DateOnly(1999, 8, 12), new DateOnly(1999, 7, 14) },
                    { "uq", 5, 1420, new DateOnly(1999, 9, 11), new DateOnly(1999, 8, 12) },
                    { "uq", 6, 1420, new DateOnly(1999, 10, 10), new DateOnly(1999, 9, 11) },
                    { "uq", 7, 1420, new DateOnly(1999, 11, 9), new DateOnly(1999, 10, 10) },
                    { "uq", 8, 1420, new DateOnly(1999, 12, 9), new DateOnly(1999, 11, 9) },
                    { "uq", 9, 1420, new DateOnly(2000, 1, 8), new DateOnly(1999, 12, 9) },
                    { "uq", 10, 1420, new DateOnly(2000, 2, 7), new DateOnly(2000, 1, 8) },
                    { "uq", 11, 1420, new DateOnly(2000, 3, 7), new DateOnly(2000, 2, 7) },
                    { "uq", 12, 1420, new DateOnly(2000, 4, 6), new DateOnly(2000, 3, 7) },
                    { "uq", 1, 1421, new DateOnly(2000, 5, 5), new DateOnly(2000, 4, 6) },
                    { "uq", 2, 1421, new DateOnly(2000, 6, 3), new DateOnly(2000, 5, 5) },
                    { "uq", 3, 1421, new DateOnly(2000, 7, 3), new DateOnly(2000, 6, 3) },
                    { "uq", 4, 1421, new DateOnly(2000, 8, 1), new DateOnly(2000, 7, 3) },
                    { "uq", 5, 1421, new DateOnly(2000, 8, 30), new DateOnly(2000, 8, 1) },
                    { "uq", 6, 1421, new DateOnly(2000, 9, 28), new DateOnly(2000, 8, 30) },
                    { "uq", 7, 1421, new DateOnly(2000, 10, 28), new DateOnly(2000, 9, 28) },
                    { "uq", 8, 1421, new DateOnly(2000, 11, 27), new DateOnly(2000, 10, 28) },
                    { "uq", 9, 1421, new DateOnly(2000, 12, 27), new DateOnly(2000, 11, 27) },
                    { "uq", 10, 1421, new DateOnly(2001, 1, 26), new DateOnly(2000, 12, 27) },
                    { "uq", 11, 1421, new DateOnly(2001, 2, 24), new DateOnly(2001, 1, 26) },
                    { "uq", 12, 1421, new DateOnly(2001, 3, 26), new DateOnly(2001, 2, 24) },
                    { "uq", 1, 1422, new DateOnly(2001, 4, 25), new DateOnly(2001, 3, 26) },
                    { "uq", 2, 1422, new DateOnly(2001, 5, 24), new DateOnly(2001, 4, 25) },
                    { "uq", 3, 1422, new DateOnly(2001, 6, 22), new DateOnly(2001, 5, 24) },
                    { "uq", 4, 1422, new DateOnly(2001, 7, 22), new DateOnly(2001, 6, 22) },
                    { "uq", 5, 1422, new DateOnly(2001, 8, 20), new DateOnly(2001, 7, 22) },
                    { "uq", 6, 1422, new DateOnly(2001, 9, 18), new DateOnly(2001, 8, 20) },
                    { "uq", 7, 1422, new DateOnly(2001, 10, 17), new DateOnly(2001, 9, 18) },
                    { "uq", 8, 1422, new DateOnly(2001, 11, 16), new DateOnly(2001, 10, 17) },
                    { "uq", 9, 1422, new DateOnly(2001, 12, 16), new DateOnly(2001, 11, 16) },
                    { "uq", 10, 1422, new DateOnly(2002, 1, 15), new DateOnly(2001, 12, 16) },
                    { "uq", 11, 1422, new DateOnly(2002, 2, 13), new DateOnly(2002, 1, 15) },
                    { "uq", 12, 1422, new DateOnly(2002, 3, 15), new DateOnly(2002, 2, 13) },
                    { "uq", 1, 1423, new DateOnly(2002, 4, 14), new DateOnly(2002, 3, 15) },
                    { "uq", 2, 1423, new DateOnly(2002, 5, 13), new DateOnly(2002, 4, 14) },
                    { "uq", 3, 1423, new DateOnly(2002, 6, 12), new DateOnly(2002, 5, 13) },
                    { "uq", 4, 1423, new DateOnly(2002, 7, 11), new DateOnly(2002, 6, 12) },
                    { "uq", 5, 1423, new DateOnly(2002, 8, 10), new DateOnly(2002, 7, 11) },
                    { "uq", 6, 1423, new DateOnly(2002, 9, 8), new DateOnly(2002, 8, 10) },
                    { "uq", 7, 1423, new DateOnly(2002, 10, 7), new DateOnly(2002, 9, 8) },
                    { "uq", 8, 1423, new DateOnly(2002, 11, 6), new DateOnly(2002, 10, 7) },
                    { "uq", 9, 1423, new DateOnly(2002, 12, 5), new DateOnly(2002, 11, 6) },
                    { "uq", 10, 1423, new DateOnly(2003, 1, 4), new DateOnly(2002, 12, 5) },
                    { "uq", 11, 1423, new DateOnly(2003, 2, 2), new DateOnly(2003, 1, 4) },
                    { "uq", 12, 1423, new DateOnly(2003, 3, 4), new DateOnly(2003, 2, 2) },
                    { "uq", 1, 1424, new DateOnly(2003, 4, 3), new DateOnly(2003, 3, 4) },
                    { "uq", 2, 1424, new DateOnly(2003, 5, 2), new DateOnly(2003, 4, 3) },
                    { "uq", 3, 1424, new DateOnly(2003, 6, 1), new DateOnly(2003, 5, 2) },
                    { "uq", 4, 1424, new DateOnly(2003, 7, 1), new DateOnly(2003, 6, 1) },
                    { "uq", 5, 1424, new DateOnly(2003, 7, 30), new DateOnly(2003, 7, 1) },
                    { "uq", 6, 1424, new DateOnly(2003, 8, 29), new DateOnly(2003, 7, 30) },
                    { "uq", 7, 1424, new DateOnly(2003, 9, 27), new DateOnly(2003, 8, 29) },
                    { "uq", 8, 1424, new DateOnly(2003, 10, 26), new DateOnly(2003, 9, 27) },
                    { "uq", 9, 1424, new DateOnly(2003, 11, 25), new DateOnly(2003, 10, 26) },
                    { "uq", 10, 1424, new DateOnly(2003, 12, 24), new DateOnly(2003, 11, 25) },
                    { "uq", 11, 1424, new DateOnly(2004, 1, 23), new DateOnly(2003, 12, 24) },
                    { "uq", 12, 1424, new DateOnly(2004, 2, 21), new DateOnly(2004, 1, 23) },
                    { "uq", 1, 1425, new DateOnly(2004, 3, 22), new DateOnly(2004, 2, 21) },
                    { "uq", 2, 1425, new DateOnly(2004, 4, 20), new DateOnly(2004, 3, 22) },
                    { "uq", 3, 1425, new DateOnly(2004, 5, 20), new DateOnly(2004, 4, 20) },
                    { "uq", 4, 1425, new DateOnly(2004, 6, 19), new DateOnly(2004, 5, 20) },
                    { "uq", 5, 1425, new DateOnly(2004, 7, 18), new DateOnly(2004, 6, 19) },
                    { "uq", 6, 1425, new DateOnly(2004, 8, 17), new DateOnly(2004, 7, 18) },
                    { "uq", 7, 1425, new DateOnly(2004, 9, 15), new DateOnly(2004, 8, 17) },
                    { "uq", 8, 1425, new DateOnly(2004, 10, 15), new DateOnly(2004, 9, 15) },
                    { "uq", 9, 1425, new DateOnly(2004, 11, 14), new DateOnly(2004, 10, 15) },
                    { "uq", 10, 1425, new DateOnly(2004, 12, 13), new DateOnly(2004, 11, 14) },
                    { "uq", 11, 1425, new DateOnly(2005, 1, 12), new DateOnly(2004, 12, 13) },
                    { "uq", 12, 1425, new DateOnly(2005, 2, 10), new DateOnly(2005, 1, 12) },
                    { "uq", 1, 1426, new DateOnly(2005, 3, 11), new DateOnly(2005, 2, 10) },
                    { "uq", 2, 1426, new DateOnly(2005, 4, 10), new DateOnly(2005, 3, 11) },
                    { "uq", 3, 1426, new DateOnly(2005, 5, 9), new DateOnly(2005, 4, 10) },
                    { "uq", 4, 1426, new DateOnly(2005, 6, 8), new DateOnly(2005, 5, 9) },
                    { "uq", 5, 1426, new DateOnly(2005, 7, 7), new DateOnly(2005, 6, 8) },
                    { "uq", 6, 1426, new DateOnly(2005, 8, 6), new DateOnly(2005, 7, 7) },
                    { "uq", 7, 1426, new DateOnly(2005, 9, 5), new DateOnly(2005, 8, 6) },
                    { "uq", 8, 1426, new DateOnly(2005, 10, 4), new DateOnly(2005, 9, 5) },
                    { "uq", 9, 1426, new DateOnly(2005, 11, 3), new DateOnly(2005, 10, 4) },
                    { "uq", 10, 1426, new DateOnly(2005, 12, 3), new DateOnly(2005, 11, 3) },
                    { "uq", 11, 1426, new DateOnly(2006, 1, 1), new DateOnly(2005, 12, 3) },
                    { "uq", 12, 1426, new DateOnly(2006, 1, 31), new DateOnly(2006, 1, 1) },
                    { "uq", 1, 1427, new DateOnly(2006, 3, 1), new DateOnly(2006, 1, 31) },
                    { "uq", 2, 1427, new DateOnly(2006, 3, 30), new DateOnly(2006, 3, 1) },
                    { "uq", 3, 1427, new DateOnly(2006, 4, 29), new DateOnly(2006, 3, 30) },
                    { "uq", 4, 1427, new DateOnly(2006, 5, 28), new DateOnly(2006, 4, 29) },
                    { "uq", 5, 1427, new DateOnly(2006, 6, 27), new DateOnly(2006, 5, 28) },
                    { "uq", 6, 1427, new DateOnly(2006, 7, 26), new DateOnly(2006, 6, 27) },
                    { "uq", 7, 1427, new DateOnly(2006, 8, 25), new DateOnly(2006, 7, 26) },
                    { "uq", 8, 1427, new DateOnly(2006, 9, 24), new DateOnly(2006, 8, 25) },
                    { "uq", 9, 1427, new DateOnly(2006, 10, 23), new DateOnly(2006, 9, 24) },
                    { "uq", 10, 1427, new DateOnly(2006, 11, 22), new DateOnly(2006, 10, 23) },
                    { "uq", 11, 1427, new DateOnly(2006, 12, 22), new DateOnly(2006, 11, 22) },
                    { "uq", 12, 1427, new DateOnly(2007, 1, 20), new DateOnly(2006, 12, 22) },
                    { "uq", 1, 1428, new DateOnly(2007, 2, 19), new DateOnly(2007, 1, 20) },
                    { "uq", 2, 1428, new DateOnly(2007, 3, 20), new DateOnly(2007, 2, 19) },
                    { "uq", 3, 1428, new DateOnly(2007, 4, 18), new DateOnly(2007, 3, 20) },
                    { "uq", 4, 1428, new DateOnly(2007, 5, 18), new DateOnly(2007, 4, 18) },
                    { "uq", 5, 1428, new DateOnly(2007, 6, 16), new DateOnly(2007, 5, 18) },
                    { "uq", 6, 1428, new DateOnly(2007, 7, 15), new DateOnly(2007, 6, 16) },
                    { "uq", 7, 1428, new DateOnly(2007, 8, 14), new DateOnly(2007, 7, 15) },
                    { "uq", 8, 1428, new DateOnly(2007, 9, 13), new DateOnly(2007, 8, 14) },
                    { "uq", 9, 1428, new DateOnly(2007, 10, 13), new DateOnly(2007, 9, 13) },
                    { "uq", 10, 1428, new DateOnly(2007, 11, 11), new DateOnly(2007, 10, 13) },
                    { "uq", 11, 1428, new DateOnly(2007, 12, 11), new DateOnly(2007, 11, 11) },
                    { "uq", 12, 1428, new DateOnly(2008, 1, 10), new DateOnly(2007, 12, 11) },
                    { "uq", 1, 1429, new DateOnly(2008, 2, 8), new DateOnly(2008, 1, 10) },
                    { "uq", 2, 1429, new DateOnly(2008, 3, 9), new DateOnly(2008, 2, 8) },
                    { "uq", 3, 1429, new DateOnly(2008, 4, 7), new DateOnly(2008, 3, 9) },
                    { "uq", 4, 1429, new DateOnly(2008, 5, 6), new DateOnly(2008, 4, 7) },
                    { "uq", 5, 1429, new DateOnly(2008, 6, 5), new DateOnly(2008, 5, 6) },
                    { "uq", 6, 1429, new DateOnly(2008, 7, 4), new DateOnly(2008, 6, 5) },
                    { "uq", 7, 1429, new DateOnly(2008, 8, 2), new DateOnly(2008, 7, 4) },
                    { "uq", 8, 1429, new DateOnly(2008, 9, 1), new DateOnly(2008, 8, 2) },
                    { "uq", 9, 1429, new DateOnly(2008, 10, 1), new DateOnly(2008, 9, 1) },
                    { "uq", 10, 1429, new DateOnly(2008, 10, 30), new DateOnly(2008, 10, 1) },
                    { "uq", 11, 1429, new DateOnly(2008, 11, 29), new DateOnly(2008, 10, 30) },
                    { "uq", 12, 1429, new DateOnly(2008, 12, 29), new DateOnly(2008, 11, 29) },
                    { "uq", 1, 1430, new DateOnly(2009, 1, 27), new DateOnly(2008, 12, 29) },
                    { "uq", 2, 1430, new DateOnly(2009, 2, 26), new DateOnly(2009, 1, 27) },
                    { "uq", 3, 1430, new DateOnly(2009, 3, 28), new DateOnly(2009, 2, 26) },
                    { "uq", 4, 1430, new DateOnly(2009, 4, 26), new DateOnly(2009, 3, 28) },
                    { "uq", 5, 1430, new DateOnly(2009, 5, 25), new DateOnly(2009, 4, 26) },
                    { "uq", 6, 1430, new DateOnly(2009, 6, 24), new DateOnly(2009, 5, 25) },
                    { "uq", 7, 1430, new DateOnly(2009, 7, 23), new DateOnly(2009, 6, 24) },
                    { "uq", 8, 1430, new DateOnly(2009, 8, 22), new DateOnly(2009, 7, 23) },
                    { "uq", 9, 1430, new DateOnly(2009, 9, 20), new DateOnly(2009, 8, 22) },
                    { "uq", 10, 1430, new DateOnly(2009, 10, 20), new DateOnly(2009, 9, 20) },
                    { "uq", 11, 1430, new DateOnly(2009, 11, 18), new DateOnly(2009, 10, 20) },
                    { "uq", 12, 1430, new DateOnly(2009, 12, 18), new DateOnly(2009, 11, 18) },
                    { "uq", 1, 1431, new DateOnly(2010, 1, 16), new DateOnly(2009, 12, 18) },
                    { "uq", 2, 1431, new DateOnly(2010, 2, 15), new DateOnly(2010, 1, 16) },
                    { "uq", 3, 1431, new DateOnly(2010, 3, 17), new DateOnly(2010, 2, 15) },
                    { "uq", 4, 1431, new DateOnly(2010, 4, 15), new DateOnly(2010, 3, 17) },
                    { "uq", 5, 1431, new DateOnly(2010, 5, 15), new DateOnly(2010, 4, 15) },
                    { "uq", 6, 1431, new DateOnly(2010, 6, 13), new DateOnly(2010, 5, 15) },
                    { "uq", 7, 1431, new DateOnly(2010, 7, 13), new DateOnly(2010, 6, 13) },
                    { "uq", 8, 1431, new DateOnly(2010, 8, 11), new DateOnly(2010, 7, 13) },
                    { "uq", 9, 1431, new DateOnly(2010, 9, 10), new DateOnly(2010, 8, 11) },
                    { "uq", 10, 1431, new DateOnly(2010, 10, 9), new DateOnly(2010, 9, 10) },
                    { "uq", 11, 1431, new DateOnly(2010, 11, 7), new DateOnly(2010, 10, 9) },
                    { "uq", 12, 1431, new DateOnly(2010, 12, 7), new DateOnly(2010, 11, 7) },
                    { "uq", 1, 1432, new DateOnly(2011, 1, 5), new DateOnly(2010, 12, 7) },
                    { "uq", 2, 1432, new DateOnly(2011, 2, 4), new DateOnly(2011, 1, 5) },
                    { "uq", 3, 1432, new DateOnly(2011, 3, 6), new DateOnly(2011, 2, 4) },
                    { "uq", 4, 1432, new DateOnly(2011, 4, 5), new DateOnly(2011, 3, 6) },
                    { "uq", 5, 1432, new DateOnly(2011, 5, 4), new DateOnly(2011, 4, 5) },
                    { "uq", 6, 1432, new DateOnly(2011, 6, 3), new DateOnly(2011, 5, 4) },
                    { "uq", 7, 1432, new DateOnly(2011, 7, 2), new DateOnly(2011, 6, 3) },
                    { "uq", 8, 1432, new DateOnly(2011, 8, 1), new DateOnly(2011, 7, 2) },
                    { "uq", 9, 1432, new DateOnly(2011, 8, 30), new DateOnly(2011, 8, 1) },
                    { "uq", 10, 1432, new DateOnly(2011, 9, 29), new DateOnly(2011, 8, 30) },
                    { "uq", 11, 1432, new DateOnly(2011, 10, 28), new DateOnly(2011, 9, 29) },
                    { "uq", 12, 1432, new DateOnly(2011, 11, 26), new DateOnly(2011, 10, 28) },
                    { "uq", 1, 1433, new DateOnly(2011, 12, 26), new DateOnly(2011, 11, 26) },
                    { "uq", 2, 1433, new DateOnly(2012, 1, 24), new DateOnly(2011, 12, 26) },
                    { "uq", 3, 1433, new DateOnly(2012, 2, 23), new DateOnly(2012, 1, 24) },
                    { "uq", 4, 1433, new DateOnly(2012, 3, 24), new DateOnly(2012, 2, 23) },
                    { "uq", 5, 1433, new DateOnly(2012, 4, 22), new DateOnly(2012, 3, 24) },
                    { "uq", 6, 1433, new DateOnly(2012, 5, 22), new DateOnly(2012, 4, 22) },
                    { "uq", 7, 1433, new DateOnly(2012, 6, 21), new DateOnly(2012, 5, 22) },
                    { "uq", 8, 1433, new DateOnly(2012, 7, 20), new DateOnly(2012, 6, 21) },
                    { "uq", 9, 1433, new DateOnly(2012, 8, 19), new DateOnly(2012, 7, 20) },
                    { "uq", 10, 1433, new DateOnly(2012, 9, 17), new DateOnly(2012, 8, 19) },
                    { "uq", 11, 1433, new DateOnly(2012, 10, 17), new DateOnly(2012, 9, 17) },
                    { "uq", 12, 1433, new DateOnly(2012, 11, 15), new DateOnly(2012, 10, 17) },
                    { "uq", 1, 1434, new DateOnly(2012, 12, 14), new DateOnly(2012, 11, 15) },
                    { "uq", 2, 1434, new DateOnly(2013, 1, 13), new DateOnly(2012, 12, 14) },
                    { "uq", 3, 1434, new DateOnly(2013, 2, 11), new DateOnly(2013, 1, 13) },
                    { "uq", 4, 1434, new DateOnly(2013, 3, 13), new DateOnly(2013, 2, 11) },
                    { "uq", 5, 1434, new DateOnly(2013, 4, 11), new DateOnly(2013, 3, 13) },
                    { "uq", 6, 1434, new DateOnly(2013, 5, 11), new DateOnly(2013, 4, 11) },
                    { "uq", 7, 1434, new DateOnly(2013, 6, 10), new DateOnly(2013, 5, 11) },
                    { "uq", 8, 1434, new DateOnly(2013, 7, 9), new DateOnly(2013, 6, 10) },
                    { "uq", 9, 1434, new DateOnly(2013, 8, 8), new DateOnly(2013, 7, 9) },
                    { "uq", 10, 1434, new DateOnly(2013, 9, 7), new DateOnly(2013, 8, 8) },
                    { "uq", 11, 1434, new DateOnly(2013, 10, 6), new DateOnly(2013, 9, 7) },
                    { "uq", 12, 1434, new DateOnly(2013, 11, 4), new DateOnly(2013, 10, 6) },
                    { "uq", 1, 1435, new DateOnly(2013, 12, 4), new DateOnly(2013, 11, 4) },
                    { "uq", 2, 1435, new DateOnly(2014, 1, 2), new DateOnly(2013, 12, 4) },
                    { "uq", 3, 1435, new DateOnly(2014, 2, 1), new DateOnly(2014, 1, 2) },
                    { "uq", 4, 1435, new DateOnly(2014, 3, 2), new DateOnly(2014, 2, 1) },
                    { "uq", 5, 1435, new DateOnly(2014, 4, 1), new DateOnly(2014, 3, 2) },
                    { "uq", 6, 1435, new DateOnly(2014, 4, 30), new DateOnly(2014, 4, 1) },
                    { "uq", 7, 1435, new DateOnly(2014, 5, 30), new DateOnly(2014, 4, 30) },
                    { "uq", 8, 1435, new DateOnly(2014, 6, 28), new DateOnly(2014, 5, 30) },
                    { "uq", 9, 1435, new DateOnly(2014, 7, 28), new DateOnly(2014, 6, 28) },
                    { "uq", 10, 1435, new DateOnly(2014, 8, 27), new DateOnly(2014, 7, 28) },
                    { "uq", 11, 1435, new DateOnly(2014, 9, 25), new DateOnly(2014, 8, 27) },
                    { "uq", 12, 1435, new DateOnly(2014, 10, 25), new DateOnly(2014, 9, 25) },
                    { "uq", 1, 1436, new DateOnly(2014, 11, 23), new DateOnly(2014, 10, 25) },
                    { "uq", 2, 1436, new DateOnly(2014, 12, 23), new DateOnly(2014, 11, 23) },
                    { "uq", 3, 1436, new DateOnly(2015, 1, 21), new DateOnly(2014, 12, 23) },
                    { "uq", 4, 1436, new DateOnly(2015, 2, 20), new DateOnly(2015, 1, 21) },
                    { "uq", 5, 1436, new DateOnly(2015, 3, 21), new DateOnly(2015, 2, 20) },
                    { "uq", 6, 1436, new DateOnly(2015, 4, 20), new DateOnly(2015, 3, 21) },
                    { "uq", 7, 1436, new DateOnly(2015, 5, 19), new DateOnly(2015, 4, 20) },
                    { "uq", 8, 1436, new DateOnly(2015, 6, 18), new DateOnly(2015, 5, 19) },
                    { "uq", 9, 1436, new DateOnly(2015, 7, 17), new DateOnly(2015, 6, 18) },
                    { "uq", 10, 1436, new DateOnly(2015, 8, 16), new DateOnly(2015, 7, 17) },
                    { "uq", 11, 1436, new DateOnly(2015, 9, 14), new DateOnly(2015, 8, 16) },
                    { "uq", 12, 1436, new DateOnly(2015, 10, 14), new DateOnly(2015, 9, 14) },
                    { "uq", 1, 1437, new DateOnly(2015, 11, 13), new DateOnly(2015, 10, 14) },
                    { "uq", 2, 1437, new DateOnly(2015, 12, 12), new DateOnly(2015, 11, 13) },
                    { "uq", 3, 1437, new DateOnly(2016, 1, 11), new DateOnly(2015, 12, 12) },
                    { "uq", 4, 1437, new DateOnly(2016, 2, 10), new DateOnly(2016, 1, 11) },
                    { "uq", 5, 1437, new DateOnly(2016, 3, 10), new DateOnly(2016, 2, 10) },
                    { "uq", 6, 1437, new DateOnly(2016, 4, 8), new DateOnly(2016, 3, 10) },
                    { "uq", 7, 1437, new DateOnly(2016, 5, 8), new DateOnly(2016, 4, 8) },
                    { "uq", 8, 1437, new DateOnly(2016, 6, 6), new DateOnly(2016, 5, 8) },
                    { "uq", 9, 1437, new DateOnly(2016, 7, 6), new DateOnly(2016, 6, 6) },
                    { "uq", 10, 1437, new DateOnly(2016, 8, 4), new DateOnly(2016, 7, 6) },
                    { "uq", 11, 1437, new DateOnly(2016, 9, 2), new DateOnly(2016, 8, 4) },
                    { "uq", 12, 1437, new DateOnly(2016, 10, 2), new DateOnly(2016, 9, 2) },
                    { "uq", 1, 1438, new DateOnly(2016, 11, 1), new DateOnly(2016, 10, 2) },
                    { "uq", 2, 1438, new DateOnly(2016, 11, 30), new DateOnly(2016, 11, 1) },
                    { "uq", 3, 1438, new DateOnly(2016, 12, 30), new DateOnly(2016, 11, 30) },
                    { "uq", 4, 1438, new DateOnly(2017, 1, 29), new DateOnly(2016, 12, 30) },
                    { "uq", 5, 1438, new DateOnly(2017, 2, 28), new DateOnly(2017, 1, 29) },
                    { "uq", 6, 1438, new DateOnly(2017, 3, 29), new DateOnly(2017, 2, 28) },
                    { "uq", 7, 1438, new DateOnly(2017, 4, 27), new DateOnly(2017, 3, 29) },
                    { "uq", 8, 1438, new DateOnly(2017, 5, 27), new DateOnly(2017, 4, 27) },
                    { "uq", 9, 1438, new DateOnly(2017, 6, 25), new DateOnly(2017, 5, 27) },
                    { "uq", 10, 1438, new DateOnly(2017, 7, 24), new DateOnly(2017, 6, 25) },
                    { "uq", 11, 1438, new DateOnly(2017, 8, 23), new DateOnly(2017, 7, 24) },
                    { "uq", 12, 1438, new DateOnly(2017, 9, 21), new DateOnly(2017, 8, 23) },
                    { "uq", 1, 1439, new DateOnly(2017, 10, 21), new DateOnly(2017, 9, 21) },
                    { "uq", 2, 1439, new DateOnly(2017, 11, 19), new DateOnly(2017, 10, 21) },
                    { "uq", 3, 1439, new DateOnly(2017, 12, 19), new DateOnly(2017, 11, 19) },
                    { "uq", 4, 1439, new DateOnly(2018, 1, 18), new DateOnly(2017, 12, 19) },
                    { "uq", 5, 1439, new DateOnly(2018, 2, 17), new DateOnly(2018, 1, 18) },
                    { "uq", 6, 1439, new DateOnly(2018, 3, 18), new DateOnly(2018, 2, 17) },
                    { "uq", 7, 1439, new DateOnly(2018, 4, 17), new DateOnly(2018, 3, 18) },
                    { "uq", 8, 1439, new DateOnly(2018, 5, 16), new DateOnly(2018, 4, 17) },
                    { "uq", 9, 1439, new DateOnly(2018, 6, 15), new DateOnly(2018, 5, 16) },
                    { "uq", 10, 1439, new DateOnly(2018, 7, 14), new DateOnly(2018, 6, 15) },
                    { "uq", 11, 1439, new DateOnly(2018, 8, 12), new DateOnly(2018, 7, 14) },
                    { "uq", 12, 1439, new DateOnly(2018, 9, 11), new DateOnly(2018, 8, 12) },
                    { "uq", 1, 1440, new DateOnly(2018, 10, 10), new DateOnly(2018, 9, 11) },
                    { "uq", 2, 1440, new DateOnly(2018, 11, 9), new DateOnly(2018, 10, 10) },
                    { "uq", 3, 1440, new DateOnly(2018, 12, 8), new DateOnly(2018, 11, 9) },
                    { "uq", 4, 1440, new DateOnly(2019, 1, 7), new DateOnly(2018, 12, 8) },
                    { "uq", 5, 1440, new DateOnly(2019, 2, 6), new DateOnly(2019, 1, 7) },
                    { "uq", 6, 1440, new DateOnly(2019, 3, 8), new DateOnly(2019, 2, 6) },
                    { "uq", 7, 1440, new DateOnly(2019, 4, 6), new DateOnly(2019, 3, 8) },
                    { "uq", 8, 1440, new DateOnly(2019, 5, 6), new DateOnly(2019, 4, 6) },
                    { "uq", 9, 1440, new DateOnly(2019, 6, 4), new DateOnly(2019, 5, 6) },
                    { "uq", 10, 1440, new DateOnly(2019, 7, 4), new DateOnly(2019, 6, 4) },
                    { "uq", 11, 1440, new DateOnly(2019, 8, 2), new DateOnly(2019, 7, 4) },
                    { "uq", 12, 1440, new DateOnly(2019, 8, 31), new DateOnly(2019, 8, 2) },
                    { "uq", 1, 1441, new DateOnly(2019, 9, 30), new DateOnly(2019, 8, 31) },
                    { "uq", 2, 1441, new DateOnly(2019, 10, 29), new DateOnly(2019, 9, 30) },
                    { "uq", 3, 1441, new DateOnly(2019, 11, 28), new DateOnly(2019, 10, 29) },
                    { "uq", 4, 1441, new DateOnly(2019, 12, 27), new DateOnly(2019, 11, 28) },
                    { "uq", 5, 1441, new DateOnly(2020, 1, 26), new DateOnly(2019, 12, 27) },
                    { "uq", 6, 1441, new DateOnly(2020, 2, 25), new DateOnly(2020, 1, 26) },
                    { "uq", 7, 1441, new DateOnly(2020, 3, 25), new DateOnly(2020, 2, 25) },
                    { "uq", 8, 1441, new DateOnly(2020, 4, 24), new DateOnly(2020, 3, 25) },
                    { "uq", 9, 1441, new DateOnly(2020, 5, 24), new DateOnly(2020, 4, 24) },
                    { "uq", 10, 1441, new DateOnly(2020, 6, 22), new DateOnly(2020, 5, 24) },
                    { "uq", 11, 1441, new DateOnly(2020, 7, 22), new DateOnly(2020, 6, 22) },
                    { "uq", 12, 1441, new DateOnly(2020, 8, 20), new DateOnly(2020, 7, 22) },
                    { "uq", 1, 1442, new DateOnly(2020, 9, 18), new DateOnly(2020, 8, 20) },
                    { "uq", 2, 1442, new DateOnly(2020, 10, 18), new DateOnly(2020, 9, 18) },
                    { "uq", 3, 1442, new DateOnly(2020, 11, 16), new DateOnly(2020, 10, 18) },
                    { "uq", 4, 1442, new DateOnly(2020, 12, 16), new DateOnly(2020, 11, 16) },
                    { "uq", 5, 1442, new DateOnly(2021, 1, 14), new DateOnly(2020, 12, 16) },
                    { "uq", 6, 1442, new DateOnly(2021, 2, 13), new DateOnly(2021, 1, 14) },
                    { "uq", 7, 1442, new DateOnly(2021, 3, 14), new DateOnly(2021, 2, 13) },
                    { "uq", 8, 1442, new DateOnly(2021, 4, 13), new DateOnly(2021, 3, 14) },
                    { "uq", 9, 1442, new DateOnly(2021, 5, 13), new DateOnly(2021, 4, 13) },
                    { "uq", 10, 1442, new DateOnly(2021, 6, 11), new DateOnly(2021, 5, 13) },
                    { "uq", 11, 1442, new DateOnly(2021, 7, 11), new DateOnly(2021, 6, 11) },
                    { "uq", 12, 1442, new DateOnly(2021, 8, 9), new DateOnly(2021, 7, 11) },
                    { "uq", 1, 1443, new DateOnly(2021, 9, 8), new DateOnly(2021, 8, 9) },
                    { "uq", 2, 1443, new DateOnly(2021, 10, 7), new DateOnly(2021, 9, 8) },
                    { "uq", 3, 1443, new DateOnly(2021, 11, 6), new DateOnly(2021, 10, 7) },
                    { "uq", 4, 1443, new DateOnly(2021, 12, 5), new DateOnly(2021, 11, 6) },
                    { "uq", 5, 1443, new DateOnly(2022, 1, 4), new DateOnly(2021, 12, 5) },
                    { "uq", 6, 1443, new DateOnly(2022, 2, 2), new DateOnly(2022, 1, 4) },
                    { "uq", 7, 1443, new DateOnly(2022, 3, 4), new DateOnly(2022, 2, 2) },
                    { "uq", 8, 1443, new DateOnly(2022, 4, 2), new DateOnly(2022, 3, 4) },
                    { "uq", 9, 1443, new DateOnly(2022, 5, 2), new DateOnly(2022, 4, 2) },
                    { "uq", 10, 1443, new DateOnly(2022, 5, 31), new DateOnly(2022, 5, 2) },
                    { "uq", 11, 1443, new DateOnly(2022, 6, 30), new DateOnly(2022, 5, 31) },
                    { "uq", 12, 1443, new DateOnly(2022, 7, 30), new DateOnly(2022, 6, 30) },
                    { "uq", 1, 1444, new DateOnly(2022, 8, 28), new DateOnly(2022, 7, 30) },
                    { "uq", 2, 1444, new DateOnly(2022, 9, 27), new DateOnly(2022, 8, 28) },
                    { "uq", 3, 1444, new DateOnly(2022, 10, 26), new DateOnly(2022, 9, 27) },
                    { "uq", 4, 1444, new DateOnly(2022, 11, 25), new DateOnly(2022, 10, 26) },
                    { "uq", 5, 1444, new DateOnly(2022, 12, 25), new DateOnly(2022, 11, 25) },
                    { "uq", 6, 1444, new DateOnly(2023, 1, 23), new DateOnly(2022, 12, 25) },
                    { "uq", 7, 1444, new DateOnly(2023, 2, 21), new DateOnly(2023, 1, 23) },
                    { "uq", 8, 1444, new DateOnly(2023, 3, 23), new DateOnly(2023, 2, 21) },
                    { "uq", 9, 1444, new DateOnly(2023, 4, 21), new DateOnly(2023, 3, 23) },
                    { "uq", 10, 1444, new DateOnly(2023, 5, 21), new DateOnly(2023, 4, 21) },
                    { "uq", 11, 1444, new DateOnly(2023, 6, 19), new DateOnly(2023, 5, 21) },
                    { "uq", 12, 1444, new DateOnly(2023, 7, 19), new DateOnly(2023, 6, 19) },
                    { "uq", 1, 1445, new DateOnly(2023, 8, 17), new DateOnly(2023, 7, 19) },
                    { "uq", 2, 1445, new DateOnly(2023, 9, 16), new DateOnly(2023, 8, 17) },
                    { "uq", 3, 1445, new DateOnly(2023, 10, 16), new DateOnly(2023, 9, 16) },
                    { "uq", 4, 1445, new DateOnly(2023, 11, 15), new DateOnly(2023, 10, 16) },
                    { "uq", 5, 1445, new DateOnly(2023, 12, 14), new DateOnly(2023, 11, 15) },
                    { "uq", 6, 1445, new DateOnly(2024, 1, 13), new DateOnly(2023, 12, 14) },
                    { "uq", 7, 1445, new DateOnly(2024, 2, 11), new DateOnly(2024, 1, 13) },
                    { "uq", 8, 1445, new DateOnly(2024, 3, 11), new DateOnly(2024, 2, 11) },
                    { "uq", 9, 1445, new DateOnly(2024, 4, 10), new DateOnly(2024, 3, 11) },
                    { "uq", 10, 1445, new DateOnly(2024, 5, 9), new DateOnly(2024, 4, 10) },
                    { "uq", 11, 1445, new DateOnly(2024, 6, 7), new DateOnly(2024, 5, 9) },
                    { "uq", 12, 1445, new DateOnly(2024, 7, 7), new DateOnly(2024, 6, 7) },
                    { "uq", 1, 1446, new DateOnly(2024, 8, 5), new DateOnly(2024, 7, 7) },
                    { "uq", 2, 1446, new DateOnly(2024, 9, 4), new DateOnly(2024, 8, 5) },
                    { "uq", 3, 1446, new DateOnly(2024, 10, 4), new DateOnly(2024, 9, 4) },
                    { "uq", 4, 1446, new DateOnly(2024, 11, 3), new DateOnly(2024, 10, 4) },
                    { "uq", 5, 1446, new DateOnly(2024, 12, 2), new DateOnly(2024, 11, 3) },
                    { "uq", 6, 1446, new DateOnly(2025, 1, 1), new DateOnly(2024, 12, 2) },
                    { "uq", 7, 1446, new DateOnly(2025, 1, 31), new DateOnly(2025, 1, 1) },
                    { "uq", 8, 1446, new DateOnly(2025, 3, 1), new DateOnly(2025, 1, 31) },
                    { "uq", 9, 1446, new DateOnly(2025, 3, 30), new DateOnly(2025, 3, 1) },
                    { "uq", 10, 1446, new DateOnly(2025, 4, 29), new DateOnly(2025, 3, 30) },
                    { "uq", 11, 1446, new DateOnly(2025, 5, 28), new DateOnly(2025, 4, 29) },
                    { "uq", 12, 1446, new DateOnly(2025, 6, 26), new DateOnly(2025, 5, 28) },
                    { "uq", 1, 1447, new DateOnly(2025, 7, 26), new DateOnly(2025, 6, 26) },
                    { "uq", 2, 1447, new DateOnly(2025, 8, 24), new DateOnly(2025, 7, 26) },
                    { "uq", 3, 1447, new DateOnly(2025, 9, 23), new DateOnly(2025, 8, 24) },
                    { "uq", 4, 1447, new DateOnly(2025, 10, 23), new DateOnly(2025, 9, 23) },
                    { "uq", 5, 1447, new DateOnly(2025, 11, 22), new DateOnly(2025, 10, 23) },
                    { "uq", 6, 1447, new DateOnly(2025, 12, 21), new DateOnly(2025, 11, 22) },
                    { "uq", 7, 1447, new DateOnly(2026, 1, 20), new DateOnly(2025, 12, 21) },
                    { "uq", 8, 1447, new DateOnly(2026, 2, 18), new DateOnly(2026, 1, 20) },
                    { "uq", 9, 1447, new DateOnly(2026, 3, 20), new DateOnly(2026, 2, 18) },
                    { "uq", 10, 1447, new DateOnly(2026, 4, 18), new DateOnly(2026, 3, 20) },
                    { "uq", 11, 1447, new DateOnly(2026, 5, 18), new DateOnly(2026, 4, 18) },
                    { "uq", 12, 1447, new DateOnly(2026, 6, 16), new DateOnly(2026, 5, 18) },
                    { "uq", 1, 1448, new DateOnly(2026, 7, 15), new DateOnly(2026, 6, 16) },
                    { "uq", 2, 1448, new DateOnly(2026, 8, 14), new DateOnly(2026, 7, 15) },
                    { "uq", 3, 1448, new DateOnly(2026, 9, 12), new DateOnly(2026, 8, 14) },
                    { "uq", 4, 1448, new DateOnly(2026, 10, 12), new DateOnly(2026, 9, 12) },
                    { "uq", 5, 1448, new DateOnly(2026, 11, 11), new DateOnly(2026, 10, 12) },
                    { "uq", 6, 1448, new DateOnly(2026, 12, 10), new DateOnly(2026, 11, 11) },
                    { "uq", 7, 1448, new DateOnly(2027, 1, 9), new DateOnly(2026, 12, 10) },
                    { "uq", 8, 1448, new DateOnly(2027, 2, 8), new DateOnly(2027, 1, 9) },
                    { "uq", 9, 1448, new DateOnly(2027, 3, 9), new DateOnly(2027, 2, 8) },
                    { "uq", 10, 1448, new DateOnly(2027, 4, 8), new DateOnly(2027, 3, 9) },
                    { "uq", 11, 1448, new DateOnly(2027, 5, 7), new DateOnly(2027, 4, 8) },
                    { "uq", 12, 1448, new DateOnly(2027, 6, 6), new DateOnly(2027, 5, 7) },
                    { "uq", 1, 1449, new DateOnly(2027, 7, 5), new DateOnly(2027, 6, 6) },
                    { "uq", 2, 1449, new DateOnly(2027, 8, 3), new DateOnly(2027, 7, 5) },
                    { "uq", 3, 1449, new DateOnly(2027, 9, 2), new DateOnly(2027, 8, 3) },
                    { "uq", 4, 1449, new DateOnly(2027, 10, 1), new DateOnly(2027, 9, 2) },
                    { "uq", 5, 1449, new DateOnly(2027, 10, 31), new DateOnly(2027, 10, 1) },
                    { "uq", 6, 1449, new DateOnly(2027, 11, 29), new DateOnly(2027, 10, 31) },
                    { "uq", 7, 1449, new DateOnly(2027, 12, 29), new DateOnly(2027, 11, 29) },
                    { "uq", 8, 1449, new DateOnly(2028, 1, 28), new DateOnly(2027, 12, 29) },
                    { "uq", 9, 1449, new DateOnly(2028, 2, 26), new DateOnly(2028, 1, 28) },
                    { "uq", 10, 1449, new DateOnly(2028, 3, 27), new DateOnly(2028, 2, 26) },
                    { "uq", 11, 1449, new DateOnly(2028, 4, 26), new DateOnly(2028, 3, 27) },
                    { "uq", 12, 1449, new DateOnly(2028, 5, 25), new DateOnly(2028, 4, 26) },
                    { "uq", 1, 1450, new DateOnly(2028, 6, 24), new DateOnly(2028, 5, 25) },
                    { "uq", 2, 1450, new DateOnly(2028, 7, 23), new DateOnly(2028, 6, 24) },
                    { "uq", 3, 1450, new DateOnly(2028, 8, 22), new DateOnly(2028, 7, 23) },
                    { "uq", 4, 1450, new DateOnly(2028, 9, 20), new DateOnly(2028, 8, 22) },
                    { "uq", 5, 1450, new DateOnly(2028, 10, 19), new DateOnly(2028, 9, 20) },
                    { "uq", 6, 1450, new DateOnly(2028, 11, 18), new DateOnly(2028, 10, 19) },
                    { "uq", 7, 1450, new DateOnly(2028, 12, 17), new DateOnly(2028, 11, 18) },
                    { "uq", 8, 1450, new DateOnly(2029, 1, 16), new DateOnly(2028, 12, 17) },
                    { "uq", 9, 1450, new DateOnly(2029, 2, 14), new DateOnly(2029, 1, 16) },
                    { "uq", 10, 1450, new DateOnly(2029, 3, 16), new DateOnly(2029, 2, 14) },
                    { "uq", 11, 1450, new DateOnly(2029, 4, 15), new DateOnly(2029, 3, 16) },
                    { "uq", 12, 1450, new DateOnly(2029, 5, 14), new DateOnly(2029, 4, 15) },
                    { "uq", 1, 1451, new DateOnly(2029, 6, 13), new DateOnly(2029, 5, 14) },
                    { "uq", 2, 1451, new DateOnly(2029, 7, 13), new DateOnly(2029, 6, 13) },
                    { "uq", 3, 1451, new DateOnly(2029, 8, 11), new DateOnly(2029, 7, 13) },
                    { "uq", 4, 1451, new DateOnly(2029, 9, 10), new DateOnly(2029, 8, 11) },
                    { "uq", 5, 1451, new DateOnly(2029, 10, 9), new DateOnly(2029, 9, 10) },
                    { "uq", 6, 1451, new DateOnly(2029, 11, 7), new DateOnly(2029, 10, 9) },
                    { "uq", 7, 1451, new DateOnly(2029, 12, 7), new DateOnly(2029, 11, 7) },
                    { "uq", 8, 1451, new DateOnly(2030, 1, 5), new DateOnly(2029, 12, 7) },
                    { "uq", 9, 1451, new DateOnly(2030, 2, 4), new DateOnly(2030, 1, 5) },
                    { "uq", 10, 1451, new DateOnly(2030, 3, 5), new DateOnly(2030, 2, 4) },
                    { "uq", 11, 1451, new DateOnly(2030, 4, 4), new DateOnly(2030, 3, 5) },
                    { "uq", 12, 1451, new DateOnly(2030, 5, 3), new DateOnly(2030, 4, 4) },
                    { "uq", 1, 1452, new DateOnly(2030, 6, 2), new DateOnly(2030, 5, 3) },
                    { "uq", 2, 1452, new DateOnly(2030, 7, 2), new DateOnly(2030, 6, 2) },
                    { "uq", 3, 1452, new DateOnly(2030, 8, 1), new DateOnly(2030, 7, 2) },
                    { "uq", 4, 1452, new DateOnly(2030, 8, 30), new DateOnly(2030, 8, 1) },
                    { "uq", 5, 1452, new DateOnly(2030, 9, 29), new DateOnly(2030, 8, 30) },
                    { "uq", 6, 1452, new DateOnly(2030, 10, 28), new DateOnly(2030, 9, 29) },
                    { "uq", 7, 1452, new DateOnly(2030, 11, 26), new DateOnly(2030, 10, 28) },
                    { "uq", 8, 1452, new DateOnly(2030, 12, 26), new DateOnly(2030, 11, 26) },
                    { "uq", 9, 1452, new DateOnly(2031, 1, 24), new DateOnly(2030, 12, 26) },
                    { "uq", 10, 1452, new DateOnly(2031, 2, 23), new DateOnly(2031, 1, 24) },
                    { "uq", 11, 1452, new DateOnly(2031, 3, 24), new DateOnly(2031, 2, 23) },
                    { "uq", 12, 1452, new DateOnly(2031, 4, 23), new DateOnly(2031, 3, 24) },
                    { "uq", 1, 1453, new DateOnly(2031, 5, 22), new DateOnly(2031, 4, 23) },
                    { "uq", 2, 1453, new DateOnly(2031, 6, 21), new DateOnly(2031, 5, 22) },
                    { "uq", 3, 1453, new DateOnly(2031, 7, 21), new DateOnly(2031, 6, 21) },
                    { "uq", 4, 1453, new DateOnly(2031, 8, 20), new DateOnly(2031, 7, 21) },
                    { "uq", 5, 1453, new DateOnly(2031, 9, 18), new DateOnly(2031, 8, 20) },
                    { "uq", 6, 1453, new DateOnly(2031, 10, 17), new DateOnly(2031, 9, 18) },
                    { "uq", 7, 1453, new DateOnly(2031, 11, 16), new DateOnly(2031, 10, 17) },
                    { "uq", 8, 1453, new DateOnly(2031, 12, 15), new DateOnly(2031, 11, 16) },
                    { "uq", 9, 1453, new DateOnly(2032, 1, 14), new DateOnly(2031, 12, 15) },
                    { "uq", 10, 1453, new DateOnly(2032, 2, 12), new DateOnly(2032, 1, 14) },
                    { "uq", 11, 1453, new DateOnly(2032, 3, 13), new DateOnly(2032, 2, 12) },
                    { "uq", 12, 1453, new DateOnly(2032, 4, 11), new DateOnly(2032, 3, 13) },
                    { "uq", 1, 1454, new DateOnly(2032, 5, 10), new DateOnly(2032, 4, 11) },
                    { "uq", 2, 1454, new DateOnly(2032, 6, 9), new DateOnly(2032, 5, 10) },
                    { "uq", 3, 1454, new DateOnly(2032, 7, 9), new DateOnly(2032, 6, 9) },
                    { "uq", 4, 1454, new DateOnly(2032, 8, 8), new DateOnly(2032, 7, 9) },
                    { "uq", 5, 1454, new DateOnly(2032, 9, 6), new DateOnly(2032, 8, 8) },
                    { "uq", 6, 1454, new DateOnly(2032, 10, 6), new DateOnly(2032, 9, 6) },
                    { "uq", 7, 1454, new DateOnly(2032, 11, 4), new DateOnly(2032, 10, 6) },
                    { "uq", 8, 1454, new DateOnly(2032, 12, 4), new DateOnly(2032, 11, 4) },
                    { "uq", 9, 1454, new DateOnly(2033, 1, 2), new DateOnly(2032, 12, 4) },
                    { "uq", 10, 1454, new DateOnly(2033, 2, 1), new DateOnly(2033, 1, 2) },
                    { "uq", 11, 1454, new DateOnly(2033, 3, 2), new DateOnly(2033, 2, 1) },
                    { "uq", 12, 1454, new DateOnly(2033, 4, 1), new DateOnly(2033, 3, 2) },
                    { "uq", 1, 1455, new DateOnly(2033, 4, 30), new DateOnly(2033, 4, 1) },
                    { "uq", 2, 1455, new DateOnly(2033, 5, 29), new DateOnly(2033, 4, 30) },
                    { "uq", 3, 1455, new DateOnly(2033, 6, 28), new DateOnly(2033, 5, 29) },
                    { "uq", 4, 1455, new DateOnly(2033, 7, 28), new DateOnly(2033, 6, 28) },
                    { "uq", 5, 1455, new DateOnly(2033, 8, 26), new DateOnly(2033, 7, 28) },
                    { "uq", 6, 1455, new DateOnly(2033, 9, 25), new DateOnly(2033, 8, 26) },
                    { "uq", 7, 1455, new DateOnly(2033, 10, 24), new DateOnly(2033, 9, 25) },
                    { "uq", 8, 1455, new DateOnly(2033, 11, 23), new DateOnly(2033, 10, 24) },
                    { "uq", 9, 1455, new DateOnly(2033, 12, 23), new DateOnly(2033, 11, 23) },
                    { "uq", 10, 1455, new DateOnly(2034, 1, 21), new DateOnly(2033, 12, 23) },
                    { "uq", 11, 1455, new DateOnly(2034, 2, 20), new DateOnly(2034, 1, 21) },
                    { "uq", 12, 1455, new DateOnly(2034, 3, 21), new DateOnly(2034, 2, 20) },
                    { "uq", 1, 1456, new DateOnly(2034, 4, 20), new DateOnly(2034, 3, 21) },
                    { "uq", 2, 1456, new DateOnly(2034, 5, 19), new DateOnly(2034, 4, 20) },
                    { "uq", 3, 1456, new DateOnly(2034, 6, 17), new DateOnly(2034, 5, 19) },
                    { "uq", 4, 1456, new DateOnly(2034, 7, 17), new DateOnly(2034, 6, 17) },
                    { "uq", 5, 1456, new DateOnly(2034, 8, 15), new DateOnly(2034, 7, 17) },
                    { "uq", 6, 1456, new DateOnly(2034, 9, 14), new DateOnly(2034, 8, 15) },
                    { "uq", 7, 1456, new DateOnly(2034, 10, 13), new DateOnly(2034, 9, 14) },
                    { "uq", 8, 1456, new DateOnly(2034, 11, 12), new DateOnly(2034, 10, 13) },
                    { "uq", 9, 1456, new DateOnly(2034, 12, 12), new DateOnly(2034, 11, 12) },
                    { "uq", 10, 1456, new DateOnly(2035, 1, 11), new DateOnly(2034, 12, 12) },
                    { "uq", 11, 1456, new DateOnly(2035, 2, 9), new DateOnly(2035, 1, 11) },
                    { "uq", 12, 1456, new DateOnly(2035, 3, 11), new DateOnly(2035, 2, 9) },
                    { "uq", 1, 1457, new DateOnly(2035, 4, 9), new DateOnly(2035, 3, 11) },
                    { "uq", 2, 1457, new DateOnly(2035, 5, 9), new DateOnly(2035, 4, 9) },
                    { "uq", 3, 1457, new DateOnly(2035, 6, 7), new DateOnly(2035, 5, 9) },
                    { "uq", 4, 1457, new DateOnly(2035, 7, 6), new DateOnly(2035, 6, 7) },
                    { "uq", 5, 1457, new DateOnly(2035, 8, 5), new DateOnly(2035, 7, 6) },
                    { "uq", 6, 1457, new DateOnly(2035, 9, 3), new DateOnly(2035, 8, 5) },
                    { "uq", 7, 1457, new DateOnly(2035, 10, 2), new DateOnly(2035, 9, 3) },
                    { "uq", 8, 1457, new DateOnly(2035, 11, 1), new DateOnly(2035, 10, 2) },
                    { "uq", 9, 1457, new DateOnly(2035, 12, 1), new DateOnly(2035, 11, 1) },
                    { "uq", 10, 1457, new DateOnly(2035, 12, 30), new DateOnly(2035, 12, 1) },
                    { "uq", 11, 1457, new DateOnly(2036, 1, 29), new DateOnly(2035, 12, 30) },
                    { "uq", 12, 1457, new DateOnly(2036, 2, 28), new DateOnly(2036, 1, 29) },
                    { "uq", 1, 1458, new DateOnly(2036, 3, 29), new DateOnly(2036, 2, 28) },
                    { "uq", 2, 1458, new DateOnly(2036, 4, 27), new DateOnly(2036, 3, 29) },
                    { "uq", 3, 1458, new DateOnly(2036, 5, 27), new DateOnly(2036, 4, 27) },
                    { "uq", 4, 1458, new DateOnly(2036, 6, 25), new DateOnly(2036, 5, 27) },
                    { "uq", 5, 1458, new DateOnly(2036, 7, 24), new DateOnly(2036, 6, 25) },
                    { "uq", 6, 1458, new DateOnly(2036, 8, 23), new DateOnly(2036, 7, 24) },
                    { "uq", 7, 1458, new DateOnly(2036, 9, 21), new DateOnly(2036, 8, 23) },
                    { "uq", 8, 1458, new DateOnly(2036, 10, 20), new DateOnly(2036, 9, 21) },
                    { "uq", 9, 1458, new DateOnly(2036, 11, 19), new DateOnly(2036, 10, 20) },
                    { "uq", 10, 1458, new DateOnly(2036, 12, 19), new DateOnly(2036, 11, 19) },
                    { "uq", 11, 1458, new DateOnly(2037, 1, 17), new DateOnly(2036, 12, 19) },
                    { "uq", 12, 1458, new DateOnly(2037, 2, 16), new DateOnly(2037, 1, 17) },
                    { "uq", 1, 1459, new DateOnly(2037, 3, 18), new DateOnly(2037, 2, 16) },
                    { "uq", 2, 1459, new DateOnly(2037, 4, 17), new DateOnly(2037, 3, 18) },
                    { "uq", 3, 1459, new DateOnly(2037, 5, 16), new DateOnly(2037, 4, 17) },
                    { "uq", 4, 1459, new DateOnly(2037, 6, 15), new DateOnly(2037, 5, 16) },
                    { "uq", 5, 1459, new DateOnly(2037, 7, 14), new DateOnly(2037, 6, 15) },
                    { "uq", 6, 1459, new DateOnly(2037, 8, 12), new DateOnly(2037, 7, 14) },
                    { "uq", 7, 1459, new DateOnly(2037, 9, 11), new DateOnly(2037, 8, 12) },
                    { "uq", 8, 1459, new DateOnly(2037, 10, 10), new DateOnly(2037, 9, 11) },
                    { "uq", 9, 1459, new DateOnly(2037, 11, 8), new DateOnly(2037, 10, 10) },
                    { "uq", 10, 1459, new DateOnly(2037, 12, 8), new DateOnly(2037, 11, 8) },
                    { "uq", 11, 1459, new DateOnly(2038, 1, 7), new DateOnly(2037, 12, 8) },
                    { "uq", 12, 1459, new DateOnly(2038, 2, 5), new DateOnly(2038, 1, 7) },
                    { "uq", 1, 1460, new DateOnly(2038, 3, 7), new DateOnly(2038, 2, 5) },
                    { "uq", 2, 1460, new DateOnly(2038, 4, 6), new DateOnly(2038, 3, 7) },
                    { "uq", 3, 1460, new DateOnly(2038, 5, 5), new DateOnly(2038, 4, 6) },
                    { "uq", 4, 1460, new DateOnly(2038, 6, 4), new DateOnly(2038, 5, 5) },
                    { "uq", 5, 1460, new DateOnly(2038, 7, 3), new DateOnly(2038, 6, 4) },
                    { "uq", 6, 1460, new DateOnly(2038, 8, 2), new DateOnly(2038, 7, 3) },
                    { "uq", 7, 1460, new DateOnly(2038, 8, 31), new DateOnly(2038, 8, 2) },
                    { "uq", 8, 1460, new DateOnly(2038, 9, 30), new DateOnly(2038, 8, 31) },
                    { "uq", 9, 1460, new DateOnly(2038, 10, 29), new DateOnly(2038, 9, 30) },
                    { "uq", 10, 1460, new DateOnly(2038, 11, 27), new DateOnly(2038, 10, 29) },
                    { "uq", 11, 1460, new DateOnly(2038, 12, 27), new DateOnly(2038, 11, 27) },
                    { "uq", 12, 1460, new DateOnly(2039, 1, 26), new DateOnly(2038, 12, 27) },
                    { "uq", 1, 1461, new DateOnly(2039, 2, 24), new DateOnly(2039, 1, 26) },
                    { "uq", 2, 1461, new DateOnly(2039, 3, 26), new DateOnly(2039, 2, 24) },
                    { "uq", 3, 1461, new DateOnly(2039, 4, 24), new DateOnly(2039, 3, 26) },
                    { "uq", 4, 1461, new DateOnly(2039, 5, 24), new DateOnly(2039, 4, 24) },
                    { "uq", 5, 1461, new DateOnly(2039, 6, 23), new DateOnly(2039, 5, 24) },
                    { "uq", 6, 1461, new DateOnly(2039, 7, 22), new DateOnly(2039, 6, 23) },
                    { "uq", 7, 1461, new DateOnly(2039, 8, 21), new DateOnly(2039, 7, 22) },
                    { "uq", 8, 1461, new DateOnly(2039, 9, 19), new DateOnly(2039, 8, 21) },
                    { "uq", 9, 1461, new DateOnly(2039, 10, 19), new DateOnly(2039, 9, 19) },
                    { "uq", 10, 1461, new DateOnly(2039, 11, 17), new DateOnly(2039, 10, 19) },
                    { "uq", 11, 1461, new DateOnly(2039, 12, 17), new DateOnly(2039, 11, 17) },
                    { "uq", 12, 1461, new DateOnly(2040, 1, 15), new DateOnly(2039, 12, 17) },
                    { "uq", 1, 1462, new DateOnly(2040, 2, 14), new DateOnly(2040, 1, 15) },
                    { "uq", 2, 1462, new DateOnly(2040, 3, 14), new DateOnly(2040, 2, 14) },
                    { "uq", 3, 1462, new DateOnly(2040, 4, 13), new DateOnly(2040, 3, 14) },
                    { "uq", 4, 1462, new DateOnly(2040, 5, 12), new DateOnly(2040, 4, 13) },
                    { "uq", 5, 1462, new DateOnly(2040, 6, 11), new DateOnly(2040, 5, 12) },
                    { "uq", 6, 1462, new DateOnly(2040, 7, 10), new DateOnly(2040, 6, 11) },
                    { "uq", 7, 1462, new DateOnly(2040, 8, 9), new DateOnly(2040, 7, 10) },
                    { "uq", 8, 1462, new DateOnly(2040, 9, 7), new DateOnly(2040, 8, 9) },
                    { "uq", 9, 1462, new DateOnly(2040, 10, 7), new DateOnly(2040, 9, 7) },
                    { "uq", 10, 1462, new DateOnly(2040, 11, 6), new DateOnly(2040, 10, 7) },
                    { "uq", 11, 1462, new DateOnly(2040, 12, 5), new DateOnly(2040, 11, 6) },
                    { "uq", 12, 1462, new DateOnly(2041, 1, 4), new DateOnly(2040, 12, 5) },
                    { "uq", 1, 1463, new DateOnly(2041, 2, 2), new DateOnly(2041, 1, 4) },
                    { "uq", 2, 1463, new DateOnly(2041, 3, 4), new DateOnly(2041, 2, 2) },
                    { "uq", 3, 1463, new DateOnly(2041, 4, 2), new DateOnly(2041, 3, 4) },
                    { "uq", 4, 1463, new DateOnly(2041, 5, 1), new DateOnly(2041, 4, 2) },
                    { "uq", 5, 1463, new DateOnly(2041, 5, 31), new DateOnly(2041, 5, 1) },
                    { "uq", 6, 1463, new DateOnly(2041, 6, 29), new DateOnly(2041, 5, 31) },
                    { "uq", 7, 1463, new DateOnly(2041, 7, 29), new DateOnly(2041, 6, 29) },
                    { "uq", 8, 1463, new DateOnly(2041, 8, 28), new DateOnly(2041, 7, 29) },
                    { "uq", 9, 1463, new DateOnly(2041, 9, 26), new DateOnly(2041, 8, 28) },
                    { "uq", 10, 1463, new DateOnly(2041, 10, 26), new DateOnly(2041, 9, 26) },
                    { "uq", 11, 1463, new DateOnly(2041, 11, 25), new DateOnly(2041, 10, 26) },
                    { "uq", 12, 1463, new DateOnly(2041, 12, 24), new DateOnly(2041, 11, 25) },
                    { "uq", 1, 1464, new DateOnly(2042, 1, 23), new DateOnly(2041, 12, 24) },
                    { "uq", 2, 1464, new DateOnly(2042, 2, 21), new DateOnly(2042, 1, 23) },
                    { "uq", 3, 1464, new DateOnly(2042, 3, 23), new DateOnly(2042, 2, 21) },
                    { "uq", 4, 1464, new DateOnly(2042, 4, 21), new DateOnly(2042, 3, 23) },
                    { "uq", 5, 1464, new DateOnly(2042, 5, 20), new DateOnly(2042, 4, 21) },
                    { "uq", 6, 1464, new DateOnly(2042, 6, 19), new DateOnly(2042, 5, 20) },
                    { "uq", 7, 1464, new DateOnly(2042, 7, 18), new DateOnly(2042, 6, 19) },
                    { "uq", 8, 1464, new DateOnly(2042, 8, 17), new DateOnly(2042, 7, 18) },
                    { "uq", 9, 1464, new DateOnly(2042, 9, 15), new DateOnly(2042, 8, 17) },
                    { "uq", 10, 1464, new DateOnly(2042, 10, 15), new DateOnly(2042, 9, 15) },
                    { "uq", 11, 1464, new DateOnly(2042, 11, 14), new DateOnly(2042, 10, 15) },
                    { "uq", 12, 1464, new DateOnly(2042, 12, 14), new DateOnly(2042, 11, 14) },
                    { "uq", 1, 1465, new DateOnly(2043, 1, 12), new DateOnly(2042, 12, 14) },
                    { "uq", 2, 1465, new DateOnly(2043, 2, 11), new DateOnly(2043, 1, 12) },
                    { "uq", 3, 1465, new DateOnly(2043, 3, 12), new DateOnly(2043, 2, 11) },
                    { "uq", 4, 1465, new DateOnly(2043, 4, 11), new DateOnly(2043, 3, 12) },
                    { "uq", 5, 1465, new DateOnly(2043, 5, 10), new DateOnly(2043, 4, 11) },
                    { "uq", 6, 1465, new DateOnly(2043, 6, 8), new DateOnly(2043, 5, 10) },
                    { "uq", 7, 1465, new DateOnly(2043, 7, 8), new DateOnly(2043, 6, 8) },
                    { "uq", 8, 1465, new DateOnly(2043, 8, 6), new DateOnly(2043, 7, 8) },
                    { "uq", 9, 1465, new DateOnly(2043, 9, 4), new DateOnly(2043, 8, 6) },
                    { "uq", 10, 1465, new DateOnly(2043, 10, 4), new DateOnly(2043, 9, 4) },
                    { "uq", 11, 1465, new DateOnly(2043, 11, 3), new DateOnly(2043, 10, 4) },
                    { "uq", 12, 1465, new DateOnly(2043, 12, 3), new DateOnly(2043, 11, 3) },
                    { "uq", 1, 1466, new DateOnly(2044, 1, 2), new DateOnly(2043, 12, 3) },
                    { "uq", 2, 1466, new DateOnly(2044, 1, 31), new DateOnly(2044, 1, 2) },
                    { "uq", 3, 1466, new DateOnly(2044, 3, 1), new DateOnly(2044, 1, 31) },
                    { "uq", 4, 1466, new DateOnly(2044, 3, 30), new DateOnly(2044, 3, 1) },
                    { "uq", 5, 1466, new DateOnly(2044, 4, 29), new DateOnly(2044, 3, 30) },
                    { "uq", 6, 1466, new DateOnly(2044, 5, 28), new DateOnly(2044, 4, 29) },
                    { "uq", 7, 1466, new DateOnly(2044, 6, 26), new DateOnly(2044, 5, 28) },
                    { "uq", 8, 1466, new DateOnly(2044, 7, 26), new DateOnly(2044, 6, 26) },
                    { "uq", 9, 1466, new DateOnly(2044, 8, 24), new DateOnly(2044, 7, 26) },
                    { "uq", 10, 1466, new DateOnly(2044, 9, 23), new DateOnly(2044, 8, 24) },
                    { "uq", 11, 1466, new DateOnly(2044, 10, 22), new DateOnly(2044, 9, 23) },
                    { "uq", 12, 1466, new DateOnly(2044, 11, 21), new DateOnly(2044, 10, 22) },
                    { "uq", 1, 1467, new DateOnly(2044, 12, 21), new DateOnly(2044, 11, 21) },
                    { "uq", 2, 1467, new DateOnly(2045, 1, 19), new DateOnly(2044, 12, 21) },
                    { "uq", 3, 1467, new DateOnly(2045, 2, 18), new DateOnly(2045, 1, 19) },
                    { "uq", 4, 1467, new DateOnly(2045, 3, 20), new DateOnly(2045, 2, 18) },
                    { "uq", 5, 1467, new DateOnly(2045, 4, 18), new DateOnly(2045, 3, 20) },
                    { "uq", 6, 1467, new DateOnly(2045, 5, 18), new DateOnly(2045, 4, 18) },
                    { "uq", 7, 1467, new DateOnly(2045, 6, 16), new DateOnly(2045, 5, 18) },
                    { "uq", 8, 1467, new DateOnly(2045, 7, 15), new DateOnly(2045, 6, 16) },
                    { "uq", 9, 1467, new DateOnly(2045, 8, 14), new DateOnly(2045, 7, 15) },
                    { "uq", 10, 1467, new DateOnly(2045, 9, 12), new DateOnly(2045, 8, 14) },
                    { "uq", 11, 1467, new DateOnly(2045, 10, 12), new DateOnly(2045, 9, 12) },
                    { "uq", 12, 1467, new DateOnly(2045, 11, 10), new DateOnly(2045, 10, 12) },
                    { "uq", 1, 1468, new DateOnly(2045, 12, 10), new DateOnly(2045, 11, 10) },
                    { "uq", 2, 1468, new DateOnly(2046, 1, 8), new DateOnly(2045, 12, 10) },
                    { "uq", 3, 1468, new DateOnly(2046, 2, 7), new DateOnly(2046, 1, 8) },
                    { "uq", 4, 1468, new DateOnly(2046, 3, 9), new DateOnly(2046, 2, 7) },
                    { "uq", 5, 1468, new DateOnly(2046, 4, 7), new DateOnly(2046, 3, 9) },
                    { "uq", 6, 1468, new DateOnly(2046, 5, 7), new DateOnly(2046, 4, 7) },
                    { "uq", 7, 1468, new DateOnly(2046, 6, 5), new DateOnly(2046, 5, 7) },
                    { "uq", 8, 1468, new DateOnly(2046, 7, 5), new DateOnly(2046, 6, 5) },
                    { "uq", 9, 1468, new DateOnly(2046, 8, 3), new DateOnly(2046, 7, 5) },
                    { "uq", 10, 1468, new DateOnly(2046, 9, 2), new DateOnly(2046, 8, 3) },
                    { "uq", 11, 1468, new DateOnly(2046, 10, 1), new DateOnly(2046, 9, 2) },
                    { "uq", 12, 1468, new DateOnly(2046, 10, 31), new DateOnly(2046, 10, 1) },
                    { "uq", 1, 1469, new DateOnly(2046, 11, 29), new DateOnly(2046, 10, 31) },
                    { "uq", 2, 1469, new DateOnly(2046, 12, 28), new DateOnly(2046, 11, 29) },
                    { "uq", 3, 1469, new DateOnly(2047, 1, 27), new DateOnly(2046, 12, 28) },
                    { "uq", 4, 1469, new DateOnly(2047, 2, 26), new DateOnly(2047, 1, 27) },
                    { "uq", 5, 1469, new DateOnly(2047, 3, 27), new DateOnly(2047, 2, 26) },
                    { "uq", 6, 1469, new DateOnly(2047, 4, 26), new DateOnly(2047, 3, 27) },
                    { "uq", 7, 1469, new DateOnly(2047, 5, 26), new DateOnly(2047, 4, 26) },
                    { "uq", 8, 1469, new DateOnly(2047, 6, 24), new DateOnly(2047, 5, 26) },
                    { "uq", 9, 1469, new DateOnly(2047, 7, 24), new DateOnly(2047, 6, 24) },
                    { "uq", 10, 1469, new DateOnly(2047, 8, 23), new DateOnly(2047, 7, 24) },
                    { "uq", 11, 1469, new DateOnly(2047, 9, 21), new DateOnly(2047, 8, 23) },
                    { "uq", 12, 1469, new DateOnly(2047, 10, 20), new DateOnly(2047, 9, 21) },
                    { "uq", 1, 1470, new DateOnly(2047, 11, 19), new DateOnly(2047, 10, 20) },
                    { "uq", 2, 1470, new DateOnly(2047, 12, 18), new DateOnly(2047, 11, 19) },
                    { "uq", 3, 1470, new DateOnly(2048, 1, 16), new DateOnly(2047, 12, 18) },
                    { "uq", 4, 1470, new DateOnly(2048, 2, 15), new DateOnly(2048, 1, 16) },
                    { "uq", 5, 1470, new DateOnly(2048, 3, 16), new DateOnly(2048, 2, 15) },
                    { "uq", 6, 1470, new DateOnly(2048, 4, 14), new DateOnly(2048, 3, 16) },
                    { "uq", 7, 1470, new DateOnly(2048, 5, 14), new DateOnly(2048, 4, 14) },
                    { "uq", 8, 1470, new DateOnly(2048, 6, 12), new DateOnly(2048, 5, 14) },
                    { "uq", 9, 1470, new DateOnly(2048, 7, 12), new DateOnly(2048, 6, 12) },
                    { "uq", 10, 1470, new DateOnly(2048, 8, 11), new DateOnly(2048, 7, 12) },
                    { "uq", 11, 1470, new DateOnly(2048, 9, 10), new DateOnly(2048, 8, 11) },
                    { "uq", 12, 1470, new DateOnly(2048, 10, 9), new DateOnly(2048, 9, 10) },
                    { "uq", 1, 1471, new DateOnly(2048, 11, 7), new DateOnly(2048, 10, 9) },
                    { "uq", 2, 1471, new DateOnly(2048, 12, 7), new DateOnly(2048, 11, 7) },
                    { "uq", 3, 1471, new DateOnly(2049, 1, 5), new DateOnly(2048, 12, 7) },
                    { "uq", 4, 1471, new DateOnly(2049, 2, 3), new DateOnly(2049, 1, 5) },
                    { "uq", 5, 1471, new DateOnly(2049, 3, 5), new DateOnly(2049, 2, 3) },
                    { "uq", 6, 1471, new DateOnly(2049, 4, 3), new DateOnly(2049, 3, 5) },
                    { "uq", 7, 1471, new DateOnly(2049, 5, 3), new DateOnly(2049, 4, 3) },
                    { "uq", 8, 1471, new DateOnly(2049, 6, 2), new DateOnly(2049, 5, 3) },
                    { "uq", 9, 1471, new DateOnly(2049, 7, 1), new DateOnly(2049, 6, 2) },
                    { "uq", 10, 1471, new DateOnly(2049, 7, 31), new DateOnly(2049, 7, 1) },
                    { "uq", 11, 1471, new DateOnly(2049, 8, 30), new DateOnly(2049, 7, 31) },
                    { "uq", 12, 1471, new DateOnly(2049, 9, 28), new DateOnly(2049, 8, 30) },
                    { "uq", 1, 1472, new DateOnly(2049, 10, 28), new DateOnly(2049, 9, 28) },
                    { "uq", 2, 1472, new DateOnly(2049, 11, 26), new DateOnly(2049, 10, 28) },
                    { "uq", 3, 1472, new DateOnly(2049, 12, 26), new DateOnly(2049, 11, 26) },
                    { "uq", 4, 1472, new DateOnly(2050, 1, 24), new DateOnly(2049, 12, 26) },
                    { "uq", 5, 1472, new DateOnly(2050, 2, 23), new DateOnly(2050, 1, 24) },
                    { "uq", 6, 1472, new DateOnly(2050, 3, 24), new DateOnly(2050, 2, 23) },
                    { "uq", 7, 1472, new DateOnly(2050, 4, 22), new DateOnly(2050, 3, 24) },
                    { "uq", 8, 1472, new DateOnly(2050, 5, 22), new DateOnly(2050, 4, 22) },
                    { "uq", 9, 1472, new DateOnly(2050, 6, 20), new DateOnly(2050, 5, 22) },
                    { "uq", 10, 1472, new DateOnly(2050, 7, 20), new DateOnly(2050, 6, 20) },
                    { "uq", 11, 1472, new DateOnly(2050, 8, 19), new DateOnly(2050, 7, 20) },
                    { "uq", 12, 1472, new DateOnly(2050, 9, 17), new DateOnly(2050, 8, 19) },
                    { "uq", 1, 1473, new DateOnly(2050, 10, 17), new DateOnly(2050, 9, 17) },
                    { "uq", 2, 1473, new DateOnly(2050, 11, 15), new DateOnly(2050, 10, 17) },
                    { "uq", 3, 1473, new DateOnly(2050, 12, 15), new DateOnly(2050, 11, 15) },
                    { "uq", 4, 1473, new DateOnly(2051, 1, 14), new DateOnly(2050, 12, 15) },
                    { "uq", 5, 1473, new DateOnly(2051, 2, 12), new DateOnly(2051, 1, 14) },
                    { "uq", 6, 1473, new DateOnly(2051, 3, 14), new DateOnly(2051, 2, 12) },
                    { "uq", 7, 1473, new DateOnly(2051, 4, 12), new DateOnly(2051, 3, 14) },
                    { "uq", 8, 1473, new DateOnly(2051, 5, 11), new DateOnly(2051, 4, 12) },
                    { "uq", 9, 1473, new DateOnly(2051, 6, 10), new DateOnly(2051, 5, 11) },
                    { "uq", 10, 1473, new DateOnly(2051, 7, 9), new DateOnly(2051, 6, 10) },
                    { "uq", 11, 1473, new DateOnly(2051, 8, 8), new DateOnly(2051, 7, 9) },
                    { "uq", 12, 1473, new DateOnly(2051, 9, 6), new DateOnly(2051, 8, 8) },
                    { "uq", 1, 1474, new DateOnly(2051, 10, 6), new DateOnly(2051, 9, 6) },
                    { "uq", 2, 1474, new DateOnly(2051, 11, 5), new DateOnly(2051, 10, 6) },
                    { "uq", 3, 1474, new DateOnly(2051, 12, 4), new DateOnly(2051, 11, 5) },
                    { "uq", 4, 1474, new DateOnly(2052, 1, 3), new DateOnly(2051, 12, 4) },
                    { "uq", 5, 1474, new DateOnly(2052, 2, 2), new DateOnly(2052, 1, 3) },
                    { "uq", 6, 1474, new DateOnly(2052, 3, 2), new DateOnly(2052, 2, 2) },
                    { "uq", 7, 1474, new DateOnly(2052, 4, 1), new DateOnly(2052, 3, 2) },
                    { "uq", 8, 1474, new DateOnly(2052, 4, 30), new DateOnly(2052, 4, 1) },
                    { "uq", 9, 1474, new DateOnly(2052, 5, 29), new DateOnly(2052, 4, 30) },
                    { "uq", 10, 1474, new DateOnly(2052, 6, 28), new DateOnly(2052, 5, 29) },
                    { "uq", 11, 1474, new DateOnly(2052, 7, 27), new DateOnly(2052, 6, 28) },
                    { "uq", 12, 1474, new DateOnly(2052, 8, 26), new DateOnly(2052, 7, 27) },
                    { "uq", 1, 1475, new DateOnly(2052, 9, 24), new DateOnly(2052, 8, 26) },
                    { "uq", 2, 1475, new DateOnly(2052, 10, 24), new DateOnly(2052, 9, 24) },
                    { "uq", 3, 1475, new DateOnly(2052, 11, 22), new DateOnly(2052, 10, 24) },
                    { "uq", 4, 1475, new DateOnly(2052, 12, 22), new DateOnly(2052, 11, 22) },
                    { "uq", 5, 1475, new DateOnly(2053, 1, 21), new DateOnly(2052, 12, 22) },
                    { "uq", 6, 1475, new DateOnly(2053, 2, 20), new DateOnly(2053, 1, 21) },
                    { "uq", 7, 1475, new DateOnly(2053, 3, 21), new DateOnly(2053, 2, 20) },
                    { "uq", 8, 1475, new DateOnly(2053, 4, 20), new DateOnly(2053, 3, 21) },
                    { "uq", 9, 1475, new DateOnly(2053, 5, 19), new DateOnly(2053, 4, 20) },
                    { "uq", 10, 1475, new DateOnly(2053, 6, 17), new DateOnly(2053, 5, 19) },
                    { "uq", 11, 1475, new DateOnly(2053, 7, 17), new DateOnly(2053, 6, 17) },
                    { "uq", 12, 1475, new DateOnly(2053, 8, 15), new DateOnly(2053, 7, 17) },
                    { "uq", 1, 1476, new DateOnly(2053, 9, 13), new DateOnly(2053, 8, 15) },
                    { "uq", 2, 1476, new DateOnly(2053, 10, 13), new DateOnly(2053, 9, 13) },
                    { "uq", 3, 1476, new DateOnly(2053, 11, 11), new DateOnly(2053, 10, 13) },
                    { "uq", 4, 1476, new DateOnly(2053, 12, 11), new DateOnly(2053, 11, 11) },
                    { "uq", 5, 1476, new DateOnly(2054, 1, 10), new DateOnly(2053, 12, 11) },
                    { "uq", 6, 1476, new DateOnly(2054, 2, 9), new DateOnly(2054, 1, 10) },
                    { "uq", 7, 1476, new DateOnly(2054, 3, 10), new DateOnly(2054, 2, 9) },
                    { "uq", 8, 1476, new DateOnly(2054, 4, 9), new DateOnly(2054, 3, 10) },
                    { "uq", 9, 1476, new DateOnly(2054, 5, 9), new DateOnly(2054, 4, 9) },
                    { "uq", 10, 1476, new DateOnly(2054, 6, 7), new DateOnly(2054, 5, 9) },
                    { "uq", 11, 1476, new DateOnly(2054, 7, 6), new DateOnly(2054, 6, 7) },
                    { "uq", 12, 1476, new DateOnly(2054, 8, 5), new DateOnly(2054, 7, 6) },
                    { "uq", 1, 1477, new DateOnly(2054, 9, 3), new DateOnly(2054, 8, 5) },
                    { "uq", 2, 1477, new DateOnly(2054, 10, 2), new DateOnly(2054, 9, 3) },
                    { "uq", 3, 1477, new DateOnly(2054, 11, 1), new DateOnly(2054, 10, 2) },
                    { "uq", 4, 1477, new DateOnly(2054, 11, 30), new DateOnly(2054, 11, 1) },
                    { "uq", 5, 1477, new DateOnly(2054, 12, 30), new DateOnly(2054, 11, 30) },
                    { "uq", 6, 1477, new DateOnly(2055, 1, 29), new DateOnly(2054, 12, 30) },
                    { "uq", 7, 1477, new DateOnly(2055, 2, 27), new DateOnly(2055, 1, 29) },
                    { "uq", 8, 1477, new DateOnly(2055, 3, 29), new DateOnly(2055, 2, 27) },
                    { "uq", 9, 1477, new DateOnly(2055, 4, 28), new DateOnly(2055, 3, 29) },
                    { "uq", 10, 1477, new DateOnly(2055, 5, 28), new DateOnly(2055, 4, 28) },
                    { "uq", 11, 1477, new DateOnly(2055, 6, 26), new DateOnly(2055, 5, 28) },
                    { "uq", 12, 1477, new DateOnly(2055, 7, 25), new DateOnly(2055, 6, 26) },
                    { "uq", 1, 1478, new DateOnly(2055, 8, 24), new DateOnly(2055, 7, 25) },
                    { "uq", 2, 1478, new DateOnly(2055, 9, 22), new DateOnly(2055, 8, 24) },
                    { "uq", 3, 1478, new DateOnly(2055, 10, 21), new DateOnly(2055, 9, 22) },
                    { "uq", 4, 1478, new DateOnly(2055, 11, 20), new DateOnly(2055, 10, 21) },
                    { "uq", 5, 1478, new DateOnly(2055, 12, 19), new DateOnly(2055, 11, 20) },
                    { "uq", 6, 1478, new DateOnly(2056, 1, 18), new DateOnly(2055, 12, 19) },
                    { "uq", 7, 1478, new DateOnly(2056, 2, 17), new DateOnly(2056, 1, 18) },
                    { "uq", 8, 1478, new DateOnly(2056, 3, 17), new DateOnly(2056, 2, 17) },
                    { "uq", 9, 1478, new DateOnly(2056, 4, 16), new DateOnly(2056, 3, 17) },
                    { "uq", 10, 1478, new DateOnly(2056, 5, 16), new DateOnly(2056, 4, 16) },
                    { "uq", 11, 1478, new DateOnly(2056, 6, 14), new DateOnly(2056, 5, 16) },
                    { "uq", 12, 1478, new DateOnly(2056, 7, 14), new DateOnly(2056, 6, 14) },
                    { "uq", 1, 1479, new DateOnly(2056, 8, 12), new DateOnly(2056, 7, 14) },
                    { "uq", 2, 1479, new DateOnly(2056, 9, 11), new DateOnly(2056, 8, 12) },
                    { "uq", 3, 1479, new DateOnly(2056, 10, 10), new DateOnly(2056, 9, 11) },
                    { "uq", 4, 1479, new DateOnly(2056, 11, 8), new DateOnly(2056, 10, 10) },
                    { "uq", 5, 1479, new DateOnly(2056, 12, 8), new DateOnly(2056, 11, 8) },
                    { "uq", 6, 1479, new DateOnly(2057, 1, 6), new DateOnly(2056, 12, 8) },
                    { "uq", 7, 1479, new DateOnly(2057, 2, 5), new DateOnly(2057, 1, 6) },
                    { "uq", 8, 1479, new DateOnly(2057, 3, 6), new DateOnly(2057, 2, 5) },
                    { "uq", 9, 1479, new DateOnly(2057, 4, 5), new DateOnly(2057, 3, 6) },
                    { "uq", 10, 1479, new DateOnly(2057, 5, 5), new DateOnly(2057, 4, 5) },
                    { "uq", 11, 1479, new DateOnly(2057, 6, 3), new DateOnly(2057, 5, 5) },
                    { "uq", 12, 1479, new DateOnly(2057, 7, 3), new DateOnly(2057, 6, 3) },
                    { "uq", 1, 1480, new DateOnly(2057, 8, 1), new DateOnly(2057, 7, 3) },
                    { "uq", 2, 1480, new DateOnly(2057, 8, 31), new DateOnly(2057, 8, 1) },
                    { "uq", 3, 1480, new DateOnly(2057, 9, 30), new DateOnly(2057, 8, 31) },
                    { "uq", 4, 1480, new DateOnly(2057, 10, 29), new DateOnly(2057, 9, 30) },
                    { "uq", 5, 1480, new DateOnly(2057, 11, 27), new DateOnly(2057, 10, 29) },
                    { "uq", 6, 1480, new DateOnly(2057, 12, 27), new DateOnly(2057, 11, 27) },
                    { "uq", 7, 1480, new DateOnly(2058, 1, 25), new DateOnly(2057, 12, 27) },
                    { "uq", 8, 1480, new DateOnly(2058, 2, 24), new DateOnly(2058, 1, 25) },
                    { "uq", 9, 1480, new DateOnly(2058, 3, 25), new DateOnly(2058, 2, 24) },
                    { "uq", 10, 1480, new DateOnly(2058, 4, 24), new DateOnly(2058, 3, 25) },
                    { "uq", 11, 1480, new DateOnly(2058, 5, 23), new DateOnly(2058, 4, 24) },
                    { "uq", 12, 1480, new DateOnly(2058, 6, 22), new DateOnly(2058, 5, 23) },
                    { "uq", 1, 1481, new DateOnly(2058, 7, 21), new DateOnly(2058, 6, 22) },
                    { "uq", 2, 1481, new DateOnly(2058, 8, 20), new DateOnly(2058, 7, 21) },
                    { "uq", 3, 1481, new DateOnly(2058, 9, 19), new DateOnly(2058, 8, 20) },
                    { "uq", 4, 1481, new DateOnly(2058, 10, 18), new DateOnly(2058, 9, 19) },
                    { "uq", 5, 1481, new DateOnly(2058, 11, 17), new DateOnly(2058, 10, 18) },
                    { "uq", 6, 1481, new DateOnly(2058, 12, 17), new DateOnly(2058, 11, 17) },
                    { "uq", 7, 1481, new DateOnly(2059, 1, 15), new DateOnly(2058, 12, 17) },
                    { "uq", 8, 1481, new DateOnly(2059, 2, 14), new DateOnly(2059, 1, 15) },
                    { "uq", 9, 1481, new DateOnly(2059, 3, 15), new DateOnly(2059, 2, 14) },
                    { "uq", 10, 1481, new DateOnly(2059, 4, 13), new DateOnly(2059, 3, 15) },
                    { "uq", 11, 1481, new DateOnly(2059, 5, 13), new DateOnly(2059, 4, 13) },
                    { "uq", 12, 1481, new DateOnly(2059, 6, 11), new DateOnly(2059, 5, 13) },
                    { "uq", 1, 1482, new DateOnly(2059, 7, 11), new DateOnly(2059, 6, 11) },
                    { "uq", 2, 1482, new DateOnly(2059, 8, 9), new DateOnly(2059, 7, 11) },
                    { "uq", 3, 1482, new DateOnly(2059, 9, 8), new DateOnly(2059, 8, 9) },
                    { "uq", 4, 1482, new DateOnly(2059, 10, 8), new DateOnly(2059, 9, 8) },
                    { "uq", 5, 1482, new DateOnly(2059, 11, 6), new DateOnly(2059, 10, 8) },
                    { "uq", 6, 1482, new DateOnly(2059, 12, 6), new DateOnly(2059, 11, 6) },
                    { "uq", 7, 1482, new DateOnly(2060, 1, 5), new DateOnly(2059, 12, 6) },
                    { "uq", 8, 1482, new DateOnly(2060, 2, 3), new DateOnly(2060, 1, 5) },
                    { "uq", 9, 1482, new DateOnly(2060, 3, 4), new DateOnly(2060, 2, 3) },
                    { "uq", 10, 1482, new DateOnly(2060, 4, 2), new DateOnly(2060, 3, 4) },
                    { "uq", 11, 1482, new DateOnly(2060, 5, 1), new DateOnly(2060, 4, 2) },
                    { "uq", 12, 1482, new DateOnly(2060, 5, 31), new DateOnly(2060, 5, 1) },
                    { "uq", 1, 1483, new DateOnly(2060, 6, 29), new DateOnly(2060, 5, 31) },
                    { "uq", 2, 1483, new DateOnly(2060, 7, 28), new DateOnly(2060, 6, 29) },
                    { "uq", 3, 1483, new DateOnly(2060, 8, 27), new DateOnly(2060, 7, 28) },
                    { "uq", 4, 1483, new DateOnly(2060, 9, 26), new DateOnly(2060, 8, 27) },
                    { "uq", 5, 1483, new DateOnly(2060, 10, 25), new DateOnly(2060, 9, 26) },
                    { "uq", 6, 1483, new DateOnly(2060, 11, 24), new DateOnly(2060, 10, 25) },
                    { "uq", 7, 1483, new DateOnly(2060, 12, 24), new DateOnly(2060, 11, 24) },
                    { "uq", 8, 1483, new DateOnly(2061, 1, 23), new DateOnly(2060, 12, 24) },
                    { "uq", 9, 1483, new DateOnly(2061, 2, 21), new DateOnly(2061, 1, 23) },
                    { "uq", 10, 1483, new DateOnly(2061, 3, 23), new DateOnly(2061, 2, 21) },
                    { "uq", 11, 1483, new DateOnly(2061, 4, 21), new DateOnly(2061, 3, 23) },
                    { "uq", 12, 1483, new DateOnly(2061, 5, 20), new DateOnly(2061, 4, 21) },
                    { "uq", 1, 1484, new DateOnly(2061, 6, 19), new DateOnly(2061, 5, 20) },
                    { "uq", 2, 1484, new DateOnly(2061, 7, 18), new DateOnly(2061, 6, 19) },
                    { "uq", 3, 1484, new DateOnly(2061, 8, 16), new DateOnly(2061, 7, 18) },
                    { "uq", 4, 1484, new DateOnly(2061, 9, 15), new DateOnly(2061, 8, 16) },
                    { "uq", 5, 1484, new DateOnly(2061, 10, 15), new DateOnly(2061, 9, 15) },
                    { "uq", 6, 1484, new DateOnly(2061, 11, 13), new DateOnly(2061, 10, 15) },
                    { "uq", 7, 1484, new DateOnly(2061, 12, 13), new DateOnly(2061, 11, 13) },
                    { "uq", 8, 1484, new DateOnly(2062, 1, 12), new DateOnly(2061, 12, 13) },
                    { "uq", 9, 1484, new DateOnly(2062, 2, 10), new DateOnly(2062, 1, 12) },
                    { "uq", 10, 1484, new DateOnly(2062, 3, 12), new DateOnly(2062, 2, 10) },
                    { "uq", 11, 1484, new DateOnly(2062, 4, 11), new DateOnly(2062, 3, 12) },
                    { "uq", 12, 1484, new DateOnly(2062, 5, 10), new DateOnly(2062, 4, 11) },
                    { "uq", 1, 1485, new DateOnly(2062, 6, 8), new DateOnly(2062, 5, 10) },
                    { "uq", 2, 1485, new DateOnly(2062, 7, 8), new DateOnly(2062, 6, 8) },
                    { "uq", 3, 1485, new DateOnly(2062, 8, 6), new DateOnly(2062, 7, 8) },
                    { "uq", 4, 1485, new DateOnly(2062, 9, 4), new DateOnly(2062, 8, 6) },
                    { "uq", 5, 1485, new DateOnly(2062, 10, 4), new DateOnly(2062, 9, 4) },
                    { "uq", 6, 1485, new DateOnly(2062, 11, 3), new DateOnly(2062, 10, 4) },
                    { "uq", 7, 1485, new DateOnly(2062, 12, 2), new DateOnly(2062, 11, 3) },
                    { "uq", 8, 1485, new DateOnly(2063, 1, 1), new DateOnly(2062, 12, 2) },
                    { "uq", 9, 1485, new DateOnly(2063, 1, 30), new DateOnly(2063, 1, 1) },
                    { "uq", 10, 1485, new DateOnly(2063, 3, 1), new DateOnly(2063, 1, 30) },
                    { "uq", 11, 1485, new DateOnly(2063, 3, 31), new DateOnly(2063, 3, 1) },
                    { "uq", 12, 1485, new DateOnly(2063, 4, 30), new DateOnly(2063, 3, 31) },
                    { "uq", 1, 1486, new DateOnly(2063, 5, 29), new DateOnly(2063, 4, 30) },
                    { "uq", 2, 1486, new DateOnly(2063, 6, 27), new DateOnly(2063, 5, 29) },
                    { "uq", 3, 1486, new DateOnly(2063, 7, 27), new DateOnly(2063, 6, 27) },
                    { "uq", 4, 1486, new DateOnly(2063, 8, 25), new DateOnly(2063, 7, 27) },
                    { "uq", 5, 1486, new DateOnly(2063, 9, 24), new DateOnly(2063, 8, 25) },
                    { "uq", 6, 1486, new DateOnly(2063, 10, 23), new DateOnly(2063, 9, 24) },
                    { "uq", 7, 1486, new DateOnly(2063, 11, 22), new DateOnly(2063, 10, 23) },
                    { "uq", 8, 1486, new DateOnly(2063, 12, 21), new DateOnly(2063, 11, 22) },
                    { "uq", 9, 1486, new DateOnly(2064, 1, 20), new DateOnly(2063, 12, 21) },
                    { "uq", 10, 1486, new DateOnly(2064, 2, 18), new DateOnly(2064, 1, 20) },
                    { "uq", 11, 1486, new DateOnly(2064, 3, 19), new DateOnly(2064, 2, 18) },
                    { "uq", 12, 1486, new DateOnly(2064, 4, 18), new DateOnly(2064, 3, 19) },
                    { "uq", 1, 1487, new DateOnly(2064, 5, 17), new DateOnly(2064, 4, 18) },
                    { "uq", 2, 1487, new DateOnly(2064, 6, 16), new DateOnly(2064, 5, 17) },
                    { "uq", 3, 1487, new DateOnly(2064, 7, 15), new DateOnly(2064, 6, 16) },
                    { "uq", 4, 1487, new DateOnly(2064, 8, 14), new DateOnly(2064, 7, 15) },
                    { "uq", 5, 1487, new DateOnly(2064, 9, 12), new DateOnly(2064, 8, 14) },
                    { "uq", 6, 1487, new DateOnly(2064, 10, 12), new DateOnly(2064, 9, 12) },
                    { "uq", 7, 1487, new DateOnly(2064, 11, 10), new DateOnly(2064, 10, 12) },
                    { "uq", 8, 1487, new DateOnly(2064, 12, 9), new DateOnly(2064, 11, 10) },
                    { "uq", 9, 1487, new DateOnly(2065, 1, 8), new DateOnly(2064, 12, 9) },
                    { "uq", 10, 1487, new DateOnly(2065, 2, 6), new DateOnly(2065, 1, 8) },
                    { "uq", 11, 1487, new DateOnly(2065, 3, 8), new DateOnly(2065, 2, 6) },
                    { "uq", 12, 1487, new DateOnly(2065, 4, 7), new DateOnly(2065, 3, 8) },
                    { "uq", 1, 1488, new DateOnly(2065, 5, 6), new DateOnly(2065, 4, 7) },
                    { "uq", 2, 1488, new DateOnly(2065, 6, 5), new DateOnly(2065, 5, 6) },
                    { "uq", 3, 1488, new DateOnly(2065, 7, 5), new DateOnly(2065, 6, 5) },
                    { "uq", 4, 1488, new DateOnly(2065, 8, 3), new DateOnly(2065, 7, 5) },
                    { "uq", 5, 1488, new DateOnly(2065, 9, 2), new DateOnly(2065, 8, 3) },
                    { "uq", 6, 1488, new DateOnly(2065, 10, 1), new DateOnly(2065, 9, 2) },
                    { "uq", 7, 1488, new DateOnly(2065, 10, 31), new DateOnly(2065, 10, 1) },
                    { "uq", 8, 1488, new DateOnly(2065, 11, 29), new DateOnly(2065, 10, 31) },
                    { "uq", 9, 1488, new DateOnly(2065, 12, 28), new DateOnly(2065, 11, 29) },
                    { "uq", 10, 1488, new DateOnly(2066, 1, 27), new DateOnly(2065, 12, 28) },
                    { "uq", 11, 1488, new DateOnly(2066, 2, 25), new DateOnly(2066, 1, 27) },
                    { "uq", 12, 1488, new DateOnly(2066, 3, 27), new DateOnly(2066, 2, 25) },
                    { "uq", 1, 1489, new DateOnly(2066, 4, 25), new DateOnly(2066, 3, 27) },
                    { "uq", 2, 1489, new DateOnly(2066, 5, 25), new DateOnly(2066, 4, 25) },
                    { "uq", 3, 1489, new DateOnly(2066, 6, 24), new DateOnly(2066, 5, 25) },
                    { "uq", 4, 1489, new DateOnly(2066, 7, 24), new DateOnly(2066, 6, 24) },
                    { "uq", 5, 1489, new DateOnly(2066, 8, 22), new DateOnly(2066, 7, 24) },
                    { "uq", 6, 1489, new DateOnly(2066, 9, 21), new DateOnly(2066, 8, 22) },
                    { "uq", 7, 1489, new DateOnly(2066, 10, 20), new DateOnly(2066, 9, 21) },
                    { "uq", 8, 1489, new DateOnly(2066, 11, 19), new DateOnly(2066, 10, 20) },
                    { "uq", 9, 1489, new DateOnly(2066, 12, 18), new DateOnly(2066, 11, 19) },
                    { "uq", 10, 1489, new DateOnly(2067, 1, 16), new DateOnly(2066, 12, 18) },
                    { "uq", 11, 1489, new DateOnly(2067, 2, 15), new DateOnly(2067, 1, 16) },
                    { "uq", 12, 1489, new DateOnly(2067, 3, 16), new DateOnly(2067, 2, 15) },
                    { "uq", 1, 1490, new DateOnly(2067, 4, 15), new DateOnly(2067, 3, 16) },
                    { "uq", 2, 1490, new DateOnly(2067, 5, 14), new DateOnly(2067, 4, 15) },
                    { "uq", 3, 1490, new DateOnly(2067, 6, 13), new DateOnly(2067, 5, 14) },
                    { "uq", 4, 1490, new DateOnly(2067, 7, 13), new DateOnly(2067, 6, 13) },
                    { "uq", 5, 1490, new DateOnly(2067, 8, 11), new DateOnly(2067, 7, 13) },
                    { "uq", 6, 1490, new DateOnly(2067, 9, 10), new DateOnly(2067, 8, 11) },
                    { "uq", 7, 1490, new DateOnly(2067, 10, 10), new DateOnly(2067, 9, 10) },
                    { "uq", 8, 1490, new DateOnly(2067, 11, 8), new DateOnly(2067, 10, 10) },
                    { "uq", 9, 1490, new DateOnly(2067, 12, 8), new DateOnly(2067, 11, 8) },
                    { "uq", 10, 1490, new DateOnly(2068, 1, 6), new DateOnly(2067, 12, 8) },
                    { "uq", 11, 1490, new DateOnly(2068, 2, 4), new DateOnly(2068, 1, 6) },
                    { "uq", 12, 1490, new DateOnly(2068, 3, 5), new DateOnly(2068, 2, 4) },
                    { "uq", 1, 1491, new DateOnly(2068, 4, 3), new DateOnly(2068, 3, 5) },
                    { "uq", 2, 1491, new DateOnly(2068, 5, 3), new DateOnly(2068, 4, 3) },
                    { "uq", 3, 1491, new DateOnly(2068, 6, 1), new DateOnly(2068, 5, 3) },
                    { "uq", 4, 1491, new DateOnly(2068, 7, 1), new DateOnly(2068, 6, 1) },
                    { "uq", 5, 1491, new DateOnly(2068, 7, 30), new DateOnly(2068, 7, 1) },
                    { "uq", 6, 1491, new DateOnly(2068, 8, 29), new DateOnly(2068, 7, 30) },
                    { "uq", 7, 1491, new DateOnly(2068, 9, 28), new DateOnly(2068, 8, 29) },
                    { "uq", 8, 1491, new DateOnly(2068, 10, 27), new DateOnly(2068, 9, 28) },
                    { "uq", 9, 1491, new DateOnly(2068, 11, 26), new DateOnly(2068, 10, 27) },
                    { "uq", 10, 1491, new DateOnly(2068, 12, 25), new DateOnly(2068, 11, 26) },
                    { "uq", 11, 1491, new DateOnly(2069, 1, 24), new DateOnly(2068, 12, 25) },
                    { "uq", 12, 1491, new DateOnly(2069, 2, 23), new DateOnly(2069, 1, 24) },
                    { "uq", 1, 1492, new DateOnly(2069, 3, 24), new DateOnly(2069, 2, 23) },
                    { "uq", 2, 1492, new DateOnly(2069, 4, 22), new DateOnly(2069, 3, 24) },
                    { "uq", 3, 1492, new DateOnly(2069, 5, 22), new DateOnly(2069, 4, 22) },
                    { "uq", 4, 1492, new DateOnly(2069, 6, 20), new DateOnly(2069, 5, 22) },
                    { "uq", 5, 1492, new DateOnly(2069, 7, 20), new DateOnly(2069, 6, 20) },
                    { "uq", 6, 1492, new DateOnly(2069, 8, 18), new DateOnly(2069, 7, 20) },
                    { "uq", 7, 1492, new DateOnly(2069, 9, 17), new DateOnly(2069, 8, 18) },
                    { "uq", 8, 1492, new DateOnly(2069, 10, 16), new DateOnly(2069, 9, 17) },
                    { "uq", 9, 1492, new DateOnly(2069, 11, 15), new DateOnly(2069, 10, 16) },
                    { "uq", 10, 1492, new DateOnly(2069, 12, 15), new DateOnly(2069, 11, 15) },
                    { "uq", 11, 1492, new DateOnly(2070, 1, 13), new DateOnly(2069, 12, 15) },
                    { "uq", 12, 1492, new DateOnly(2070, 2, 12), new DateOnly(2070, 1, 13) },
                    { "uq", 1, 1493, new DateOnly(2070, 3, 14), new DateOnly(2070, 2, 12) },
                    { "uq", 2, 1493, new DateOnly(2070, 4, 12), new DateOnly(2070, 3, 14) },
                    { "uq", 3, 1493, new DateOnly(2070, 5, 11), new DateOnly(2070, 4, 12) },
                    { "uq", 4, 1493, new DateOnly(2070, 6, 10), new DateOnly(2070, 5, 11) },
                    { "uq", 5, 1493, new DateOnly(2070, 7, 9), new DateOnly(2070, 6, 10) },
                    { "uq", 6, 1493, new DateOnly(2070, 8, 8), new DateOnly(2070, 7, 9) },
                    { "uq", 7, 1493, new DateOnly(2070, 9, 6), new DateOnly(2070, 8, 8) },
                    { "uq", 8, 1493, new DateOnly(2070, 10, 5), new DateOnly(2070, 9, 6) },
                    { "uq", 9, 1493, new DateOnly(2070, 11, 4), new DateOnly(2070, 10, 5) },
                    { "uq", 10, 1493, new DateOnly(2070, 12, 4), new DateOnly(2070, 11, 4) },
                    { "uq", 11, 1493, new DateOnly(2071, 1, 2), new DateOnly(2070, 12, 4) },
                    { "uq", 12, 1493, new DateOnly(2071, 2, 1), new DateOnly(2071, 1, 2) },
                    { "uq", 1, 1494, new DateOnly(2071, 3, 3), new DateOnly(2071, 2, 1) },
                    { "uq", 2, 1494, new DateOnly(2071, 4, 2), new DateOnly(2071, 3, 3) },
                    { "uq", 3, 1494, new DateOnly(2071, 5, 1), new DateOnly(2071, 4, 2) },
                    { "uq", 4, 1494, new DateOnly(2071, 5, 30), new DateOnly(2071, 5, 1) },
                    { "uq", 5, 1494, new DateOnly(2071, 6, 29), new DateOnly(2071, 5, 30) },
                    { "uq", 6, 1494, new DateOnly(2071, 7, 28), new DateOnly(2071, 6, 29) },
                    { "uq", 7, 1494, new DateOnly(2071, 8, 26), new DateOnly(2071, 7, 28) },
                    { "uq", 8, 1494, new DateOnly(2071, 9, 25), new DateOnly(2071, 8, 26) },
                    { "uq", 9, 1494, new DateOnly(2071, 10, 24), new DateOnly(2071, 9, 25) },
                    { "uq", 10, 1494, new DateOnly(2071, 11, 23), new DateOnly(2071, 10, 24) },
                    { "uq", 11, 1494, new DateOnly(2071, 12, 22), new DateOnly(2071, 11, 23) },
                    { "uq", 12, 1494, new DateOnly(2072, 1, 21), new DateOnly(2071, 12, 22) },
                    { "uq", 1, 1495, new DateOnly(2072, 2, 20), new DateOnly(2072, 1, 21) },
                    { "uq", 2, 1495, new DateOnly(2072, 3, 21), new DateOnly(2072, 2, 20) },
                    { "uq", 3, 1495, new DateOnly(2072, 4, 19), new DateOnly(2072, 3, 21) },
                    { "uq", 4, 1495, new DateOnly(2072, 5, 19), new DateOnly(2072, 4, 19) },
                    { "uq", 5, 1495, new DateOnly(2072, 6, 17), new DateOnly(2072, 5, 19) },
                    { "uq", 6, 1495, new DateOnly(2072, 7, 17), new DateOnly(2072, 6, 17) },
                    { "uq", 7, 1495, new DateOnly(2072, 8, 15), new DateOnly(2072, 7, 17) },
                    { "uq", 8, 1495, new DateOnly(2072, 9, 13), new DateOnly(2072, 8, 15) },
                    { "uq", 9, 1495, new DateOnly(2072, 10, 13), new DateOnly(2072, 9, 13) },
                    { "uq", 10, 1495, new DateOnly(2072, 11, 11), new DateOnly(2072, 10, 13) },
                    { "uq", 11, 1495, new DateOnly(2072, 12, 11), new DateOnly(2072, 11, 11) },
                    { "uq", 12, 1495, new DateOnly(2073, 1, 9), new DateOnly(2072, 12, 11) },
                    { "uq", 1, 1496, new DateOnly(2073, 2, 8), new DateOnly(2073, 1, 9) },
                    { "uq", 2, 1496, new DateOnly(2073, 3, 10), new DateOnly(2073, 2, 8) },
                    { "uq", 3, 1496, new DateOnly(2073, 4, 9), new DateOnly(2073, 3, 10) },
                    { "uq", 4, 1496, new DateOnly(2073, 5, 8), new DateOnly(2073, 4, 9) },
                    { "uq", 5, 1496, new DateOnly(2073, 6, 7), new DateOnly(2073, 5, 8) },
                    { "uq", 6, 1496, new DateOnly(2073, 7, 6), new DateOnly(2073, 6, 7) },
                    { "uq", 7, 1496, new DateOnly(2073, 8, 5), new DateOnly(2073, 7, 6) },
                    { "uq", 8, 1496, new DateOnly(2073, 9, 3), new DateOnly(2073, 8, 5) },
                    { "uq", 9, 1496, new DateOnly(2073, 10, 2), new DateOnly(2073, 9, 3) },
                    { "uq", 10, 1496, new DateOnly(2073, 11, 1), new DateOnly(2073, 10, 2) },
                    { "uq", 11, 1496, new DateOnly(2073, 11, 30), new DateOnly(2073, 11, 1) },
                    { "uq", 12, 1496, new DateOnly(2073, 12, 30), new DateOnly(2073, 11, 30) },
                    { "uq", 1, 1497, new DateOnly(2074, 1, 28), new DateOnly(2073, 12, 30) },
                    { "uq", 2, 1497, new DateOnly(2074, 2, 27), new DateOnly(2074, 1, 28) },
                    { "uq", 3, 1497, new DateOnly(2074, 3, 29), new DateOnly(2074, 2, 27) },
                    { "uq", 4, 1497, new DateOnly(2074, 4, 27), new DateOnly(2074, 3, 29) },
                    { "uq", 5, 1497, new DateOnly(2074, 5, 27), new DateOnly(2074, 4, 27) },
                    { "uq", 6, 1497, new DateOnly(2074, 6, 26), new DateOnly(2074, 5, 27) },
                    { "uq", 7, 1497, new DateOnly(2074, 7, 25), new DateOnly(2074, 6, 26) },
                    { "uq", 8, 1497, new DateOnly(2074, 8, 23), new DateOnly(2074, 7, 25) },
                    { "uq", 9, 1497, new DateOnly(2074, 9, 22), new DateOnly(2074, 8, 23) },
                    { "uq", 10, 1497, new DateOnly(2074, 10, 21), new DateOnly(2074, 9, 22) },
                    { "uq", 11, 1497, new DateOnly(2074, 11, 20), new DateOnly(2074, 10, 21) },
                    { "uq", 12, 1497, new DateOnly(2074, 12, 19), new DateOnly(2074, 11, 20) },
                    { "uq", 1, 1498, new DateOnly(2075, 1, 18), new DateOnly(2074, 12, 19) },
                    { "uq", 2, 1498, new DateOnly(2075, 2, 16), new DateOnly(2075, 1, 18) },
                    { "uq", 3, 1498, new DateOnly(2075, 3, 18), new DateOnly(2075, 2, 16) },
                    { "uq", 4, 1498, new DateOnly(2075, 4, 16), new DateOnly(2075, 3, 18) },
                    { "uq", 5, 1498, new DateOnly(2075, 5, 16), new DateOnly(2075, 4, 16) },
                    { "uq", 6, 1498, new DateOnly(2075, 6, 15), new DateOnly(2075, 5, 16) },
                    { "uq", 7, 1498, new DateOnly(2075, 7, 14), new DateOnly(2075, 6, 15) },
                    { "uq", 8, 1498, new DateOnly(2075, 8, 13), new DateOnly(2075, 7, 14) },
                    { "uq", 9, 1498, new DateOnly(2075, 9, 11), new DateOnly(2075, 8, 13) },
                    { "uq", 10, 1498, new DateOnly(2075, 10, 11), new DateOnly(2075, 9, 11) },
                    { "uq", 11, 1498, new DateOnly(2075, 11, 9), new DateOnly(2075, 10, 11) },
                    { "uq", 12, 1498, new DateOnly(2075, 12, 9), new DateOnly(2075, 11, 9) },
                    { "uq", 1, 1499, new DateOnly(2076, 1, 7), new DateOnly(2075, 12, 9) },
                    { "uq", 2, 1499, new DateOnly(2076, 2, 6), new DateOnly(2076, 1, 7) },
                    { "uq", 3, 1499, new DateOnly(2076, 3, 6), new DateOnly(2076, 2, 6) },
                    { "uq", 4, 1499, new DateOnly(2076, 4, 5), new DateOnly(2076, 3, 6) },
                    { "uq", 5, 1499, new DateOnly(2076, 5, 4), new DateOnly(2076, 4, 5) },
                    { "uq", 6, 1499, new DateOnly(2076, 6, 3), new DateOnly(2076, 5, 4) },
                    { "uq", 7, 1499, new DateOnly(2076, 7, 2), new DateOnly(2076, 6, 3) },
                    { "uq", 8, 1499, new DateOnly(2076, 8, 1), new DateOnly(2076, 7, 2) },
                    { "uq", 9, 1499, new DateOnly(2076, 8, 30), new DateOnly(2076, 8, 1) },
                    { "uq", 10, 1499, new DateOnly(2076, 9, 29), new DateOnly(2076, 8, 30) },
                    { "uq", 11, 1499, new DateOnly(2076, 10, 29), new DateOnly(2076, 9, 29) },
                    { "uq", 12, 1499, new DateOnly(2076, 11, 27), new DateOnly(2076, 10, 29) },
                    { "uq", 1, 1500, new DateOnly(2076, 12, 27), new DateOnly(2076, 11, 27) },
                    { "uq", 2, 1500, new DateOnly(2077, 1, 26), new DateOnly(2076, 12, 27) },
                    { "uq", 3, 1500, new DateOnly(2077, 2, 24), new DateOnly(2077, 1, 26) },
                    { "uq", 4, 1500, new DateOnly(2077, 3, 25), new DateOnly(2077, 2, 24) },
                    { "uq", 5, 1500, new DateOnly(2077, 4, 24), new DateOnly(2077, 3, 25) },
                    { "uq", 6, 1500, new DateOnly(2077, 5, 23), new DateOnly(2077, 4, 24) },
                    { "uq", 7, 1500, new DateOnly(2077, 6, 21), new DateOnly(2077, 5, 23) },
                    { "uq", 8, 1500, new DateOnly(2077, 7, 21), new DateOnly(2077, 6, 21) },
                    { "uq", 9, 1500, new DateOnly(2077, 8, 19), new DateOnly(2077, 7, 21) },
                    { "uq", 10, 1500, new DateOnly(2077, 9, 18), new DateOnly(2077, 8, 19) },
                    { "uq", 11, 1500, new DateOnly(2077, 10, 18), new DateOnly(2077, 9, 18) },
                    { "uq", 12, 1500, new DateOnly(2077, 11, 17), new DateOnly(2077, 10, 18) }
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
                table: "Schedules",
                columns: new[] { "Id", "ArgumentsJson", "CatchUpWindowMinutes", "Code", "CreatedAt", "CreatedById", "CronExpression", "HandlerKey", "IsActive", "ModifiedAt", "ModifiedById", "Name", "Name2", "Name3", "PausedReason", "RunAsUserId", "TimeZoneId" },
                values: new object[,]
                {
                    { 1, null, 1440, "core.job-retention", new DateTimeOffset(new DateTime(2000, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 1, "0 3 * * *", "core.job-retention", true, new DateTimeOffset(new DateTime(2000, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 1, "core.job-retention", null, null, null, 1, null },
                    { 2, null, 1440, "core.notification-retention", new DateTimeOffset(new DateTime(2000, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 1, "30 3 * * *", "core.notification-retention", true, new DateTimeOffset(new DateTime(2000, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 1, "core.notification-retention", null, null, null, 1, null },
                    { 6, null, 1440, "core.tree-verify", new DateTimeOffset(new DateTime(2000, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 1, "0 1 * * 6", "core.tree-verify", true, new DateTimeOffset(new DateTime(2000, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 1, "core.tree-verify", null, null, null, 1, null }
                });

            migrationBuilder.InsertData(
                schema: "core",
                table: "Settings",
                columns: new[] { "Id", "CreatedAt", "CreatedById", "Digits", "ModifiedAt", "ModifiedById", "Name", "Name2", "Name3", "PrimaryCalendar", "PrimaryLanguage", "Region", "SecondaryCalendar", "SecondaryLanguage", "TenantId", "TertiaryLanguage", "TimeZone" },
                values: new object[] { 1, new DateTimeOffset(new DateTime(2000, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 1, null, new DateTimeOffset(new DateTime(2000, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 1, "", null, null, "gc", "en", null, null, null, 0, null, "UTC" });

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

            migrationBuilder.InsertData(
                schema: "core",
                table: "ScheduleStates",
                columns: new[] { "ScheduleId", "LastFiredAt", "LastJobId", "LastScheduledFor", "LastSkippedAt", "LeaseExpiresAt", "LeaseToken", "NextDueAt" },
                values: new object[,]
                {
                    { 1, null, null, null, null, null, null, null },
                    { 2, null, null, null, null, null, null, null },
                    { 6, null, null, null, null, null, null, null }
                });

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
                name: "UX_CalendarMonths_Calendar_StartDate",
                schema: "core",
                table: "CalendarMonths",
                columns: new[] { "Calendar", "StartDate" },
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
                name: "UX_SettingEntries_Key",
                schema: "core",
                table: "SettingEntries",
                column: "Key",
                unique: true);

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
                name: "SettingEntriesList",
                physicalName: "SettingEntriesList_609d1ba0",
                schema: "core",
                scope: "tellma.tenant",
                definitionHash: "609d1ba08b6e8600b0ccb9c32d6a985caa7cf7b9185a899391a2a2df2b7eaa3c",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "Key", StoreType = "nvarchar(128)", MaxLength = 128 },
                    new TableTypeColumnDefinition { Name = "Value", StoreType = "nvarchar(max)" },
                    new TableTypeColumnDefinition { Name = "CreatedAt", StoreType = "datetimeoffset(7)", Precision = 7 },
                    new TableTypeColumnDefinition { Name = "CreatedById", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "ModifiedAt", StoreType = "datetimeoffset(7)", Precision = 7 },
                    new TableTypeColumnDefinition { Name = "ModifiedById", StoreType = "int" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CreateTableType(
                name: "SettingsList",
                physicalName: "SettingsList_254adc5a",
                schema: "core",
                scope: "tellma.tenant",
                definitionHash: "254adc5a393f26340e5e17f105641917a10626c328fcbb74187c4dc6f8545395",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "TenantId", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "Name", StoreType = "nvarchar(255)", MaxLength = 255 },
                    new TableTypeColumnDefinition { Name = "Name2", StoreType = "nvarchar(255)", IsNullable = true, MaxLength = 255 },
                    new TableTypeColumnDefinition { Name = "Name3", StoreType = "nvarchar(255)", IsNullable = true, MaxLength = 255 },
                    new TableTypeColumnDefinition { Name = "PrimaryLanguage", StoreType = "varchar(35)" },
                    new TableTypeColumnDefinition { Name = "SecondaryLanguage", StoreType = "varchar(35)", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "TertiaryLanguage", StoreType = "varchar(35)", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "PrimaryCalendar", StoreType = "varchar(16)" },
                    new TableTypeColumnDefinition { Name = "SecondaryCalendar", StoreType = "varchar(16)", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "TimeZone", StoreType = "varchar(64)" },
                    new TableTypeColumnDefinition { Name = "Region", StoreType = "varchar(2)", IsNullable = true },
                    new TableTypeColumnDefinition { Name = "Digits", StoreType = "varchar(8)", IsNullable = true, MaxLength = 8 },
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

            migrationBuilder.CreateTableType(
                name: "GadgetsList",
                physicalName: "GadgetsList_d6f3f00e",
                schema: "probe",
                scope: "tellma.tenant",
                definitionHash: "d6f3f00ec247077f105e1b5a730546399a9b7d5c2e7f96907f0e6dbae6e4a011",
                columns: new[]
                {
                    new TableTypeColumnDefinition { Name = "Id", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "Name", StoreType = "nvarchar(255)", MaxLength = 255 },
                    new TableTypeColumnDefinition { Name = "CreatedAt", StoreType = "datetimeoffset(7)", Precision = 7 },
                    new TableTypeColumnDefinition { Name = "CreatedById", StoreType = "int" },
                    new TableTypeColumnDefinition { Name = "ModifiedAt", StoreType = "datetimeoffset(7)", Precision = 7 },
                    new TableTypeColumnDefinition { Name = "ModifiedById", StoreType = "int" },
                },
                primaryKey: new[] { "Id" },
                grants: new[] { "tellma_app" });

            migrationBuilder.CleanupTableTypes(scope: "tellma.tenant", keepList: new[] { "JobsList_a90a5696", "NotificationsList_45bbd83e", "PermissionsList_c3b8a544", "RoleMembershipsList_fd20e92d", "RolesList_77bed563", "SchedulesList_5acfe7bf", "SettingEntriesList_609d1ba0", "SettingsList_254adc5a", "UsersList_860a025a", "BigIdList_7629c0a3", "DateList_566fd984", "GuidList_922aa933", "IdList_9b0dfe3c", "IdStampList_36427f71", "JobLeaseList_17bf60e1", "JobOutcomeList_6165fff7", "JobProgressList_33eeb232", "JobRequestList_f3ef6d1d", "NotificationPreferenceList_61bf7917", "NotificationRowList_2c2b24d8", "NotificationSliceList_29aa6852", "ScheduleNextList_e422990c", "StringList_bb8903a2", "UserPreferenceList_8b1ed724", "VersionTagList_e552f6b8", "GadgetsList_d6f3f00e" });
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
                name: "CalendarMonths",
                schema: "core");

            migrationBuilder.DropTable(
                name: "Gadgets",
                schema: "probe");

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
                name: "SettingEntries",
                schema: "core")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "SettingEntriesHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "core")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

            migrationBuilder.DropTable(
                name: "Settings",
                schema: "core")
                .Annotation("SqlServer:IsTemporal", true)
                .Annotation("SqlServer:TemporalHistoryTableName", "SettingsHistory")
                .Annotation("SqlServer:TemporalHistoryTableSchema", "core")
                .Annotation("SqlServer:TemporalPeriodEndColumnName", "ValidTo")
                .Annotation("SqlServer:TemporalPeriodStartColumnName", "ValidFrom");

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
                name: "sq_Gadgets",
                schema: "probe");

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
                name: "sq_SettingEntries",
                schema: "core");

            migrationBuilder.DropSequence(
                name: "sq_Settings",
                schema: "core");

            migrationBuilder.DropSequence(
                name: "sq_Users",
                schema: "core");

            migrationBuilder.CleanupTableTypes(scope: "tellma.tenant", keepList: new string[0]);
        }
    }
}
