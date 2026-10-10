using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaAcquisitionManager.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddReplacementVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "InspectionStartedUtc",
                table: "ReplacementJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LibraryAskedUtc",
                table: "ReplacementJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DurationMilliseconds",
                table: "ReplacementJobFiles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EpisodeNumberEnd",
                table: "ReplacementJobFiles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PairedNewFileId",
                table: "ReplacementJobFiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Problem",
                table: "ReplacementJobFiles",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReplacementJobNewFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReplacementJobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    ClientSizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    ClientProgress = table.Column<double>(type: "REAL", nullable: true),
                    AppPath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    LibraryIntegrationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LibraryPath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    DeviceId = table.Column<long>(type: "INTEGER", nullable: true),
                    Inode = table.Column<long>(type: "INTEGER", nullable: true),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DurationMilliseconds = table.Column<long>(type: "INTEGER", nullable: true),
                    VideoCodec = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Width = table.Column<int>(type: "INTEGER", nullable: true),
                    Height = table.Column<int>(type: "INTEGER", nullable: true),
                    CheckedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LibraryItemId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Refreshed = table.Column<bool>(type: "INTEGER", nullable: false),
                    SeriesItemId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    SeasonNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    EpisodeNumber = table.Column<int>(type: "INTEGER", nullable: true),
                    EpisodeNumberEnd = table.Column<int>(type: "INTEGER", nullable: true),
                    ReadUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MediaWorkId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Problem = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplacementJobNewFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReplacementJobNewFiles_ReplacementJobs_ReplacementJobId",
                        column: x => x.ReplacementJobId,
                        principalTable: "ReplacementJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReplacementJobNewFileIdentifiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReplacementJobNewFileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Scheme = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    OfSeries = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplacementJobNewFileIdentifiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReplacementJobNewFileIdentifiers_ReplacementJobNewFiles_ReplacementJobNewFileId",
                        column: x => x.ReplacementJobNewFileId,
                        principalTable: "ReplacementJobNewFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReplacementJobs_Verifying",
                table: "ReplacementJobs",
                column: "State",
                filter: "\"State\" IN ('Downloaded', 'Inspecting')");

            migrationBuilder.CreateIndex(
                name: "IX_ReplacementJobNewFileIdentifiers_ReplacementJobNewFileId",
                table: "ReplacementJobNewFileIdentifiers",
                column: "ReplacementJobNewFileId");

            migrationBuilder.CreateIndex(
                name: "IX_ReplacementJobNewFiles_ReplacementJobId_Position",
                table: "ReplacementJobNewFiles",
                columns: new[] { "ReplacementJobId", "Position" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReplacementJobNewFileIdentifiers");

            migrationBuilder.DropTable(
                name: "ReplacementJobNewFiles");

            migrationBuilder.DropIndex(
                name: "IX_ReplacementJobs_Verifying",
                table: "ReplacementJobs");

            migrationBuilder.DropColumn(
                name: "InspectionStartedUtc",
                table: "ReplacementJobs");

            migrationBuilder.DropColumn(
                name: "LibraryAskedUtc",
                table: "ReplacementJobs");

            migrationBuilder.DropColumn(
                name: "DurationMilliseconds",
                table: "ReplacementJobFiles");

            migrationBuilder.DropColumn(
                name: "EpisodeNumberEnd",
                table: "ReplacementJobFiles");

            migrationBuilder.DropColumn(
                name: "PairedNewFileId",
                table: "ReplacementJobFiles");

            migrationBuilder.DropColumn(
                name: "Problem",
                table: "ReplacementJobFiles");
        }
    }
}
