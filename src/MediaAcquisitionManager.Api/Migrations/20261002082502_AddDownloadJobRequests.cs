using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaAcquisitionManager.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDownloadJobRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ErrorOrigin",
                table: "DownloadJobs",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InfoHashV1",
                table: "DownloadJobs",
                type: "TEXT",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InfoHashV2",
                table: "DownloadJobs",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequestId",
                table: "DownloadJobs",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "RequestedByUserId",
                table: "DownloadJobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequestedByUserName",
                table: "DownloadJobs",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DownloadJobs_RequestedByUserId_RequestId",
                table: "DownloadJobs",
                columns: new[] { "RequestedByUserId", "RequestId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DownloadJobs_Users_RequestedByUserId",
                table: "DownloadJobs",
                column: "RequestedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DownloadJobs_Users_RequestedByUserId",
                table: "DownloadJobs");

            migrationBuilder.DropIndex(
                name: "IX_DownloadJobs_RequestedByUserId_RequestId",
                table: "DownloadJobs");

            migrationBuilder.DropColumn(
                name: "ErrorOrigin",
                table: "DownloadJobs");

            migrationBuilder.DropColumn(
                name: "InfoHashV1",
                table: "DownloadJobs");

            migrationBuilder.DropColumn(
                name: "InfoHashV2",
                table: "DownloadJobs");

            migrationBuilder.DropColumn(
                name: "RequestId",
                table: "DownloadJobs");

            migrationBuilder.DropColumn(
                name: "RequestedByUserId",
                table: "DownloadJobs");

            migrationBuilder.DropColumn(
                name: "RequestedByUserName",
                table: "DownloadJobs");
        }
    }
}
