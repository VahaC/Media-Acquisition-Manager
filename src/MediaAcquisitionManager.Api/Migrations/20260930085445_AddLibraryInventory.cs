using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaAcquisitionManager.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LibraryLocationId",
                table: "PhysicalFiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedServicePath",
                table: "PhysicalFiles",
                type: "TEXT",
                maxLength: 4096,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedText",
                table: "MediaWorkTitles",
                type: "TEXT",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "LibraryLocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SourceConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SourceItemId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ObservedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LibraryLocations_IntegrationConnections_SourceConnectionId",
                        column: x => x.SourceConnectionId,
                        principalTable: "IntegrationConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "LibrarySyncRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IntegrationConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IntegrationName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RequestedByUserName = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FinishedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ItemsRead = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemsTotal = table.Column<int>(type: "INTEGER", nullable: true),
                    WorksCount = table.Column<int>(type: "INTEGER", nullable: true),
                    VersionsCount = table.Column<int>(type: "INTEGER", nullable: true),
                    FilesCount = table.Column<int>(type: "INTEGER", nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibrarySyncRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LibrarySyncRuns_IntegrationConnections_IntegrationConnectionId",
                        column: x => x.IntegrationConnectionId,
                        principalTable: "IntegrationConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LibrarySyncRuns_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhysicalFiles_LibraryLocationId",
                table: "PhysicalFiles",
                column: "LibraryLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryLocations_SourceConnectionId_SourceItemId",
                table: "LibraryLocations",
                columns: new[] { "SourceConnectionId", "SourceItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_LibrarySyncRuns_IntegrationConnectionId_CreatedUtc",
                table: "LibrarySyncRuns",
                columns: new[] { "IntegrationConnectionId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LibrarySyncRuns_OneActiveRunPerIntegration",
                table: "LibrarySyncRuns",
                column: "IntegrationConnectionId",
                unique: true,
                filter: "\"Status\" IN ('Queued', 'Running')");

            migrationBuilder.CreateIndex(
                name: "IX_LibrarySyncRuns_RequestedByUserId",
                table: "LibrarySyncRuns",
                column: "RequestedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_PhysicalFiles_LibraryLocations_LibraryLocationId",
                table: "PhysicalFiles",
                column: "LibraryLocationId",
                principalTable: "LibraryLocations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PhysicalFiles_LibraryLocations_LibraryLocationId",
                table: "PhysicalFiles");

            migrationBuilder.DropTable(
                name: "LibraryLocations");

            migrationBuilder.DropTable(
                name: "LibrarySyncRuns");

            migrationBuilder.DropIndex(
                name: "IX_PhysicalFiles_LibraryLocationId",
                table: "PhysicalFiles");

            migrationBuilder.DropColumn(
                name: "LibraryLocationId",
                table: "PhysicalFiles");

            migrationBuilder.DropColumn(
                name: "NormalizedServicePath",
                table: "PhysicalFiles");

            migrationBuilder.DropColumn(
                name: "NormalizedText",
                table: "MediaWorkTitles");
        }
    }
}
