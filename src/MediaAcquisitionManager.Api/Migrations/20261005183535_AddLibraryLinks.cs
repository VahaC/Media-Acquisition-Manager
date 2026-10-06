using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaAcquisitionManager.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DetailsVersion",
                table: "CatalogWorks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Overview",
                table: "CatalogWorks",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RuntimeMinutes",
                table: "CatalogWorks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuditEventIdentifiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AuditEventId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Scheme = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEventIdentifiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditEventIdentifiers_AuditEvents_AuditEventId",
                        column: x => x.AuditEventId,
                        principalTable: "AuditEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CatalogWorkAlternativeTitles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CatalogWorkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogWorkAlternativeTitles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogWorkAlternativeTitles_CatalogWorks_CatalogWorkId",
                        column: x => x.CatalogWorkId,
                        principalTable: "CatalogWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LibraryItemLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IntegrationConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceItemId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ItemKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    MediaWorkId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Target = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Scheme = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SeasonNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    EpisodeNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    CatalogTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ItemTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ImdbId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ImdbSourceConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ImdbSourceName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ImdbObservedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SupersededValue = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SetByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SetByUserName = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SetUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: true),
                    WriteState = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    WriteAttemptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WriteError = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    WriteStateUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryItemLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LibraryItemLinks_IntegrationConnections_ImdbSourceConnectionId",
                        column: x => x.ImdbSourceConnectionId,
                        principalTable: "IntegrationConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LibraryItemLinks_IntegrationConnections_IntegrationConnectionId",
                        column: x => x.IntegrationConnectionId,
                        principalTable: "IntegrationConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LibraryItemLinks_MediaWorks_MediaWorkId",
                        column: x => x.MediaWorkId,
                        principalTable: "MediaWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LibraryItemLinks_Users_SetByUserId",
                        column: x => x.SetByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "LibraryLinkSearches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MediaWorkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QueryKey = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    SearchedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NoneFitsUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NoneFitsByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryLinkSearches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LibraryLinkSearches_MediaWorks_MediaWorkId",
                        column: x => x.MediaWorkId,
                        principalTable: "MediaWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LibraryLinkSearches_Users_NoneFitsByUserId",
                        column: x => x.NoneFitsByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "LibraryLinkSuggestionRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Trigger = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RequestedByUserName = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    StartedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FinishedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    WorksTotal = table.Column<int>(type: "INTEGER", nullable: true),
                    WorksDone = table.Column<int>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryLinkSuggestionRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LibraryLinkSuggestionRuns_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "LibraryItemLinkFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LibraryItemLinkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<long>(type: "INTEGER", nullable: false),
                    Inode = table.Column<long>(type: "INTEGER", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryItemLinkFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LibraryItemLinkFiles_LibraryItemLinks_LibraryItemLinkId",
                        column: x => x.LibraryItemLinkId,
                        principalTable: "LibraryItemLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LibraryLinkCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    LibraryLinkSearchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    CatalogKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CatalogId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryLinkCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LibraryLinkCandidates_LibraryLinkSearches_LibraryLinkSearchId",
                        column: x => x.LibraryLinkSearchId,
                        principalTable: "LibraryLinkSearches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEventIdentifiers_AuditEventId_Position",
                table: "AuditEventIdentifiers",
                columns: new[] { "AuditEventId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogWorkAlternativeTitles_CatalogWorkId_Position",
                table: "CatalogWorkAlternativeTitles",
                columns: new[] { "CatalogWorkId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryItemLinkFiles_DeviceId_Inode",
                table: "LibraryItemLinkFiles",
                columns: new[] { "DeviceId", "Inode" });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryItemLinkFiles_LibraryItemLinkId_DeviceId_Inode_SizeBytes",
                table: "LibraryItemLinkFiles",
                columns: new[] { "LibraryItemLinkId", "DeviceId", "Inode", "SizeBytes" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryItemLinks_BatchId",
                table: "LibraryItemLinks",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryItemLinks_ImdbSourceConnectionId",
                table: "LibraryItemLinks",
                column: "ImdbSourceConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryItemLinks_MediaWorkId",
                table: "LibraryItemLinks",
                column: "MediaWorkId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryItemLinks_OneLinkPerItem",
                table: "LibraryItemLinks",
                columns: new[] { "IntegrationConnectionId", "SourceItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryItemLinks_SetByUserId",
                table: "LibraryItemLinks",
                column: "SetByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryItemLinks_WriteState",
                table: "LibraryItemLinks",
                column: "WriteState");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryLinkCandidates_LibraryLinkSearchId_CatalogKind_CatalogId",
                table: "LibraryLinkCandidates",
                columns: new[] { "LibraryLinkSearchId", "CatalogKind", "CatalogId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryLinkSearches_MediaWorkId",
                table: "LibraryLinkSearches",
                column: "MediaWorkId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryLinkSearches_NoneFitsByUserId",
                table: "LibraryLinkSearches",
                column: "NoneFitsByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryLinkSuggestionRuns_CreatedUtc",
                table: "LibraryLinkSuggestionRuns",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryLinkSuggestionRuns_OneRunPerActiveStatus",
                table: "LibraryLinkSuggestionRuns",
                column: "Status",
                unique: true,
                filter: "\"Status\" IN ('Queued', 'Running')");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryLinkSuggestionRuns_RequestedByUserId",
                table: "LibraryLinkSuggestionRuns",
                column: "RequestedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEventIdentifiers");

            migrationBuilder.DropTable(
                name: "CatalogWorkAlternativeTitles");

            migrationBuilder.DropTable(
                name: "LibraryItemLinkFiles");

            migrationBuilder.DropTable(
                name: "LibraryLinkCandidates");

            migrationBuilder.DropTable(
                name: "LibraryLinkSuggestionRuns");

            migrationBuilder.DropTable(
                name: "LibraryItemLinks");

            migrationBuilder.DropTable(
                name: "LibraryLinkSearches");

            migrationBuilder.DropColumn(
                name: "DetailsVersion",
                table: "CatalogWorks");

            migrationBuilder.DropColumn(
                name: "Overview",
                table: "CatalogWorks");

            migrationBuilder.DropColumn(
                name: "RuntimeMinutes",
                table: "CatalogWorks");
        }
    }
}
