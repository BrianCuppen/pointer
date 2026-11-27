using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Pointer.Tests;

/// <summary>The real web app against the test database, with background work and demo data off.</summary>
public sealed class WebAppFactory(PostgresFixture fixture) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = fixture.ConnectionString,
            ["Notifications:WorkerEnabled"] = "false",
            ["Demo:Seed"] = "false",
            ["Logging:LogLevel:Default"] = "Warning",
        }));
        // Program reads the connection string while building services, before the in-memory
        // configuration above is applied, so also pass it the way production does.
        builder.UseSetting("ConnectionStrings:Default", fixture.ConnectionString);
    }
}
