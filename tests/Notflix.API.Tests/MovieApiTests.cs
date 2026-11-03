using System.Net;
using System.Net.Http.Json;
using Dapper;
using Notflix.API.Models;
using Xunit;

namespace Notflix.API.Tests;

[Collection("Notflix LocalDB")]
public sealed class MovieApiTests : IAsyncLifetime
{
    private NotflixTestApplication _app = null!;
    public async Task InitializeAsync()
    {
        _app = await NotflixTestApplication.CreateAsync();
        await NotflixTestApplication.LoginAsync(_app.Client);
    }
    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task AddReadEditRateDelete_PersistsExpectedDataAndReturnsUsefulBodies()
    {
        using var added = await _app.Client.PostAsJsonAsync("/api/movies", new { title = "  Verification movie  ", category = 4 });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var created = await added.Content.ReadFromJsonAsync<Movie>();
        Assert.NotNull(created);
        Assert.Equal("Verification movie", created.Title);
        Assert.Equal(4, (int?)created.Category);
        Assert.Null(created.AverageRating);
        Assert.Null(created.MyRating);
        Assert.Null(created.LegacyRating);
        Assert.Equal(0, created.RatingCount);
        Assert.EndsWith("/api/movies/" + created.Id, added.Headers.Location?.OriginalString);

        using var edited = await _app.Client.PutAsJsonAsync("/api/movies/" + created.Id, new { title = "Edited movie", category = 2, id = -1 });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var updated = await edited.Content.ReadFromJsonAsync<Movie>();
        Assert.NotNull(updated);
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("Edited movie", updated.Title);
        Assert.Equal(2, (int?)updated.Category);

        using var rated = await _app.Client.PutAsJsonAsync($"/api/movies/{created.Id}/rating", new { rating = 5 });
        Assert.Equal(HttpStatusCode.OK, rated.StatusCode);
        var vote = await rated.Content.ReadFromJsonAsync<Movie>();
        Assert.NotNull(vote);
        Assert.Equal(5, vote.MyRating);
        Assert.Equal(5d, vote.AverageRating);
        Assert.Equal(1, vote.RatingCount);
        var reloaded = await _app.Client.GetFromJsonAsync<Movie>($"/api/movies/{created.Id}");
        Assert.NotNull(reloaded);
        Assert.Equal("Edited movie", reloaded.Title);
        Assert.Equal(5, reloaded.MyRating);
        await using (var db = await _app.OpenAsync())
        {
            Assert.Equal("Edited movie", await db.QuerySingleAsync<string>("SELECT Title FROM dbo.Movies WHERE Id=@Id", new { created.Id }));
            Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.MovieRatings WHERE MovieId=@Id", new { created.Id }));
        }
        using var deleted = await _app.Client.DeleteAsync($"/api/movies/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var missing = await _app.Client.GetAsync($"/api/movies/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        await using var cleaned = await _app.OpenAsync();
        Assert.Equal(0, await cleaned.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.MovieRatings WHERE MovieId=@Id", new { created.Id }));
    }

    [Theory]
    [InlineData("GET", "/api/movies/2147483647")]
    [InlineData("PUT", "/api/movies/2147483647")]
    [InlineData("PUT", "/api/movies/2147483647/rating")]
    [InlineData("DELETE", "/api/movies/2147483647")]
    public async Task MissingMovie_ReturnsNotFound(string method, string route)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (method == "PUT") request.Content = route.EndsWith("/rating", StringComparison.Ordinal)
            ? JsonContent.Create(new { rating = 3 }) : JsonContent.Create(new { title = "Not found", category = 2 });
        using var response = await _app.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    public static IEnumerable<object[]> InvalidMovies()
    {
        yield return new object[] { "", 1 };
        yield return new object[] { "   ", 1 };
        yield return new object[] { new string('t', 101), 1 };
        yield return new object[] { "Verification", 0 };
        yield return new object[] { "Verification", 6 };
    }

    [Theory]
    [MemberData(nameof(InvalidMovies))]
    public async Task InvalidMovie_IsRejectedWithoutInsertion(string title, int category)
    {
        using var response = await _app.Client.PostAsJsonAsync("/api/movies", new { title, category });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var db = await _app.OpenAsync();
        Assert.Equal(3, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Movies"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task RatingOutsideOneToFive_IsRejectedWithoutVote(int rating)
    {
        var movies = await _app.Client.GetFromJsonAsync<Movie[]>("/api/movies");
        Assert.NotNull(movies);
        Assert.NotEmpty(movies);
        var id = movies[0].Id;
        using var response = await _app.Client.PutAsJsonAsync($"/api/movies/{id}/rating", new { rating });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var db = await _app.OpenAsync();
        Assert.Equal(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.MovieRatings"));
    }

    [Fact]
    public async Task VotesArePerAuthenticatedUser_AndRepeatedVoteUpdatesRatherThanDuplicates()
    {
        var secondId = await _app.AddUserAsync("second-user");
        var firstId = await _app.CurrentUserIdAsync();
        var movie = (await _app.Client.GetFromJsonAsync<Movie[]>("/api/movies"))![0];
        using var spoofedUser = await _app.Client.PutAsJsonAsync($"/api/movies/{movie.Id}/rating", new { rating = 5, userId = secondId });
        Assert.Equal(HttpStatusCode.OK, spoofedUser.StatusCode);
        using var secondClient = _app.CreateClient();
        await NotflixTestApplication.LoginAsync(secondClient, "second-user");
        using var secondVote = await secondClient.PutAsJsonAsync($"/api/movies/{movie.Id}/rating", new { rating = 3 });
        Assert.Equal(HttpStatusCode.OK, secondVote.StatusCode);
        var firstView = await _app.Client.GetFromJsonAsync<Movie>($"/api/movies/{movie.Id}");
        var secondView = await secondClient.GetFromJsonAsync<Movie>($"/api/movies/{movie.Id}");
        Assert.Equal(5, firstView!.MyRating);
        Assert.Equal(3, secondView!.MyRating);
        Assert.Equal(4d, firstView.AverageRating);
        Assert.Equal(2, firstView.RatingCount);

        using var updatedVote = await _app.Client.PutAsJsonAsync($"/api/movies/{movie.Id}/rating", new { rating = 1 });
        Assert.Equal(HttpStatusCode.OK, updatedVote.StatusCode);
        var updated = await _app.Client.GetFromJsonAsync<Movie>($"/api/movies/{movie.Id}");
        Assert.Equal(2d, updated!.AverageRating);
        Assert.Equal(2, updated.RatingCount);
        await using var db = await _app.OpenAsync();
        Assert.Equal(1, await db.QuerySingleAsync<int>("SELECT Rating FROM dbo.MovieRatings WHERE MovieId=@MovieId AND UserId=@UserId", new { MovieId = movie.Id, UserId = firstId }));
    }

    [Fact]
    public async Task CatalogueEdit_PreservesLegacyRatingWithoutInventingVotes()
    {
        await using var db = await _app.OpenAsync();
        var id = await db.ExecuteScalarAsync<int>("SELECT MIN(Id) FROM dbo.Movies");
        await db.ExecuteAsync("UPDATE dbo.Movies SET Rating=9 WHERE Id=@Id", new { id });

        using var response = await _app.Client.PutAsJsonAsync($"/api/movies/{id}", new { title = "Edited legacy movie", category = 1 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var movie = await response.Content.ReadFromJsonAsync<Movie>();
        Assert.Equal(9, movie!.LegacyRating);
        Assert.Null(movie.AverageRating);
        Assert.Null(movie.MyRating);
        Assert.Equal(0, movie.RatingCount);
        Assert.Equal(9, await db.QuerySingleAsync<int>("SELECT Rating FROM dbo.Movies WHERE Id=@Id", new { id }));
    }

    [Fact]
    public async Task AuthenticatedMutationWithoutCsrf_IsRejected()
    {
        _app.Client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        using var response = await _app.Client.PostAsJsonAsync("/api/movies", new { title = "Rejected", category = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DatabaseFailure_ReturnsSanitizedProblemDetails()
    {
        await using (var db = await _app.OpenAsync()) await db.ExecuteAsync("DROP TABLE dbo.MovieRatings");

        using var response = await _app.Client.GetAsync("/api/movies");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Invalid object name", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_app.DatabaseName, body);
    }
}

