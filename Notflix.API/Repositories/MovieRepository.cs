using System.Data;
using Dapper;
using Notflix.API.Data;
using Notflix.API.Models;
using Notflix.API.Repositories.Interfaces;
namespace Notflix.API.Repositories;

public sealed class MovieRepository(SqlConnectionFactory factory) : IMovieRepository
{
    private const string Projection = """
        SELECT m.Id, COALESCE(m.Title, '(Untitled)') AS Title, m.Category, m.Rating AS LegacyRating,
            votes.AverageRating, votes.RatingCount, votes.MyRating
        FROM dbo.Movies m
        OUTER APPLY (SELECT AVG(CAST(r.Rating AS float)) AS AverageRating, COUNT(*) AS RatingCount,
            MAX(CASE WHEN r.UserId = @UserId THEN r.Rating END) AS MyRating
            FROM dbo.MovieRatings r WHERE r.MovieId = m.Id) votes
        """;
    public async Task<IReadOnlyList<Movie>> GetAllAsync(int userId, CancellationToken cancellationToken)
    {
        await using var db = await factory.OpenAsync(cancellationToken);
        return (await db.QueryAsync<Movie>(new CommandDefinition(Projection + " ORDER BY m.Title, m.Id", new { userId }, cancellationToken: cancellationToken))).AsList();
    }
    public async Task<Movie?> GetAsync(int id, int userId, CancellationToken cancellationToken)
    {
        await using var db = await factory.OpenAsync(cancellationToken);
        return await db.QuerySingleOrDefaultAsync<Movie>(new CommandDefinition(Projection + " WHERE m.Id = @Id", new { id, userId }, cancellationToken: cancellationToken));
    }
    public async Task<Movie> AddAsync(MovieRequest request, int userId, CancellationToken cancellationToken)
    {
        await using var db = await factory.OpenAsync(cancellationToken);
        using var transaction = db.BeginTransaction(IsolationLevel.Serializable);
        var id = await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COALESCE(MAX(Id),0)+1 FROM dbo.Movies WITH (UPDLOCK,HOLDLOCK)", transaction: transaction, cancellationToken: cancellationToken));
        await db.ExecuteAsync(new CommandDefinition("INSERT INTO dbo.Movies (Id,Title,Category,Rating) VALUES (@Id,@Title,@Category,NULL)", new { id, Title = request.Title.Trim(), request.Category }, transaction, cancellationToken: cancellationToken));
        transaction.Commit();
        return (await GetAsync(id, userId, cancellationToken))!;
    }
    public async Task<Movie?> UpdateAsync(int id, MovieRequest request, int userId, CancellationToken cancellationToken)
    {
        await using var db = await factory.OpenAsync(cancellationToken);
        var count = await db.ExecuteAsync(new CommandDefinition("UPDATE dbo.Movies SET Title=@Title,Category=@Category WHERE Id=@Id", new { id, Title = request.Title.Trim(), request.Category }, cancellationToken: cancellationToken));
        return count == 0 ? null : await GetAsync(id, userId, cancellationToken);
    }
    public async Task<Movie?> RateAsync(int id, int userId, int rating, CancellationToken cancellationToken)
    {
        await using var db = await factory.OpenAsync(cancellationToken);
        using var transaction = db.BeginTransaction(IsolationLevel.Serializable);
        if (await db.ExecuteScalarAsync<int>(new CommandDefinition("SELECT COUNT(*) FROM dbo.Movies WITH (UPDLOCK,HOLDLOCK) WHERE Id=@Id", new { id }, transaction, cancellationToken: cancellationToken)) == 0)
            return null;
        await db.ExecuteAsync(new CommandDefinition("""
            UPDATE dbo.MovieRatings WITH (UPDLOCK,HOLDLOCK) SET Rating=@Rating WHERE MovieId=@Id AND UserId=@UserId;
            IF @@ROWCOUNT=0 INSERT INTO dbo.MovieRatings(MovieId,UserId,Rating) VALUES(@Id,@UserId,@Rating);
            """, new { id, userId, rating }, transaction, cancellationToken: cancellationToken));
        transaction.Commit();
        return await GetAsync(id, userId, cancellationToken);
    }
    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        await using var db = await factory.OpenAsync(cancellationToken);
        using var transaction = db.BeginTransaction(IsolationLevel.Serializable);
        await db.ExecuteAsync(new CommandDefinition("DELETE FROM dbo.MovieRatings WHERE MovieId=@Id; IF OBJECT_ID('dbo.Favorites','U') IS NOT NULL DELETE FROM dbo.Favorites WHERE MovieId=@Id;", new { id }, transaction, cancellationToken: cancellationToken));
        var count = await db.ExecuteAsync(new CommandDefinition("DELETE FROM dbo.Movies WHERE Id=@Id", new { id }, transaction, cancellationToken: cancellationToken));
        transaction.Commit();
        return count > 0;
    }
}
