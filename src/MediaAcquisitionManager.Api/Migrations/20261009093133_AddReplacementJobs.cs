using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaAcquisitionManager.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddReplacementJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PayloadKind",
                table: "DownloadJobs",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReplacementJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DownloadJobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RequestedByUserName = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StateChangedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ReleaseEditions = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplacementJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReplacementJobs_DownloadJobs_DownloadJobId",
                        column: x => x.DownloadJobId,
                        principalTable: "DownloadJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReplacementJobs_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ReplacementJobFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReplacementJobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    PhysicalFileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MediaVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MediaWorkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MatchedWorkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SeasonNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    EpisodeNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    ServicePath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    AppPath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    DeviceId = table.Column<long>(type: "INTEGER", nullable: false),
                    Inode = table.Column<long>(type: "INTEGER", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LinkCount = table.Column<int>(type: "INTEGER", nullable: true),
                    EditionLabel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplacementJobFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReplacementJobFiles_ReplacementJobs_ReplacementJobId",
                        column: x => x.ReplacementJobId,
                        principalTable: "ReplacementJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReplacementJobWorks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReplacementJobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MediaWorkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    Evidence = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ConfirmedByOwner = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConfirmationText = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplacementJobWorks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReplacementJobWorks_ReplacementJobs_ReplacementJobId",
                        column: x => x.ReplacementJobId,
                        principalTable: "ReplacementJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReplacementJobWorkIdentifiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReplacementJobWorkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Scheme = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SetByOwner = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplacementJobWorkIdentifiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReplacementJobWorkIdentifiers_ReplacementJobWorks_ReplacementJobWorkId",
                        column: x => x.ReplacementJobWorkId,
                        principalTable: "ReplacementJobWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReplacementJobFiles_ReplacementJobId_Position",
                table: "ReplacementJobFiles",
                columns: new[] { "ReplacementJobId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReplacementJobs_DownloadJobId",
                table: "ReplacementJobs",
                column: "DownloadJobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReplacementJobs_Followed",
                table: "ReplacementJobs",
                column: "State",
                filter: "\"State\" IN ('Scheduled', 'Submitted', 'Downloading', 'Paused', 'NeedsReview')");

            migrationBuilder.CreateIndex(
                name: "IX_ReplacementJobs_RequestedByUserId",
                table: "ReplacementJobs",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReplacementJobWorkIdentifiers_ReplacementJobWorkId",
                table: "ReplacementJobWorkIdentifiers",
                column: "ReplacementJobWorkId");

            migrationBuilder.CreateIndex(
                name: "IX_ReplacementJobWorks_ReplacementJobId_MediaWorkId",
                table: "ReplacementJobWorks",
                columns: new[] { "ReplacementJobId", "MediaWorkId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReplacementJobFiles");

            migrationBuilder.DropTable(
                name: "ReplacementJobWorkIdentifiers");

            migrationBuilder.DropTable(
                name: "ReplacementJobWorks");

            migrationBuilder.DropTable(
                name: "ReplacementJobs");

            migrationBuilder.DropColumn(
                name: "PayloadKind",
                table: "DownloadJobs");
        }
    }
}
