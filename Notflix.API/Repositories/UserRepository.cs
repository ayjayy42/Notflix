using Dapper;
using Notflix.API.Data;
using Notflix.API.Models;
namespace Notflix.API.Repositories;
public interface IUserRepository
{
    Task<NotflixUser?> FindAsync(string userName, CancellationToken cancellationToken);
}
public sealed class UserRepository(SqlConnectionFactory factory) : IUserRepository
{
    public async Task<NotflixUser?> FindAsync(string userName, CancellationToken cancellationToken)
    {
        await using var db = await factory.OpenAsync(cancellationToken);
        return await db.QuerySingleOrDefaultAsync<NotflixUser>(new CommandDefinition(
            "SELECT Id,UserName,PasswordHash FROM dbo.Users WHERE NormalizedUserName=@Name AND PasswordHash IS NOT NULL",
            new { Name = userName.Trim().ToUpperInvariant() }, cancellationToken: cancellationToken));
    }
}
