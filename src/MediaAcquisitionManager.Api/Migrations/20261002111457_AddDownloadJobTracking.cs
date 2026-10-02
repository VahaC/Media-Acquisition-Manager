using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaAcquisitionManager.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDownloadJobTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BlockReason",
                table: "DownloadJobs",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BlockReasonOrigin",
                table: "DownloadJobs",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Progress",
                table: "DownloadJobs",
                type: "REAL",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DownloadJobFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DownloadJobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    Progress = table.Column<double>(type: "REAL", nullable: true),
                    IsWanted = table.Column<bool>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadJobFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DownloadJobFiles_DownloadJobs_DownloadJobId",
                        column: x => x.DownloadJobId,
                        principalTable: "DownloadJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadJobs_Followed",
                table: "DownloadJobs",
                column: "Status",
                filter: "\"Status\" IN ('Created', 'Submitted', 'Downloading', 'Paused')");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadJobs_RequestedByUserId_CreatedUtc",
                table: "DownloadJobs",
                columns: new[] { "RequestedByUserId", "CreatedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadJobFiles_DownloadJobId_Position",
                table: "DownloadJobFiles",
                columns: new[] { "DownloadJobId", "Position" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DownloadJobFiles");

            migrationBuilder.DropIndex(
                name: "IX_DownloadJobs_Followed",
                table: "DownloadJobs");

            migrationBuilder.DropIndex(
                name: "IX_DownloadJobs_RequestedByUserId_CreatedUtc",
                table: "DownloadJobs");

            migrationBuilder.DropColumn(
                name: "BlockReason",
                table: "DownloadJobs");

            migrationBuilder.DropColumn(
                name: "BlockReasonOrigin",
                table: "DownloadJobs");

            migrationBuilder.DropColumn(
                name: "Progress",
                table: "DownloadJobs");
        }
    }
}
