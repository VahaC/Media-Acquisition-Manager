using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaAcquisitionManager.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryPosters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MediaWorkPosters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MediaWorkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Tag = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SourceConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SourceItemId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ObservedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaWorkPosters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaWorkPosters_IntegrationConnections_SourceConnectionId",
                        column: x => x.SourceConnectionId,
                        principalTable: "IntegrationConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MediaWorkPosters_MediaWorks_MediaWorkId",
                        column: x => x.MediaWorkId,
                        principalTable: "MediaWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaWorkPosters_MediaWorkId",
                table: "MediaWorkPosters",
                column: "MediaWorkId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaWorkPosters_SourceConnectionId_SourceItemId",
                table: "MediaWorkPosters",
                columns: new[] { "SourceConnectionId", "SourceItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaWorkPosters_SourceItemId_Tag",
                table: "MediaWorkPosters",
                columns: new[] { "SourceItemId", "Tag" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaWorkPosters");
        }
    }
}
