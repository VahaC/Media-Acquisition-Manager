using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaAcquisitionManager.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFolderScan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // MAM-18 — SQLite changes a column only by rebuilding its table, so EF Core rebuilds PhysicalFiles
            // here (warning 20410), every row kept: a name the scan found that no library reported has no
            // version (ADR 0021).
            migrationBuilder.AlterColumn<Guid>(
                name: "MediaVersionId",
                table: "PhysicalFiles",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<DateTime>(
                name: "ReadModifiedUtc",
                table: "PhysicalFiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReadSizeBytes",
                table: "PhysicalFiles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FolderScanRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RequestedByUserName = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FinishedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FilesFound = table.Column<int>(type: "INTEGER", nullable: false),
                    FilesUnreported = table.Column<int>(type: "INTEGER", nullable: true),
                    ProblemCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Problem = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FolderScanRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FolderScanRuns_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FolderScanRuns_CreatedUtc",
                table: "FolderScanRuns",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FolderScanRuns_OneRunPerActiveStatus",
                table: "FolderScanRuns",
                column: "Status",
                unique: true,
                filter: "\"Status\" IN ('Queued', 'Running')");

            migrationBuilder.CreateIndex(
                name: "IX_FolderScanRuns_RequestedByUserId",
                table: "FolderScanRuns",
                column: "RequestedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FolderScanRuns");

            migrationBuilder.DropColumn(
                name: "ReadModifiedUtc",
                table: "PhysicalFiles");

            migrationBuilder.DropColumn(
                name: "ReadSizeBytes",
                table: "PhysicalFiles");

            migrationBuilder.AlterColumn<Guid>(
                name: "MediaVersionId",
                table: "PhysicalFiles",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
