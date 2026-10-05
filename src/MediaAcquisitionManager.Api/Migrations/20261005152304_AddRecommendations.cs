using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediaAcquisitionManager.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRecommendations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CatalogCollections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CatalogId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SourceConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SourceItemId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ObservedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogCollections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogCollections_IntegrationConnections_SourceConnectionId",
                        column: x => x.SourceConnectionId,
                        principalTable: "IntegrationConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CatalogGenres",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    GenreId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SourceConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SourceItemId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ObservedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogGenres", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogGenres_IntegrationConnections_SourceConnectionId",
                        column: x => x.SourceConnectionId,
                        principalTable: "IntegrationConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CatalogLookups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    QueryKey = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    ResultKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    ResultCatalogId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Evidence = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SourceConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SourceItemId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ObservedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogLookups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogLookups_IntegrationConnections_SourceConnectionId",
                        column: x => x.SourceConnectionId,
                        principalTable: "IntegrationConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CatalogPeople",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CatalogId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SourceConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SourceItemId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ObservedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogPeople", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogPeople_IntegrationConnections_SourceConnectionId",
                        column: x => x.SourceConnectionId,
                        principalTable: "IntegrationConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CatalogWorks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CatalogId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    OriginalTitle = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    OriginalLanguage = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    ReleaseDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Rating = table.Column<double>(type: "REAL", nullable: true),
                    VoteCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PosterPath = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ImdbId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CollectionId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    CollectionName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DetailsUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SourceConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SourceItemId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ObservedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogWorks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogWorks_IntegrationConnections_SourceConnectionId",
                        column: x => x.SourceConnectionId,
                        principalTable: "IntegrationConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RecommendationDismissals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CatalogId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationDismissals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecommendationDismissals_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecommendationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Trigger = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    RequestedByUserName = table.Column<string>(type: "TEXT", maxLength: 32, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StartedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FinishedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SeedCount = table.Column<int>(type: "INTEGER", nullable: true),
                    CandidateCount = table.Column<int>(type: "INTEGER", nullable: true),
                    Error = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecommendationRuns_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RecommendationSearches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Tab = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CatalogWorkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SearchedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationSearches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecommendationSearches_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRecommendationSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MoviesEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    SeriesEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CartoonsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DocumentariesEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    DailySearchLimit = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRecommendationSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserRecommendationSettings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CatalogCollectionParts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CatalogCollectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    MovieCatalogId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogCollectionParts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogCollectionParts_CatalogCollections_CatalogCollectionId",
                        column: x => x.CatalogCollectionId,
                        principalTable: "CatalogCollections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CatalogPersonWorks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CatalogPersonId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CatalogId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogPersonWorks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogPersonWorks_CatalogPeople_CatalogPersonId",
                        column: x => x.CatalogPersonId,
                        principalTable: "CatalogPeople",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CatalogWorkGenres",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CatalogWorkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    GenreId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogWorkGenres", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogWorkGenres_CatalogWorks_CatalogWorkId",
                        column: x => x.CatalogWorkId,
                        principalTable: "CatalogWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CatalogWorkPeople",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CatalogWorkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    PersonCatalogId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogWorkPeople", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogWorkPeople_CatalogWorks_CatalogWorkId",
                        column: x => x.CatalogWorkId,
                        principalTable: "CatalogWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CatalogWorkRecommendations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CatalogWorkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CatalogId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogWorkRecommendations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogWorkRecommendations_CatalogWorks_CatalogWorkId",
                        column: x => x.CatalogWorkId,
                        principalTable: "CatalogWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CatalogWorkSeasons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CatalogWorkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    AirDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    EpisodeCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogWorkSeasons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CatalogWorkSeasons_CatalogWorks_CatalogWorkId",
                        column: x => x.CatalogWorkId,
                        principalTable: "CatalogWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecommendationCandidates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CatalogWorkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Tab = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Score = table.Column<double>(type: "REAL", nullable: false),
                    RequiresAvailability = table.Column<bool>(type: "INTEGER", nullable: false),
                    MissingSeasons = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecommendationCandidates_CatalogWorks_CatalogWorkId",
                        column: x => x.CatalogWorkId,
                        principalTable: "CatalogWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReleaseAvailabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CatalogWorkId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CheckedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Found = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReleaseCount = table.Column<int>(type: "INTEGER", nullable: false),
                    BestResolution = table.Column<int>(type: "INTEGER", nullable: true),
                    HasUkrainianAudio = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaxSeeders = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseAvailabilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReleaseAvailabilities_CatalogWorks_CatalogWorkId",
                        column: x => x.CatalogWorkId,
                        principalTable: "CatalogWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SourceFeedEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CategoryIds = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    PublishedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResolvedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CatalogWorkId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SourceKind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SourceConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SourceItemId = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    ObservedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceFeedEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SourceFeedEntries_CatalogWorks_CatalogWorkId",
                        column: x => x.CatalogWorkId,
                        principalTable: "CatalogWorks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SourceFeedEntries_IntegrationConnections_SourceConnectionId",
                        column: x => x.SourceConnectionId,
                        principalTable: "IntegrationConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecommendationReasons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecommendationCandidateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Held = table.Column<int>(type: "INTEGER", nullable: true),
                    Total = table.Column<int>(type: "INTEGER", nullable: true),
                    Titles = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Seasons = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    DateUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationReasons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecommendationReasons_RecommendationCandidates_RecommendationCandidateId",
                        column: x => x.RecommendationCandidateId,
                        principalTable: "RecommendationCandidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogCollectionParts_CatalogCollectionId_Position",
                table: "CatalogCollectionParts",
                columns: new[] { "CatalogCollectionId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogCollections_CatalogId",
                table: "CatalogCollections",
                column: "CatalogId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogCollections_SourceConnectionId_SourceItemId",
                table: "CatalogCollections",
                columns: new[] { "SourceConnectionId", "SourceItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogGenres_Kind_GenreId",
                table: "CatalogGenres",
                columns: new[] { "Kind", "GenreId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogGenres_SourceConnectionId_SourceItemId",
                table: "CatalogGenres",
                columns: new[] { "SourceConnectionId", "SourceItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogLookups_QueryKey",
                table: "CatalogLookups",
                column: "QueryKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogLookups_SourceConnectionId_SourceItemId",
                table: "CatalogLookups",
                columns: new[] { "SourceConnectionId", "SourceItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogPeople_CatalogId",
                table: "CatalogPeople",
                column: "CatalogId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogPeople_SourceConnectionId_SourceItemId",
                table: "CatalogPeople",
                columns: new[] { "SourceConnectionId", "SourceItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogPersonWorks_CatalogPersonId_Position",
                table: "CatalogPersonWorks",
                columns: new[] { "CatalogPersonId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogWorkGenres_CatalogWorkId_GenreId",
                table: "CatalogWorkGenres",
                columns: new[] { "CatalogWorkId", "GenreId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogWorkPeople_CatalogWorkId_Position",
                table: "CatalogWorkPeople",
                columns: new[] { "CatalogWorkId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogWorkRecommendations_CatalogWorkId_Position",
                table: "CatalogWorkRecommendations",
                columns: new[] { "CatalogWorkId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogWorks_Kind_CatalogId",
                table: "CatalogWorks",
                columns: new[] { "Kind", "CatalogId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CatalogWorks_SourceConnectionId_SourceItemId",
                table: "CatalogWorks",
                columns: new[] { "SourceConnectionId", "SourceItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_CatalogWorkSeasons_CatalogWorkId_Number",
                table: "CatalogWorkSeasons",
                columns: new[] { "CatalogWorkId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationCandidates_CatalogWorkId",
                table: "RecommendationCandidates",
                column: "CatalogWorkId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationCandidates_Tab_Score",
                table: "RecommendationCandidates",
                columns: new[] { "Tab", "Score" });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationDismissals_UserId_Kind_CatalogId",
                table: "RecommendationDismissals",
                columns: new[] { "UserId", "Kind", "CatalogId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationReasons_RecommendationCandidateId_Position",
                table: "RecommendationReasons",
                columns: new[] { "RecommendationCandidateId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationRuns_CreatedUtc",
                table: "RecommendationRuns",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationRuns_OneRunPerActiveStatus",
                table: "RecommendationRuns",
                column: "Status",
                unique: true,
                filter: "\"Status\" IN ('Queued', 'Running')");

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationRuns_RequestedByUserId",
                table: "RecommendationRuns",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationSearches_UserId_SearchedUtc",
                table: "RecommendationSearches",
                columns: new[] { "UserId", "SearchedUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseAvailabilities_CatalogWorkId",
                table: "ReleaseAvailabilities",
                column: "CatalogWorkId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceFeedEntries_CatalogWorkId",
                table: "SourceFeedEntries",
                column: "CatalogWorkId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceFeedEntries_SourceConnectionId_SourceItemId",
                table: "SourceFeedEntries",
                columns: new[] { "SourceConnectionId", "SourceItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRecommendationSettings_UserId",
                table: "UserRecommendationSettings",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CatalogCollectionParts");

            migrationBuilder.DropTable(
                name: "CatalogGenres");

            migrationBuilder.DropTable(
                name: "CatalogLookups");

            migrationBuilder.DropTable(
                name: "CatalogPersonWorks");

            migrationBuilder.DropTable(
                name: "CatalogWorkGenres");

            migrationBuilder.DropTable(
                name: "CatalogWorkPeople");

            migrationBuilder.DropTable(
                name: "CatalogWorkRecommendations");

            migrationBuilder.DropTable(
                name: "CatalogWorkSeasons");

            migrationBuilder.DropTable(
                name: "RecommendationDismissals");

            migrationBuilder.DropTable(
                name: "RecommendationReasons");

            migrationBuilder.DropTable(
                name: "RecommendationRuns");

            migrationBuilder.DropTable(
                name: "RecommendationSearches");

            migrationBuilder.DropTable(
                name: "ReleaseAvailabilities");

            migrationBuilder.DropTable(
                name: "SourceFeedEntries");

            migrationBuilder.DropTable(
                name: "UserRecommendationSettings");

            migrationBuilder.DropTable(
                name: "CatalogCollections");

            migrationBuilder.DropTable(
                name: "CatalogPeople");

            migrationBuilder.DropTable(
                name: "RecommendationCandidates");

            migrationBuilder.DropTable(
                name: "CatalogWorks");
        }
    }
}
