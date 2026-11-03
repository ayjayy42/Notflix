using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Notflix.API.Data;
using Notflix.API.Repositories;
using Xunit;

namespace Notflix.API.Tests;

[CollectionDefinition("Notflix LocalDB", DisableParallelization = true)]
public sealed class LocalDbCollection { }

internal sealed class NotflixTestApplication : IAsyncDisposable
{
    public const string UserName = "test-admin";
    public const string Password = "Test-Only!Password426";
    public string DatabaseName { get; } = "NotflixTests_" + Guid.NewGuid().ToString("N");
    public string ConnectionString { get; }
    public NotflixWebFactory Factory { get; }
    public HttpClient Client { get; }

    private NotflixTestApplication()
    {
        ConnectionString = new SqlConnectionStringBuilder
        {
            DataSource = @"(localdb)\MSSQLLocalDB", InitialCatalog = DatabaseName,
            IntegratedSecurity = true, TrustServerCertificate = true, ConnectTimeout = 15,
        }.ConnectionString;
        Factory = new NotflixWebFactory(ConnectionString);
        Client = CreateClient();
    }

    public static async Task<NotflixTestApplication> CreateAsync(bool initialize = true, bool seed = true)
    {
        var application = new NotflixTestApplication();
        try
        {
            if (initialize)
            {
                await using var scope = application.Factory.Services.CreateAsyncScope();
                var setup = scope.ServiceProvider.GetRequiredService<DatabaseSetup>();
                await setup.InitializeAsync();
                if (seed) await setup.SeedAsync(UserName, Password);
            }
            return application;
        }
        catch
        {
            await application.DisposeAsync();
            throw;
        }
    }

    public HttpClient CreateClient() => Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost"),
    });

    public async Task<SqlConnection> OpenAsync()
    {
        var connection = new SqlConnection(ConnectionString);
        try { await connection.OpenAsync(); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    public async Task<SqlConnection> OpenMasterAsync()
    {
        var settings = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" };
        var connection = new SqlConnection(settings.ConnectionString);
        try { await connection.OpenAsync(); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    public async Task<int> AddUserAsync(string userName)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DatabaseSetup>().SeedAsync(userName, Password);
        return (await scope.ServiceProvider.GetRequiredService<IUserRepository>().FindAsync(userName, CancellationToken.None))!.Id;
    }

    public async Task<int> CurrentUserIdAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IUserRepository>().FindAsync(UserName, CancellationToken.None))!.Id;
    }

    public static async Task<string> RefreshCsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/auth/csrf");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        var value = Uri.UnescapeDataString(cookie["XSRF-TOKEN=".Length..].Split(';', 2)[0]);
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", value);
        return value;
    }

    public static async Task LoginAsync(HttpClient client, string userName = UserName, string password = Password)
    {
        await RefreshCsrfAsync(client);
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { userName, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        await RefreshCsrfAsync(client);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        var settings = new SqlConnectionStringBuilder(ConnectionString);
        if (!string.Equals(settings.DataSource, @"(localdb)\MSSQLLocalDB", StringComparison.OrdinalIgnoreCase)
            || !Regex.IsMatch(settings.InitialCatalog, @"^NotflixTests_[0-9a-f]{32}$"))
            throw new InvalidOperationException("Refusing cleanup outside isolated Notflix test databases.");
        using (var pooled = new SqlConnection(ConnectionString)) SqlConnection.ClearPool(pooled);
        await using var master = await OpenMasterAsync();
        await master.ExecuteAsync($"IF DB_ID(@Name) IS NOT NULL BEGIN ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{DatabaseName}]; END", new { Name = DatabaseName });
    }
}

internal sealed class NotflixWebFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:NotflixConnectionString"] = connectionString,
        }));
    }
}

