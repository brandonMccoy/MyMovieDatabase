using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMDb.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialMovies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Movies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    Directors = table.Column<string>(type: "TEXT", nullable: false),
                    Cast = table.Column<string>(type: "TEXT", nullable: false),
                    Genres = table.Column<string>(type: "TEXT", nullable: false),
                    RuntimeMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    Rating = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Overview = table.Column<string>(type: "TEXT", nullable: true),
                    TmdbId = table.Column<int>(type: "INTEGER", nullable: true),
                    TrailerYouTubeKey = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CoverImage = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Movies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MovieFormats",
                columns: table => new
                {
                    Format = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    MovieId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Upc = table.Column<string>(type: "TEXT", maxLength: 13, nullable: true),
                    FilePath = table.Column<string>(type: "TEXT", nullable: true),
                    AddedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovieFormats", x => new { x.MovieId, x.Format });
                    table.ForeignKey(
                        name: "FK_MovieFormats_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MovieFormats_Upc",
                table: "MovieFormats",
                column: "Upc");

            migrationBuilder.CreateIndex(
                name: "IX_Movies_TmdbId",
                table: "Movies",
                column: "TmdbId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MovieFormats");

            migrationBuilder.DropTable(
                name: "Movies");
        }
    }
}
