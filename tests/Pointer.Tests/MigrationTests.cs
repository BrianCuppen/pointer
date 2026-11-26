using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Pointer.Tests;

[Collection(PostgresCollection.Name)]
public sealed class MigrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Model_has_no_changes_missing_from_migrations()
    {
        await using var db = fixture.CreateContext();
        var differ = db.GetService<IMigrationsModelDiffer>();
        IModel snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
        if (snapshot is IMutableModel mutable)
            snapshot = mutable.FinalizeModel();
        var snapshotModel = db.GetService<IModelRuntimeInitializer>().Initialize(snapshot);
        var designModel = db.GetService<IDesignTimeModel>().Model;

        var diff = differ.GetDifferences(snapshotModel.GetRelationalModel(), designModel.GetRelationalModel());

        Assert.Empty(diff);
    }
}
