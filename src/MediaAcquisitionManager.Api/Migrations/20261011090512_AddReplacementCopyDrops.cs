using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaAcquisitionManager.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddReplacementCopyDrops : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DroppedUtc",
                table: "ReplacementJobFiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OnlyDropSettles",
                table: "ReplacementJobFiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DroppedUtc",
                table: "ReplacementJobFiles");

            migrationBuilder.DropColumn(
                name: "OnlyDropSettles",
                table: "ReplacementJobFiles");
        }
    }
}
