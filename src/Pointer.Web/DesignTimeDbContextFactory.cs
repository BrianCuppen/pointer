using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pointer.Core.Data;

namespace Pointer.Web;

/// <summary>Used by <c>dotnet ef</c> only. Generating a migration needs no database connection.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PointerDbContext>
{
    public PointerDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Database=pointer_design_time";
        var options = new DbContextOptionsBuilder<PointerDbContext>().UsePointerDatabase(connectionString).Options;
        return new PointerDbContext(options);
    }
}
