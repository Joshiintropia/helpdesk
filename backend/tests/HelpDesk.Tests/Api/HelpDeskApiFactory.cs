using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;

namespace HelpDesk.Tests.Api;

/// <summary>
/// Runs the real API against a throwaway PostgreSQL database. Set HELPDESK_TEST_DB to point at another
/// server; by default it uses the same local credentials as docker-compose.
/// </summary>
public class HelpDeskApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AdminEmail = "admin@test.local";
    public const string Password = "Contrasena123";

    private readonly string _database = $"helpdesk_test_{Guid.NewGuid():N}";

    private static string ServerConnection =>
        Environment.GetEnvironmentVariable("HELPDESK_TEST_DB")
        ?? "Host=localhost;Port=5432;Username=helpdesk;Password=helpdesk";

    private string DatabaseConnection => new NpgsqlConnectionStringBuilder(ServerConnection) { Database = _database }.ConnectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:HelpDesk", DatabaseConnection);
        builder.UseSetting("Auth:AllowSelfRegistration", "true");
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
        builder.UseSetting("Seed:AdminPassword", Password);
    }

    /// <summary>A client with its own cookie jar, already signed in.</summary>
    public async Task<HttpClient> SignInAsync(string email, bool register = true)
    {
        var client = CreateClient();
        if (register)
        {
            var registered = await client.PostAsJsonAsync("/api/auth/register", new { email, password = Password });
            registered.EnsureSuccessStatusCode();
        }

        var login = await client.PostAsJsonAsync("/api/auth/login?useCookies=true", new { email, password = Password });
        login.EnsureSuccessStatusCode();
        return client;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        var maintenance = new NpgsqlConnectionStringBuilder(ServerConnection) { Database = "postgres" }.ConnectionString;
        await using var connection = new NpgsqlConnection(maintenance);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }
}
