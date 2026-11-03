using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Dapper;
using Xunit;

namespace Notflix.API.Tests;

[Collection("Notflix LocalDB")]
public sealed class AuthTests : IAsyncLifetime
{
    private NotflixTestApplication _app = null!;
    public async Task InitializeAsync() => _app = await NotflixTestApplication.CreateAsync();
    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Theory]
    [InlineData("/api/movies")]
    [InlineData("/api/auth/me")]
    public async Task AnonymousProtectedRead_ReturnsUnauthorizedWithoutRedirect(string route)
    {
        using var response = await _app.Client.GetAsync(route);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task ValidLogin_CreatesHttpOnlySessionAndSupportsSessionReload()
    {
        await NotflixTestApplication.RefreshCsrfAsync(_app.Client);
        using var login = await _app.Client.PostAsJsonAsync("/api/auth/login", new { userName = "  TEST-ADMIN  ", password = NotflixTestApplication.Password });

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var cookie = Assert.Single(login.Headers.GetValues("Set-Cookie"), value => value.StartsWith("Notflix.Session=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        using var first = await _app.Client.GetAsync("/api/auth/me");
        using var reloaded = await _app.Client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await reloaded.Content.ReadAsStringAsync());
        Assert.Contains(NotflixTestApplication.UserName, await reloaded.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnknownUserAndWrongPassword_HaveSameGenericUnauthorizedResponse()
    {
        await NotflixTestApplication.RefreshCsrfAsync(_app.Client);
        using var unknown = await _app.Client.PostAsJsonAsync("/api/auth/login", new { userName = "unknown", password = "incorrect-password" });
        using var wrong = await _app.Client.PostAsJsonAsync("/api/auth/login", new { userName = NotflixTestApplication.UserName, password = "incorrect-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(await unknown.Content.ReadAsStringAsync(), await wrong.Content.ReadAsStringAsync());
        using var me = await _app.Client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoginWithoutValidAntiforgeryToken_IsRejected(bool incorrectToken)
    {
        if (incorrectToken)
        {
            await NotflixTestApplication.RefreshCsrfAsync(_app.Client);
            _app.Client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
            _app.Client.DefaultRequestHeaders.Add("X-XSRF-TOKEN", "invalid-token");
        }
        using var response = await _app.Client.PostAsJsonAsync("/api/auth/login", new { userName = NotflixTestApplication.UserName, password = NotflixTestApplication.Password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task LoginRefreshesIdentityBoundToken_AndLogoutEndsTheSession()
    {
        var anonymousToken = await NotflixTestApplication.RefreshCsrfAsync(_app.Client);
        using var login = await _app.Client.PostAsJsonAsync("/api/auth/login", new { userName = NotflixTestApplication.UserName, password = NotflixTestApplication.Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var staleToken = await _app.Client.PostAsJsonAsync("/api/movies", new { title = "Rejected stale token", category = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, staleToken.StatusCode);

        var authenticatedToken = await NotflixTestApplication.RefreshCsrfAsync(_app.Client);
        Assert.NotEqual(anonymousToken, authenticatedToken);
        using var added = await _app.Client.PostAsJsonAsync("/api/movies", new { title = "Accepted fresh token", category = 1 });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        using var logout = await _app.Client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        await NotflixTestApplication.RefreshCsrfAsync(_app.Client);
        using var afterLogout = await _app.Client.GetAsync("/api/movies");
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
        using var rejectedMutation = await _app.Client.PostAsJsonAsync("/api/movies", new { title = "Rejected after logout", category = 1 });
        Assert.Equal(HttpStatusCode.Unauthorized, rejectedMutation.StatusCode);
    }

    [Fact]
    public async Task SeededPassword_IsStoredAsAHashAndRepeatSeedDoesNotResetIt()
    {
        await using var db = await _app.OpenAsync();
        var before = await db.QuerySingleAsync<string>("SELECT PasswordHash FROM dbo.Users WHERE UserName=@Name", new { Name = NotflixTestApplication.UserName });
        Assert.NotEqual(NotflixTestApplication.Password, before);
        await using (var scope = _app.Factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<Notflix.API.Data.DatabaseSetup>().SeedAsync(NotflixTestApplication.UserName, "Different!Password426");
        var after = await db.QuerySingleAsync<string>("SELECT PasswordHash FROM dbo.Users WHERE UserName=@Name", new { Name = NotflixTestApplication.UserName });
        Assert.Equal(before, after);
        await NotflixTestApplication.LoginAsync(_app.Client);
    }

    [Fact]
    public async Task LoginRateLimit_RejectsEleventhAttempt()
    {
        await NotflixTestApplication.RefreshCsrfAsync(_app.Client);
        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var rejected = await _app.Client.PostAsJsonAsync("/api/auth/login", new { userName = "unknown", password = "incorrect-password" });
            Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        }
        using var limited = await _app.Client.PostAsJsonAsync("/api/auth/login", new { userName = "unknown", password = "incorrect-password" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }
}

