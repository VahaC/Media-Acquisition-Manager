using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaAcquisitionManager.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogWorkOverviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CatalogWorkOverviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CatalogWorkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Language = table.Column<string>(type: "TEXT", maxLength: 5, nullable: false),
                    Overview = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    FetchedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogWorkOverviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogWorkOverviews_CatalogWorks_CatalogWorkId",
                        column: x => x.CatalogWorkId,
                        principalTable: "CatalogWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogWorkOverviews_CatalogWorkId_Language",
                table: "CatalogWorkOverviews",
                columns: new[] { "CatalogWorkId", "Language" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogWorkOverviews");
        }
    }
}
