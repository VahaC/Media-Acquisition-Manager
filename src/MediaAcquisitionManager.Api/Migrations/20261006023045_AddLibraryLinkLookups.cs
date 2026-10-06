using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaAcquisitionManager.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryLinkLookups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EpisodeImdbId",
                table: "LibraryItemLinks",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LookupError",
                table: "LibraryItemLinks",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LookupRequestedUtc",
                table: "LibraryItemLinks",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EpisodeImdbId",
                table: "LibraryItemLinks");

            migrationBuilder.DropColumn(
                name: "LookupError",
                table: "LibraryItemLinks");

            migrationBuilder.DropColumn(
                name: "LookupRequestedUtc",
                table: "LibraryItemLinks");
        }
    }
}
