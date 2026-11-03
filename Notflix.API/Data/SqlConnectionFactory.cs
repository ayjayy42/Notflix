using Microsoft.Data.SqlClient;
namespace Notflix.API.Data;
public sealed class SqlConnectionFactory(IConfiguration configuration)
{
    public string ConnectionString => configuration.GetConnectionString("NotflixConnectionString")
        ?? throw new InvalidOperationException("Configure ConnectionStrings:NotflixConnectionString.");
    public async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(ConnectionString);
        try { await connection.OpenAsync(cancellationToken); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }
}
