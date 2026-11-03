using System.Data;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Notflix.API.Models;
namespace Notflix.API.Data;

public sealed class DatabaseSetup(SqlConnectionFactory factory, IPasswordHasher<NotflixUser> hasher)
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var settings = new SqlConnectionStringBuilder(factory.ConnectionString);
        var database = settings.InitialCatalog;
        if (!Regex.IsMatch(database, @"^[a-zA-Z0-9_.-]{1,100}$")) throw new InvalidOperationException("Use a named application database.");
        if (new[] { "master", "model", "msdb", "tempdb" }.Contains(database, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException("System databases are not application targets.");
        settings.InitialCatalog = "master";
        await using (var master = new SqlConnection(settings.ConnectionString))
        {
            await master.OpenAsync(ct);
            await master.ExecuteAsync(new CommandDefinition($"IF DB_ID(@Name) IS NULL CREATE DATABASE [{database}]", new { Name = database }, cancellationToken: ct));
        }
        await using var db = await factory.OpenAsync(ct);
        using var transaction = db.BeginTransaction();
        // Separate statements permit SQL Server to compile after newly added columns exist.
        string[] statements = [
            "IF OBJECT_ID('dbo.Movies','U') IS NULL CREATE TABLE dbo.Movies(Id int NOT NULL PRIMARY KEY, Title nvarchar(100) NULL, Category int NULL, Rating int NULL)",
            "IF OBJECT_ID('dbo.Users','U') IS NULL CREATE TABLE dbo.Users(Id int IDENTITY(1,1) NOT NULL PRIMARY KEY, FirstName varchar(50) NULL, LastName varchar(50) NULL)",
            "ALTER TABLE dbo.Movies ALTER COLUMN Title nvarchar(100) NULL",
            "IF COL_LENGTH('dbo.Users','UserName') IS NULL ALTER TABLE dbo.Users ADD UserName nvarchar(80) NULL",
            "IF COL_LENGTH('dbo.Users','NormalizedUserName') IS NULL ALTER TABLE dbo.Users ADD NormalizedUserName nvarchar(80) NULL",
            "IF COL_LENGTH('dbo.Users','PasswordHash') IS NULL ALTER TABLE dbo.Users ADD PasswordHash nvarchar(512) NULL",
            "IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE object_id=OBJECT_ID('dbo.Users') AND name='UX_Users_NormalizedUserName') CREATE UNIQUE INDEX UX_Users_NormalizedUserName ON dbo.Users(NormalizedUserName) WHERE NormalizedUserName IS NOT NULL",
            """
            IF OBJECT_ID('dbo.MovieRatings','U') IS NULL CREATE TABLE dbo.MovieRatings(
                MovieId int NOT NULL REFERENCES dbo.Movies(Id), UserId int NOT NULL REFERENCES dbo.Users(Id),
                Rating int NOT NULL CHECK(Rating BETWEEN 1 AND 5), PRIMARY KEY(MovieId,UserId))
            """
        ];
        foreach (var sql in statements) await db.ExecuteAsync(new CommandDefinition(sql, transaction: transaction, cancellationToken: ct));
        transaction.Commit();
    }
    public async Task SeedAsync(string userName, string password, CancellationToken ct = default)
    {
        userName = userName.Trim();
        if (userName.Length is < 1 or > 80 || password.Length is < 12 or > 256)
            throw new InvalidOperationException("Seed:UserName (1-80 characters) and Seed:Password (12-256 characters) are required; existing accounts are never reset.");
        await using var db = await factory.OpenAsync(ct);
        using var transaction = db.BeginTransaction(IsolationLevel.Serializable);
        var normalized = userName.ToUpperInvariant();
        var userId = await db.QuerySingleOrDefaultAsync<int?>(new CommandDefinition("SELECT Id FROM dbo.Users WITH(UPDLOCK,HOLDLOCK) WHERE NormalizedUserName=@Name", new { Name = normalized }, transaction, cancellationToken: ct));
        if (userId is null)
        {
            var user = new NotflixUser { UserName = userName };
            var hash = hasher.HashPassword(user, password);
            var fields = new { FirstName = userName[..Math.Min(50,userName.Length)], UserName = userName, Normalized = normalized, Hash = hash };
            var identity = await db.ExecuteScalarAsync<int>(new CommandDefinition(
                "SELECT COLUMNPROPERTY(OBJECT_ID('dbo.Users'), 'Id', 'IsIdentity')", transaction: transaction, cancellationToken: ct));
            if (identity == 1)
            {
                userId = await db.ExecuteScalarAsync<int>(new CommandDefinition(
                    "INSERT INTO dbo.Users(FirstName,LastName,UserName,NormalizedUserName,PasswordHash) OUTPUT INSERTED.Id VALUES(@FirstName,'',@UserName,@Normalized,@Hash)",
                    fields, transaction, cancellationToken: ct));
            }
            else
            {
                // Support nonidentity development databases created by earlier versions too.
                userId = await db.ExecuteScalarAsync<int>(new CommandDefinition(
                    "SELECT COALESCE(MAX(Id),0)+1 FROM dbo.Users WITH(UPDLOCK,HOLDLOCK)", transaction: transaction, cancellationToken: ct));
                var parameters = new DynamicParameters(fields);
                parameters.Add("Id", userId.Value);
                await db.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO dbo.Users(Id,FirstName,LastName,UserName,NormalizedUserName,PasswordHash) VALUES(@Id,@FirstName,'',@UserName,@Normalized,@Hash)",
                    parameters, transaction, cancellationToken: ct));
            }
        }
        transaction.Commit();
        // Serialize the empty-catalogue check and inserts so concurrent setup runs are repeat-safe.
        using var catalogueTransaction = db.BeginTransaction(IsolationLevel.Serializable);
        var movieCount = await db.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.Movies WITH(UPDLOCK,HOLDLOCK)", transaction: catalogueTransaction, cancellationToken: ct));
        if (movieCount == 0)
        {
            await db.ExecuteAsync(new CommandDefinition("""
                INSERT INTO dbo.Movies(Id,Title,Category,Rating) VALUES
                (1,N'The Matrix',4,NULL),(2,N'The Grand Budapest Hotel',2,NULL),(3,N'Arrival',4,NULL)
                """, transaction: catalogueTransaction, cancellationToken: ct));
        }
        catalogueTransaction.Commit();
    }
}
