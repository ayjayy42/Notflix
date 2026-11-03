using Notflix.API.Models;
namespace Notflix.API.Repositories.Interfaces;
public interface IMovieRepository
{
    Task<IReadOnlyList<Movie>> GetAllAsync(int userId, CancellationToken cancellationToken);
    Task<Movie?> GetAsync(int id, int userId, CancellationToken cancellationToken);
    Task<Movie> AddAsync(MovieRequest request, int userId, CancellationToken cancellationToken);
    Task<Movie?> UpdateAsync(int id, MovieRequest request, int userId, CancellationToken cancellationToken);
    Task<Movie?> RateAsync(int id, int userId, int rating, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken);
}
