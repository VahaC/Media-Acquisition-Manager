using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaAcquisitionManager.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDestinationDefaultAndLibraryFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "DestinationMappings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "LibraryLocationFolders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LibraryLocationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    ServicePath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryLocationFolders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LibraryLocationFolders_LibraryLocations_LibraryLocationId",
                        column: x => x.LibraryLocationId,
                        principalTable: "LibraryLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DestinationMappings_OneDefault",
                table: "DestinationMappings",
                column: "IsDefault",
                unique: true,
                filter: "\"IsDefault\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryLocationFolders_LibraryLocationId_Position",
                table: "LibraryLocationFolders",
                columns: new[] { "LibraryLocationId", "Position" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LibraryLocationFolders");

            migrationBuilder.DropIndex(
                name: "IX_DestinationMappings_OneDefault",
                table: "DestinationMappings");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "DestinationMappings");
        }
    }
}
