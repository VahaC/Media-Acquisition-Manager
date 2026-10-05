using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaAcquisitionManager.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFileHashes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "HashCandidates",
                table: "FileInspectionRuns",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HashCandidatesChecked",
                table: "FileInspectionRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "HashCandidatesRead",
                table: "FileInspectionRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "HashProblem",
                table: "FileInspectionRuns",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HashProblemCount",
                table: "FileInspectionRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "FileHashes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<long>(type: "INTEGER", nullable: false),
                    Inode = table.Column<long>(type: "INTEGER", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileHashes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileHashes_DeviceId_Inode_SizeBytes_ModifiedUtc",
                table: "FileHashes",
                columns: new[] { "DeviceId", "Inode", "SizeBytes", "ModifiedUtc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileHashes");

            migrationBuilder.DropColumn(
                name: "HashCandidates",
                table: "FileInspectionRuns");

            migrationBuilder.DropColumn(
                name: "HashCandidatesChecked",
                table: "FileInspectionRuns");

            migrationBuilder.DropColumn(
                name: "HashCandidatesRead",
                table: "FileInspectionRuns");

            migrationBuilder.DropColumn(
                name: "HashProblem",
                table: "FileInspectionRuns");

            migrationBuilder.DropColumn(
                name: "HashProblemCount",
                table: "FileInspectionRuns");
        }
    }
}
