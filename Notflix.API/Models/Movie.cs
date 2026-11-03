using Notflix.API.Enumerables;
namespace Notflix.API.Models;
public sealed class Movie
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public Category? Category { get; set; }
    public int? LegacyRating { get; set; }
    public double? AverageRating { get; set; }
    public int RatingCount { get; set; }
    public int? MyRating { get; set; }
}
