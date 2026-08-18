using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tellma.Identity.Migrations
{
    /// <inheritdoc />
    public partial class InvitationDispatchAndDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeliveryReason",
                schema: "idsvr",
                table: "SingleUseCodes",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeliveryStatus",
                schema: "idsvr",
                table: "SingleUseCodes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeliveryUpdatedUtc",
                schema: "idsvr",
                table: "SingleUseCodes",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DispatchAttempts",
                schema: "idsvr",
                table: "SingleUseCodes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DispatchClaimedUntil",
                schema: "idsvr",
                table: "SingleUseCodes",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DispatchState",
                schema: "idsvr",
                table: "SingleUseCodes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ExpectsDeliveryEvents",
                schema: "idsvr",
                table: "SingleUseCodes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LastProviderEventId",
                schema: "idsvr",
                table: "SingleUseCodes",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderMessageId",
                schema: "idsvr",
                table: "SingleUseCodes",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SentUtc",
                schema: "idsvr",
                table: "SingleUseCodes",
                type: "datetimeoffset",
                nullable: true);

            // Every row that already exists predates dispatch tracking, and the column default
            // would leave all of them Pending — which the recovery sweep reads as "never sent" and
            // would answer by mailing a fresh link to every outstanding invitation the moment this
            // deploys. They were dispatched by the fire-and-forget path that came before, so they
            // are recorded as Sent at the time they were created: it is what actually happened, and
            // it is the only backfill that cannot resend.
            migrationBuilder.Sql(
                """
                UPDATE [idsvr].[SingleUseCodes]
                SET [DispatchState] = 1, [SentUtc] = [CreatedUtc]
                WHERE [DispatchState] = 0;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_SingleUseCodes_CreatedByClientId_UserId_CreatedUtc",
                schema: "idsvr",
                table: "SingleUseCodes",
                columns: new[] { "CreatedByClientId", "UserId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SingleUseCodes_PendingDispatch",
                schema: "idsvr",
                table: "SingleUseCodes",
                columns: new[] { "Purpose", "CreatedUtc" },
                filter: "[DispatchState] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SingleUseCodes_CreatedByClientId_UserId_CreatedUtc",
                schema: "idsvr",
                table: "SingleUseCodes");

            migrationBuilder.DropIndex(
                name: "IX_SingleUseCodes_PendingDispatch",
                schema: "idsvr",
                table: "SingleUseCodes");

            migrationBuilder.DropColumn(
                name: "DeliveryReason",
                schema: "idsvr",
                table: "SingleUseCodes");

            migrationBuilder.DropColumn(
                name: "DeliveryStatus",
                schema: "idsvr",
                table: "SingleUseCodes");

            migrationBuilder.DropColumn(
                name: "DeliveryUpdatedUtc",
                schema: "idsvr",
                table: "SingleUseCodes");

            migrationBuilder.DropColumn(
                name: "DispatchAttempts",
                schema: "idsvr",
                table: "SingleUseCodes");

            migrationBuilder.DropColumn(
                name: "DispatchClaimedUntil",
                schema: "idsvr",
                table: "SingleUseCodes");

            migrationBuilder.DropColumn(
                name: "DispatchState",
                schema: "idsvr",
                table: "SingleUseCodes");

            migrationBuilder.DropColumn(
                name: "ExpectsDeliveryEvents",
                schema: "idsvr",
                table: "SingleUseCodes");

            migrationBuilder.DropColumn(
                name: "LastProviderEventId",
                schema: "idsvr",
                table: "SingleUseCodes");

            migrationBuilder.DropColumn(
                name: "ProviderMessageId",
                schema: "idsvr",
                table: "SingleUseCodes");

            migrationBuilder.DropColumn(
                name: "SentUtc",
                schema: "idsvr",
                table: "SingleUseCodes");
        }
    }
}
