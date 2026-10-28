using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CollProbe.Migrations
{
    /// <inheritdoc />
    public partial class PlatformCollation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Emptied by hand: the collation is set when the database is created, never altered.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Emptied by hand, as Up.
        }
    }
}
