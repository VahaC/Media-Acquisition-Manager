using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaAcquisitionManager.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddReplacementQuarantine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LibraryNoticeDueUtc",
                table: "ReplacementJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QuarantineCheckedUtc",
                table: "ReplacementJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "QuarantinedUtc",
                table: "ReplacementJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReplacementJobNames",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReplacementJobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    ReplacementJobFileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AppPath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    QuarantinePath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    AllowedRoot = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    DeviceId = table.Column<long>(type: "INTEGER", nullable: false),
                    Inode = table.Column<long>(type: "INTEGER", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LinkCount = table.Column<int>(type: "INTEGER", nullable: true),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Problem = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    MovedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RestoredUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DeletedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReplacementJobNames", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReplacementJobNames_ReplacementJobs_ReplacementJobId",
                        column: x => x.ReplacementJobId,
                        principalTable: "ReplacementJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReplacementJobs_Quarantine",
                table: "ReplacementJobs",
                column: "State",
                filter: "\"State\" IN ('Verified', 'Quarantining')");

            migrationBuilder.CreateIndex(
                name: "IX_ReplacementJobNames_ReplacementJobId_Position",
                table: "ReplacementJobNames",
                columns: new[] { "ReplacementJobId", "Position" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReplacementJobNames");

            migrationBuilder.DropIndex(
                name: "IX_ReplacementJobs_Quarantine",
                table: "ReplacementJobs");

            migrationBuilder.DropColumn(
                name: "LibraryNoticeDueUtc",
                table: "ReplacementJobs");

            migrationBuilder.DropColumn(
                name: "QuarantineCheckedUtc",
                table: "ReplacementJobs");

            migrationBuilder.DropColumn(
                name: "QuarantinedUtc",
                table: "ReplacementJobs");
        }
    }
}
