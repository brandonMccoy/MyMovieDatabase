using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMDb.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoviePeople : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "People",
                table: "Movies",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "People",
                table: "Movies");
        }
    }
}
