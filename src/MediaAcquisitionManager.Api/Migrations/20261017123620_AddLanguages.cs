using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaAcquisitionManager.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLanguages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "Users",
                type: "TEXT",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CatalogLanguage",
                table: "IntegrationConnections",
                type: "TEXT",
                maxLength: 16,
                nullable: true);

            // MAM-46 — every TMDb integration stored before this asked TMDb in Ukrainian (the adapter's uk-UA constant),
            // so it keeps Ukrainian; every other type has no catalog language and stays null. EF's own UpdateData by the
            // type column, not a statement written by hand (R22).
            migrationBuilder.UpdateData(
                table: "IntegrationConnections",
                keyColumn: "AdapterType",
                keyValue: "tmdb",
                column: "CatalogLanguage",
                value: "uk-UA");

            // MAM-46 — every catalog row cached before this was read in Ukrainian, so it stays fresh in it and production
            // reads nothing again on the upgrade.
            migrationBuilder.AddColumn<string>(
                name: "DetailsLanguage",
                table: "CatalogWorks",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "uk-UA");

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "CatalogPeople",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "uk-UA");

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "CatalogGenres",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "uk-UA");

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "CatalogCollections",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "uk-UA");

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "CatalogLookups",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "uk-UA");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Language",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CatalogLanguage",
                table: "IntegrationConnections");

            migrationBuilder.DropColumn(
                name: "DetailsLanguage",
                table: "CatalogWorks");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "CatalogPeople");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "CatalogGenres");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "CatalogCollections");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "CatalogLookups");
        }
    }
}
