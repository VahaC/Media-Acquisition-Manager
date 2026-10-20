using MediaAcquisitionManager.TestInfrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MediaAcquisitionManager.Infrastructure.Tests.Persistence;

// MAM-47 (ADR 0042, ADR 0009) — the message columns are typed through a convention whose provider type is the string
// the column always was, so the model matches the last migration's snapshot: no migration, no table rebuild, and an
// older image still opens the database. Fails the moment a model change needs a migration nobody added.
public sealed class ModelHasNoPendingChangesTests : DatabaseTestBase
{
    [Fact]
    public void The_model_matches_the_last_migrations_snapshot()
    {
        //Arrange
        using var db = CreateContext();

        //Act
        var pending = db.Database.HasPendingModelChanges();

        //Assert
        Assert.False(pending);
    }
}
