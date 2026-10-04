using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaAcquisitionManager.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSeriesAndTvReleases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Season",
                table: "Searches",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CategoryIds",
                table: "Releases",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            // MAM-32 — every release stored before this was a movie search's.
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Releases",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Movie");

            migrationBuilder.AddColumn<string>(
                name: "TvdbId",
                table: "Releases",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeriesCount",
                table: "LibrarySyncRuns",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DestinationSections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DestinationMappingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReleaseSourceConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsSourceSpecific = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DestinationSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DestinationSections_DestinationMappings_DestinationMappingId",
                        column: x => x.DestinationMappingId,
                        principalTable: "DestinationMappings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DestinationSections_IntegrationConnections_ReleaseSourceConnectionId",
                        column: x => x.ReleaseSourceConnectionId,
                        principalTable: "IntegrationConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaEpisodePositions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MediaWorkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeriesWorkId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SeasonNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    EpisodeNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SourceConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SourceItemId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ObservedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaEpisodePositions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaEpisodePositions_IntegrationConnections_SourceConnectionId",
                        column: x => x.SourceConnectionId,
                        principalTable: "IntegrationConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MediaEpisodePositions_MediaWorks_MediaWorkId",
                        column: x => x.MediaWorkId,
                        principalTable: "MediaWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaEpisodePositions_MediaWorks_SeriesWorkId",
                        column: x => x.SeriesWorkId,
                        principalTable: "MediaWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DestinationSections_DestinationMappingId",
                table: "DestinationSections",
                column: "DestinationMappingId");

            migrationBuilder.CreateIndex(
                name: "IX_DestinationSections_ReleaseSourceConnectionId_CategoryId",
                table: "DestinationSections",
                columns: new[] { "ReleaseSourceConnectionId", "CategoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaEpisodePositions_MediaWorkId",
                table: "MediaEpisodePositions",
                column: "MediaWorkId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaEpisodePositions_SeriesWorkId_SeasonNumber",
                table: "MediaEpisodePositions",
                columns: new[] { "SeriesWorkId", "SeasonNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaEpisodePositions_SourceConnectionId_SourceItemId",
                table: "MediaEpisodePositions",
                columns: new[] { "SourceConnectionId", "SourceItemId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DestinationSections");

            migrationBuilder.DropTable(
                name: "MediaEpisodePositions");

            migrationBuilder.DropColumn(
                name: "Season",
                table: "Searches");

            migrationBuilder.DropColumn(
                name: "CategoryIds",
                table: "Releases");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Releases");

            migrationBuilder.DropColumn(
                name: "TvdbId",
                table: "Releases");

            migrationBuilder.DropColumn(
                name: "SeriesCount",
                table: "LibrarySyncRuns");
        }
    }
}
