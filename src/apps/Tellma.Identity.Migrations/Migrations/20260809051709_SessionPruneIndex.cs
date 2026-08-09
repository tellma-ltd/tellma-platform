using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tellma.Identity.Migrations
{
    /// <inheritdoc />
    public partial class SessionPruneIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Sessions_TerminatedUtc_LastSeenUtc",
                schema: "idsvr",
                table: "Sessions",
                columns: new[] { "TerminatedUtc", "LastSeenUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Sessions_TerminatedUtc_LastSeenUtc",
                schema: "idsvr",
                table: "Sessions");
        }
    }
}
