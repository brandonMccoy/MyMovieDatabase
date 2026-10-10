using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMDb.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Comics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Comics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Format = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Series = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    SeriesYear = table.Column<int>(type: "INTEGER", nullable: true),
                    Number = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Variant = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Printing = table.Column<int>(type: "INTEGER", nullable: true),
                    Publisher = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CoverDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Writers = table.Column<string>(type: "TEXT", nullable: false),
                    Artists = table.Column<string>(type: "TEXT", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: true),
                    Isbn = table.Column<string>(type: "TEXT", maxLength: 13, nullable: true),
                    MetronId = table.Column<int>(type: "INTEGER", nullable: true),
                    MetronSeriesId = table.Column<int>(type: "INTEGER", nullable: true),
                    IsKeyIssue = table.Column<bool>(type: "INTEGER", nullable: false),
                    KeyNote = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CoverUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Upc = table.Column<string>(type: "TEXT", maxLength: 13, nullable: true),
                    UpcSupplement = table.Column<string>(type: "TEXT", maxLength: 5, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    CoverImage = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ComicSeries",
                columns: table => new
                {
                    MetronId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    YearBegan = table.Column<int>(type: "INTEGER", nullable: true),
                    Publisher = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IssueNumbers = table.Column<string>(type: "TEXT", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComicSeries", x => x.MetronId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Comics_Isbn",
                table: "Comics",
                column: "Isbn");

            migrationBuilder.CreateIndex(
                name: "IX_Comics_MetronId",
                table: "Comics",
                column: "MetronId");

            migrationBuilder.CreateIndex(
                name: "IX_Comics_Upc",
                table: "Comics",
                column: "Upc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Comics");

            migrationBuilder.DropTable(
                name: "ComicSeries");
        }
    }
}
