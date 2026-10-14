using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaAcquisitionManager.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDownloadJobDestinationKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DestinationKind",
                table: "DownloadJobs",
                type: "TEXT",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DestinationKind",
                table: "DownloadJobs");
        }
    }
}
