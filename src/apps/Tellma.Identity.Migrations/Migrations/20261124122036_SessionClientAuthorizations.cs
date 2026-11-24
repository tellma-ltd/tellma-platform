using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tellma.Identity.Migrations
{
    /// <inheritdoc />
    public partial class SessionClientAuthorizations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthorizationId",
                schema: "idsvr",
                table: "SessionClients");

            migrationBuilder.CreateTable(
                name: "SessionClientAuthorizations",
                schema: "idsvr",
                columns: table => new
                {
                    Sid = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ClientId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AuthorizationId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionClientAuthorizations", x => new { x.Sid, x.ClientId, x.AuthorizationId });
                    table.ForeignKey(
                        name: "FK_SessionClientAuthorizations_SessionClients_Sid_ClientId",
                        columns: x => new { x.Sid, x.ClientId },
                        principalSchema: "idsvr",
                        principalTable: "SessionClients",
                        principalColumns: new[] { "Sid", "ClientId" },
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SessionClientAuthorizations",
                schema: "idsvr");

            migrationBuilder.AddColumn<string>(
                name: "AuthorizationId",
                schema: "idsvr",
                table: "SessionClients",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);
        }
    }
}
