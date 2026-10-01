using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaAcquisitionManager.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStoredSearches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImdbId",
                table: "Releases",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Peers",
                table: "Releases",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Position",
                table: "Releases",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SearchSourceId",
                table: "Releases",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Seeders",
                table: "Releases",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TmdbId",
                table: "Releases",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Searches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    ImdbId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    TmdbId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Searches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Searches_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SearchSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SearchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    IntegrationConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    NotSearchedReason = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SearchSources_IntegrationConnections_IntegrationConnectionId",
                        column: x => x.IntegrationConnectionId,
                        principalTable: "IntegrationConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SearchSources_Searches_SearchId",
                        column: x => x.SearchId,
                        principalTable: "Searches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Releases_SearchSourceId",
                table: "Releases",
                column: "SearchSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_Searches_UserId",
                table: "Searches",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SearchSources_IntegrationConnectionId",
                table: "SearchSources",
                column: "IntegrationConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_SearchSources_SearchId",
                table: "SearchSources",
                column: "SearchId");

            migrationBuilder.AddForeignKey(
                name: "FK_Releases_SearchSources_SearchSourceId",
                table: "Releases",
                column: "SearchSourceId",
                principalTable: "SearchSources",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Releases_SearchSources_SearchSourceId",
                table: "Releases");

            migrationBuilder.DropTable(
                name: "SearchSources");

            migrationBuilder.DropTable(
                name: "Searches");

            migrationBuilder.DropIndex(
                name: "IX_Releases_SearchSourceId",
                table: "Releases");

            migrationBuilder.DropColumn(
                name: "ImdbId",
                table: "Releases");

            migrationBuilder.DropColumn(
                name: "Peers",
                table: "Releases");

            migrationBuilder.DropColumn(
                name: "Position",
                table: "Releases");

            migrationBuilder.DropColumn(
                name: "SearchSourceId",
                table: "Releases");

            migrationBuilder.DropColumn(
                name: "Seeders",
                table: "Releases");

            migrationBuilder.DropColumn(
                name: "TmdbId",
                table: "Releases");
        }
    }
}
