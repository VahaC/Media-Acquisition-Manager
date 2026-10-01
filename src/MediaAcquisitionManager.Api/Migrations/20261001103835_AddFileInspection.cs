using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaAcquisitionManager.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFileInspection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "InspectedUtc",
                table: "PhysicalFiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InspectionError",
                table: "PhysicalFiles",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InspectionStatus",
                table: "PhysicalFiles",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LinkCount",
                table: "PhysicalFiles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "MediaInspectionId",
                table: "PhysicalFiles",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AllowedRoots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllowedRoots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FileInspectionRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Trigger = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RequestedByUserName = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FinishedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FilesTotal = table.Column<int>(type: "INTEGER", nullable: true),
                    FilesChecked = table.Column<int>(type: "INTEGER", nullable: false),
                    FilesProbed = table.Column<int>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileInspectionRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileInspectionRuns_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MediaInspections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceId = table.Column<long>(type: "INTEGER", nullable: false),
                    Inode = table.Column<long>(type: "INTEGER", nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ModifiedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    DurationMilliseconds = table.Column<long>(type: "INTEGER", nullable: true),
                    BitRate = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaInspections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MediaInspectionTracks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MediaInspectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Codec = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Profile = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Width = table.Column<int>(type: "INTEGER", nullable: true),
                    Height = table.Column<int>(type: "INTEGER", nullable: true),
                    DynamicRange = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Channels = table.Column<int>(type: "INTEGER", nullable: true),
                    ChannelLayout = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Language = table.Column<string>(type: "TEXT", maxLength: 35, nullable: true),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsForced = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsHearingImpaired = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaInspectionTracks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaInspectionTracks_MediaInspections_MediaInspectionId",
                        column: x => x.MediaInspectionId,
                        principalTable: "MediaInspections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhysicalFiles_MediaInspectionId",
                table: "PhysicalFiles",
                column: "MediaInspectionId");

            migrationBuilder.CreateIndex(
                name: "IX_AllowedRoots_Path",
                table: "AllowedRoots",
                column: "Path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FileInspectionRuns_CreatedUtc",
                table: "FileInspectionRuns",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FileInspectionRuns_OneRunPerActiveStatus",
                table: "FileInspectionRuns",
                column: "Status",
                unique: true,
                filter: "\"Status\" IN ('Queued', 'Running')");

            migrationBuilder.CreateIndex(
                name: "IX_FileInspectionRuns_RequestedByUserId",
                table: "FileInspectionRuns",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaInspections_DeviceId_Inode_SizeBytes_ModifiedUtc",
                table: "MediaInspections",
                columns: new[] { "DeviceId", "Inode", "SizeBytes", "ModifiedUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaInspectionTracks_MediaInspectionId",
                table: "MediaInspectionTracks",
                column: "MediaInspectionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AllowedRoots");

            migrationBuilder.DropTable(
                name: "FileInspectionRuns");

            migrationBuilder.DropTable(
                name: "MediaInspectionTracks");

            migrationBuilder.DropTable(
                name: "MediaInspections");

            migrationBuilder.DropIndex(
                name: "IX_PhysicalFiles_MediaInspectionId",
                table: "PhysicalFiles");

            migrationBuilder.DropColumn(
                name: "InspectedUtc",
                table: "PhysicalFiles");

            migrationBuilder.DropColumn(
                name: "InspectionError",
                table: "PhysicalFiles");

            migrationBuilder.DropColumn(
                name: "InspectionStatus",
                table: "PhysicalFiles");

            migrationBuilder.DropColumn(
                name: "LinkCount",
                table: "PhysicalFiles");

            migrationBuilder.DropColumn(
                name: "MediaInspectionId",
                table: "PhysicalFiles");
        }
    }
}
