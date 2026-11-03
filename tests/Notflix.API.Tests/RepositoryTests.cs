using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Notflix.API.Data;
using Notflix.API.Models;
using Notflix.API.Repositories;
using Notflix.API.Repositories.Interfaces;
using Xunit;

namespace Notflix.API.Tests;

[Collection("Notflix LocalDB")]
public sealed class RepositoryTests
{
    [Fact]
    public async Task SimultaneousMovieInserts_ProduceDistinctIdsAndPreserveEveryMovie()
    {
        await using var app = await NotflixTestApplication.CreateAsync();
        var userId = await app.CurrentUserIdAsync();
        var tasks = Enumerable.Range(0, 8).Select(async index =>
        {
            await using var scope = app.Factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IMovieRepository>().AddAsync(
                new MovieRequest { Title = "Concurrent movie " + index, Category = 1 }, userId, CancellationToken.None);
        });

        var created = await Task.WhenAll(tasks);

        Assert.Equal(8, created.Select(movie => movie.Id).Distinct().Count());
        await using var db = await app.OpenAsync();
        Assert.Equal(8, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Movies WHERE Title LIKE 'Concurrent movie %'"));
        Assert.Equal(11, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Movies"));
    }

    [Fact]
    public async Task SimultaneousVotesForSameUserAndMovie_KeepOneValidVote()
    {
        await using var app = await NotflixTestApplication.CreateAsync();
        var userId = await app.CurrentUserIdAsync();
        await using var db = await app.OpenAsync();
        var movieId = await db.ExecuteScalarAsync<int>("SELECT MIN(Id) FROM dbo.Movies");
        var tasks = Enumerable.Range(0, 8).Select(async index =>
        {
            await using var scope = app.Factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<IMovieRepository>()
                .RateAsync(movieId, userId, index % 5 + 1, CancellationToken.None);
        });

        var results = await Task.WhenAll(tasks);

        Assert.All(results, movie => Assert.NotNull(movie));
        Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.MovieRatings WHERE MovieId=@MovieId AND UserId=@UserId", new { movieId, userId }));
        var rating = await db.QuerySingleAsync<int>("SELECT Rating FROM dbo.MovieRatings WHERE MovieId=@MovieId AND UserId=@UserId", new { movieId, userId });
        Assert.InRange(rating, 1, 5);
        await using var finalScope = app.Factory.Services.CreateAsyncScope();
        var final = await finalScope.ServiceProvider.GetRequiredService<IMovieRepository>().GetAsync(movieId, userId, CancellationToken.None);
        Assert.Equal(1, final!.RatingCount);
        Assert.Equal(rating, final.MyRating);
        Assert.Equal((double)rating, final.AverageRating);
    }

    [Fact]
    public async Task SqlMetacharactersInTitle_AreStoredAsData()
    {
        await using var app = await NotflixTestApplication.CreateAsync();
        var userId = await app.CurrentUserIdAsync();
        await using var scope = app.Factory.Services.CreateAsyncScope();
        const string title = "Robert'); DROP TABLE dbo.Movies;--";

        var created = await scope.ServiceProvider.GetRequiredService<IMovieRepository>()
            .AddAsync(new MovieRequest { Title = title, Category = 2 }, userId, CancellationToken.None);

        Assert.Equal(title, created.Title);
        await using var db = await app.OpenAsync();
        Assert.Equal(4, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Movies"));
    }

    [Fact]
    public async Task LegacySchemaMigration_IsRepeatSafeAndPreservesUsersAndCatalogueRatings()
    {
        await using var app = await NotflixTestApplication.CreateAsync(initialize: false, seed: false);
        await using (var master = await app.OpenMasterAsync()) await master.ExecuteAsync($"CREATE DATABASE [{app.DatabaseName}]");
        await using (var legacy = await app.OpenAsync())
        {
            await legacy.ExecuteAsync("""
                CREATE TABLE dbo.Users(Id int NOT NULL PRIMARY KEY, FirstName varchar(50) NULL, LastName varchar(50) NULL);
                CREATE TABLE dbo.Movies(Id int NOT NULL PRIMARY KEY, Title varchar(50) NULL, Category int NULL, Rating int NULL);
                INSERT INTO dbo.Users(Id,FirstName,LastName) VALUES(1,'Legacy','One'),(2,'Legacy','Two');
                INSERT INTO dbo.Movies(Id,Title,Category,Rating) VALUES(7,'Legacy title',4,9),(8,NULL,NULL,NULL);
                """);
        }
        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var setup = scope.ServiceProvider.GetRequiredService<DatabaseSetup>();
            await setup.InitializeAsync();
            await setup.InitializeAsync();
            var users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
            Assert.Null(await users.FindAsync("Legacy", CancellationToken.None));
            var repository = scope.ServiceProvider.GetRequiredService<IMovieRepository>();
            var movies = await repository.GetAllAsync(1, CancellationToken.None);
            var legacy = Assert.Single(movies, movie => movie.Id == 7);
            Assert.Equal("Legacy title", legacy.Title);
            Assert.Equal(9, legacy.LegacyRating);
            Assert.Null(legacy.AverageRating);
            Assert.Null(legacy.MyRating);
            Assert.Equal(0, legacy.RatingCount);
            Assert.Equal("(Untitled)", Assert.Single(movies, movie => movie.Id == 8).Title);
        }
        await using var db = await app.OpenAsync();
        Assert.Equal(2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Users WHERE UserName IS NULL AND PasswordHash IS NULL"));
        Assert.Equal(9, await db.ExecuteScalarAsync<int>("SELECT Rating FROM dbo.Movies WHERE Id=7"));
        Assert.Equal(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.MovieRatings"));
        Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.Users') AND name='UX_Users_NormalizedUserName' AND has_filter=1"));
    }

    [Fact]
    public async Task OriginalIdentityUsersSchema_SeedsNewCredentialsAndPreservesExistingRows()
    {
        await using var app = await NotflixTestApplication.CreateAsync(initialize: false, seed: false);
        await using (var master = await app.OpenMasterAsync()) await master.ExecuteAsync($"CREATE DATABASE [{app.DatabaseName}]");
        await using (var original = await app.OpenAsync())
        {
            await original.ExecuteAsync("""
                CREATE TABLE dbo.Users(Id int IDENTITY(1,1) NOT NULL PRIMARY KEY, FirstName varchar(50) NULL, LastName varchar(50) NULL);
                CREATE TABLE dbo.Movies(Id int NOT NULL PRIMARY KEY, Title varchar(50) NULL, Category int NULL, Rating int NULL);
                INSERT INTO dbo.Users(FirstName,LastName) VALUES('Original','One'),('Original','Two');
                INSERT INTO dbo.Movies(Id,Title,Category,Rating) VALUES(7,'Original catalogue movie',4,9);
                """);
        }
        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var setup = scope.ServiceProvider.GetRequiredService<DatabaseSetup>();
            await setup.InitializeAsync();
            await setup.SeedAsync(NotflixTestApplication.UserName, NotflixTestApplication.Password);
            await setup.SeedAsync(NotflixTestApplication.UserName, "Different!Password426");
            var seeded = await scope.ServiceProvider.GetRequiredService<IUserRepository>().FindAsync(NotflixTestApplication.UserName, CancellationToken.None);
            Assert.NotNull(seeded);
            Assert.Equal(3, seeded.Id);
        }

        await NotflixTestApplication.LoginAsync(app.Client);
        await using var db = await app.OpenAsync();
        Assert.Equal(3, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Users"));
        Assert.Equal(2, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Users WHERE Id IN(1,2) AND UserName IS NULL AND PasswordHash IS NULL"));
        Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Movies"));
        Assert.Equal(9, await db.ExecuteScalarAsync<int>("SELECT Rating FROM dbo.Movies WHERE Id=7"));
        Assert.Equal(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.MovieRatings"));
    }

    [Fact]
    public async Task ConcurrentSeeds_CreateOneCredentialAndOneStarterCatalogue()
    {
        await using var app = await NotflixTestApplication.CreateAsync(seed: false);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var scope = app.Factory.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DatabaseSetup>()
                .SeedAsync(NotflixTestApplication.UserName, NotflixTestApplication.Password);
        }));

        await using var db = await app.OpenAsync();
        Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Users WHERE NormalizedUserName=@Name", new { Name = NotflixTestApplication.UserName.ToUpperInvariant() }));
        Assert.Equal(3, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Movies"));
        Assert.Equal(3, await db.ExecuteScalarAsync<int>("SELECT COUNT(DISTINCT Title) FROM dbo.Movies"));
        Assert.Equal(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.MovieRatings"));
        await NotflixTestApplication.LoginAsync(app.Client);
    }

    [Fact]
    public async Task RepeatedSeed_PreservesExistingHashAndDoesNotDuplicateCatalogue()
    {
        await using var app = await NotflixTestApplication.CreateAsync();
        await using var db = await app.OpenAsync();
        var originalHash = await db.QuerySingleAsync<string>("SELECT PasswordHash FROM dbo.Users WHERE UserName=@Name", new { Name = NotflixTestApplication.UserName });
        await using (var scope = app.Factory.Services.CreateAsyncScope())
        {
            var setup = scope.ServiceProvider.GetRequiredService<DatabaseSetup>();
            await setup.SeedAsync(NotflixTestApplication.UserName, "New-Only!Password426");
            await setup.SeedAsync(NotflixTestApplication.UserName, "New-Only!Password426");
        }

        Assert.Equal(originalHash, await db.QuerySingleAsync<string>("SELECT PasswordHash FROM dbo.Users WHERE UserName=@Name", new { Name = NotflixTestApplication.UserName }));
        Assert.Equal(1, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Users"));
        Assert.Equal(3, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.Movies"));
        Assert.Equal(0, await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.MovieRatings"));
    }
}


