using MediaAcquisitionManager.Core.Texts;
using MediaAcquisitionManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MediaAcquisitionManager.Api.Tests.Conventions;

/// <summary>
/// MAM-47 (ADR 0042) — the database keeps no sentence: the 21 columns that held one (design §5.1) hold a message, each
/// still the 1000-character text column it was, so no migration rebuilds a table (ADR 0009) and nullness keeps its
/// meaning. Pinned as a list, so the startup converter's model-driven walk can't silently shrink.
/// </summary>
public sealed class MessageColumnsTests
{
    private const int ColumnLength = 1000;

    /// <summary>The columns, and whether each may be null (only the consent words are required).</summary>
    private static readonly (string Table, string Column, bool Nullable)[] MessageColumns =
    [
        ("DownloadJobs", "Error", true),
        ("DownloadJobs", "BlockReason", true),
        ("SearchSources", "Error", true),
        ("ReplacementJobs", "Reason", true),
        ("ReplacementJobFiles", "Problem", true),
        ("ReplacementJobNewFiles", "Problem", true),
        ("ReplacementJobNames", "Problem", true),
        ("CopyRemovalNames", "Problem", true),
        ("CopyRemovals", "Reason", true),
        ("ReplacementJobWorks", "ConfirmationText", false),
        ("LibraryItemLinks", "WriteError", true),
        ("LibraryItemLinks", "LookupError", true),
        ("PhysicalFiles", "InspectionError", true),
        ("MediaInspections", "Error", true),
        ("FileInspectionRuns", "Error", true),
        ("FileInspectionRuns", "HashProblem", true),
        ("FolderScanRuns", "Error", true),
        ("FolderScanRuns", "Problem", true),
        ("LibrarySyncRuns", "Error", true),
        ("LibraryLinkSuggestionRuns", "Error", true),
        ("RecommendationRuns", "Error", true),
    ];

    [Fact]
    public void Every_message_column_is_a_message()
    {
        //Arrange
        var model = Model();

        //Act
        var wrong = MessageColumns
            .Select(column => (column.Table, column.Column, Property: PropertyOf(model, column.Table, column.Column)))
            .Where(column => column.Property?.ClrType != typeof(Message))
            .Select(column => $"{column.Table}.{column.Column}: {column.Property?.ClrType.Name ?? "no such column"}")
            .ToList();

        //Assert
        Assert.True(wrong.Count == 0, $"Columns that should hold a message:{Environment.NewLine}{string.Join(Environment.NewLine, wrong)}");
    }

    [Fact]
    public void Every_message_column_stays_the_text_column_of_1000_characters_it_was()
    {
        //Arrange
        var model = Model();

        //Act
        var wrong = MessageColumns
            .Select(column => (column.Table, column.Column, column.Nullable, Property: PropertyOf(model, column.Table, column.Column)))
            .Where(column => column.Property is null
                || column.Property.GetMaxLength() != ColumnLength
                || (column.Property.GetValueConverter()?.ProviderClrType ?? column.Property.ClrType) != typeof(string)
                || column.Property.IsNullable != column.Nullable)
            .Select(column => $"{column.Table}.{column.Column}")
            .ToList();

        //Assert
        Assert.True(wrong.Count == 0, $"Columns that are no longer the nullable-as-before TEXT of {ColumnLength}: {string.Join(", ", wrong)}");
    }

    [Fact]
    public void No_other_column_holds_a_message()
    {
        //Arrange
        var listed = MessageColumns.Select(column => (column.Table, column.Column)).ToHashSet();

        //Act
        var others = Model().GetEntityTypes()
            .SelectMany(type => type.GetProperties().Select(property => (Table: type.GetTableName(), Column: property.GetColumnName(), property.ClrType)))
            .Where(column => column.ClrType == typeof(Message) && !listed.Contains((column.Table!, column.Column)))
            .Select(column => $"{column.Table}.{column.Column}")
            .ToList();

        //Assert
        Assert.True(others.Count == 0, $"Message columns missing from this list (and from the startup converter's design, §5.1): {string.Join(", ", others)}");
    }

    private static IModel Model()
    {
        // The model is built from the entities and the conventions without opening the connection.
        using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite("Data Source=:memory:").Options);
        return db.Model;
    }

    private static IProperty? PropertyOf(IModel model, string table, string column) =>
        model.GetEntityTypes()
            .Where(type => type.GetTableName() == table)
            .SelectMany(type => type.GetProperties())
            .FirstOrDefault(property => property.GetColumnName() == column);
}
