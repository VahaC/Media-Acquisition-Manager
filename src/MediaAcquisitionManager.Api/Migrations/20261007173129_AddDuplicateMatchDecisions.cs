using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaAcquisitionManager.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDuplicateMatchDecisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DuplicateMatchDecisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    GroupKey = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Decision = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    DecidedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DecidedByUserName = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    DecidedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DuplicateMatchDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DuplicateMatchDecisions_Users_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DuplicateMatchDecisions_DecidedByUserId",
                table: "DuplicateMatchDecisions",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DuplicateMatchDecisions_GroupKey",
                table: "DuplicateMatchDecisions",
                column: "GroupKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DuplicateMatchDecisions");
        }
    }
}
