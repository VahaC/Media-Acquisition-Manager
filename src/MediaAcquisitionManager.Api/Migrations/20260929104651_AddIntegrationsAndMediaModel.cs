using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaAcquisitionManager.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationsAndMediaModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IntegrationConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AdapterType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    PasswordEncrypted = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    ApiKeyEncrypted = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MediaWorks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaWorks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DestinationMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DownloadClientConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ClientDestinationKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DestinationMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DestinationMappings_IntegrationConnections_DownloadClientConnectionId",
                        column: x => x.DownloadClientConnectionId,
                        principalTable: "IntegrationConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PathMappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IntegrationConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    ServicePrefix = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    AppPrefix = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PathMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PathMappings_IntegrationConnections_IntegrationConnectionId",
                        column: x => x.IntegrationConnectionId,
                        principalTable: "IntegrationConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Releases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    PublishedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SourcePageUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    PayloadKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    PayloadLocatorEncrypted = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: false),
                    InfoHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SourceConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SourceItemId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ObservedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Releases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Releases_IntegrationConnections_SourceConnectionId",
                        column: x => x.SourceConnectionId,
                        principalTable: "IntegrationConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MediaVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MediaWorkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceVersionId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EditionLabel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SourceConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SourceItemId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ObservedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaVersions_IntegrationConnections_SourceConnectionId",
                        column: x => x.SourceConnectionId,
                        principalTable: "IntegrationConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MediaVersions_MediaWorks_MediaWorkId",
                        column: x => x.MediaWorkId,
                        principalTable: "MediaWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaWorkIdentifiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MediaWorkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Scheme = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SourceConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SourceItemId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ObservedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaWorkIdentifiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaWorkIdentifiers_IntegrationConnections_SourceConnectionId",
                        column: x => x.SourceConnectionId,
                        principalTable: "IntegrationConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MediaWorkIdentifiers_MediaWorks_MediaWorkId",
                        column: x => x.MediaWorkId,
                        principalTable: "MediaWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaWorkTitles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MediaWorkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SourceConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SourceItemId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ObservedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaWorkTitles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaWorkTitles_IntegrationConnections_SourceConnectionId",
                        column: x => x.SourceConnectionId,
                        principalTable: "IntegrationConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MediaWorkTitles_MediaWorks_MediaWorkId",
                        column: x => x.MediaWorkId,
                        principalTable: "MediaWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DownloadJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReleaseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DownloadClientConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DownloadClientName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DestinationMappingId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DestinationName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    ClientDestinationKey = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ClientJobId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StatusChangedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DownloadJobs_DestinationMappings_DestinationMappingId",
                        column: x => x.DestinationMappingId,
                        principalTable: "DestinationMappings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DownloadJobs_IntegrationConnections_DownloadClientConnectionId",
                        column: x => x.DownloadClientConnectionId,
                        principalTable: "IntegrationConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DownloadJobs_Releases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "Releases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PhysicalFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MediaVersionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ServicePath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    DeviceId = table.Column<long>(type: "INTEGER", nullable: true),
                    Inode = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SourceConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SourceItemId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ObservedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhysicalFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhysicalFiles_IntegrationConnections_SourceConnectionId",
                        column: x => x.SourceConnectionId,
                        principalTable: "IntegrationConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PhysicalFiles_MediaVersions_MediaVersionId",
                        column: x => x.MediaVersionId,
                        principalTable: "MediaVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DestinationMappings_DownloadClientConnectionId_NormalizedName",
                table: "DestinationMappings",
                columns: new[] { "DownloadClientConnectionId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DownloadJobs_DestinationMappingId",
                table: "DownloadJobs",
                column: "DestinationMappingId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadJobs_DownloadClientConnectionId",
                table: "DownloadJobs",
                column: "DownloadClientConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadJobs_ReleaseId",
                table: "DownloadJobs",
                column: "ReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationConnections_Kind",
                table: "IntegrationConnections",
                column: "Kind");

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationConnections_NormalizedName",
                table: "IntegrationConnections",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaVersions_MediaWorkId",
                table: "MediaVersions",
                column: "MediaWorkId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaVersions_SourceConnectionId_SourceItemId",
                table: "MediaVersions",
                columns: new[] { "SourceConnectionId", "SourceItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaWorkIdentifiers_MediaWorkId",
                table: "MediaWorkIdentifiers",
                column: "MediaWorkId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaWorkIdentifiers_Scheme_Value",
                table: "MediaWorkIdentifiers",
                columns: new[] { "Scheme", "Value" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaWorkIdentifiers_SourceConnectionId_SourceItemId",
                table: "MediaWorkIdentifiers",
                columns: new[] { "SourceConnectionId", "SourceItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaWorkTitles_MediaWorkId",
                table: "MediaWorkTitles",
                column: "MediaWorkId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaWorkTitles_SourceConnectionId_SourceItemId",
                table: "MediaWorkTitles",
                columns: new[] { "SourceConnectionId", "SourceItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_PathMappings_IntegrationConnectionId_ServicePrefix",
                table: "PathMappings",
                columns: new[] { "IntegrationConnectionId", "ServicePrefix" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhysicalFiles_DeviceId_Inode",
                table: "PhysicalFiles",
                columns: new[] { "DeviceId", "Inode" });

            migrationBuilder.CreateIndex(
                name: "IX_PhysicalFiles_MediaVersionId",
                table: "PhysicalFiles",
                column: "MediaVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_PhysicalFiles_SourceConnectionId_SourceItemId",
                table: "PhysicalFiles",
                columns: new[] { "SourceConnectionId", "SourceItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_Releases_SourceConnectionId_SourceItemId",
                table: "Releases",
                columns: new[] { "SourceConnectionId", "SourceItemId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DownloadJobs");

            migrationBuilder.DropTable(
                name: "MediaWorkIdentifiers");

            migrationBuilder.DropTable(
                name: "MediaWorkTitles");

            migrationBuilder.DropTable(
                name: "PathMappings");

            migrationBuilder.DropTable(
                name: "PhysicalFiles");

            migrationBuilder.DropTable(
                name: "DestinationMappings");

            migrationBuilder.DropTable(
                name: "Releases");

            migrationBuilder.DropTable(
                name: "MediaVersions");

            migrationBuilder.DropTable(
                name: "IntegrationConnections");

            migrationBuilder.DropTable(
                name: "MediaWorks");
        }
    }
}
