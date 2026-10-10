using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMDb.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WishlistComicNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Number",
                table: "WishlistComics",
                type: "TEXT",
                maxLength: 16,
                nullable: true);

            // A run of missing issues used to be one row holding every number: each issue becomes a row of its own.
            migrationBuilder.Sql("""
                INSERT INTO WishlistComics (Id, Format, Series, SeriesYear, Number, Numbers, Title, Publisher, Isbn, MetronId, MetronSeriesId, CoverUrl, FromMissing, CreatedAt, UpdatedAt, Version, CoverImage)
                SELECT upper(printf('%s-%s-%s-%s-%s', hex(randomblob(4)), hex(randomblob(2)), hex(randomblob(2)), hex(randomblob(2)), hex(randomblob(6)))),
                       w.Format, w.Series, w.SeriesYear, j.value, '[]', w.Title, w.Publisher, w.Isbn, NULL, w.MetronSeriesId, NULL, w.FromMissing, w.CreatedAt, w.UpdatedAt, 1, NULL
                FROM WishlistComics w, json_each(w.Numbers) j
                WHERE j.key > 0;
                """);
            migrationBuilder.Sql("UPDATE WishlistComics SET Number = json_extract(Numbers, '$[0]') WHERE Number IS NULL;");

            migrationBuilder.DropColumn(
                name: "Numbers",
                table: "WishlistComics");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Number",
                table: "WishlistComics");

            migrationBuilder.AddColumn<string>(
                name: "Numbers",
                table: "WishlistComics",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }
    }
}
