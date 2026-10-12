using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaAcquisitionManager.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCopyRemovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CopyRemovals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RequestedByUserName = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StateChangedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    GroupClass = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    GroupKey = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PhysicalFileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AppPath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    ServicePath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    QuarantinedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LibraryNoticeDueUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CopyRemovals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CopyRemovals_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CopyRemovalNames",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CopyRemovalId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
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
                    table.PrimaryKey("PK_CopyRemovalNames", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CopyRemovalNames_CopyRemovals_CopyRemovalId",
                        column: x => x.CopyRemovalId,
                        principalTable: "CopyRemovals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CopyRemovalNames_CopyRemovalId_Position",
                table: "CopyRemovalNames",
                columns: new[] { "CopyRemovalId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CopyRemovals_Open",
                table: "CopyRemovals",
                column: "State",
                filter: "\"State\" IN ('Moving', 'Quarantined', 'NeedsReview', 'Restoring', 'Deleting')");

            migrationBuilder.CreateIndex(
                name: "IX_CopyRemovals_RequestedByUserId",
                table: "CopyRemovals",
                column: "RequestedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CopyRemovalNames");

            migrationBuilder.DropTable(
                name: "CopyRemovals");
        }
    }
}
