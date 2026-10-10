using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMDb.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MovieCrew : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Crew",
                table: "Movies",
                type: "TEXT",
                nullable: false,
                defaultValueSql: "'[]'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Crew",
                table: "Movies");
        }
    }
}
