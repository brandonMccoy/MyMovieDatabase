using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MMDb.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PairedDevices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PairedDevices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PairedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PairedDevices", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PairedDevices_TokenHash",
                table: "PairedDevices",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PairedDevices");
        }
    }
}
