using System.ComponentModel.DataAnnotations;
namespace Notflix.API.Models;
public sealed class MovieRequest
{
    [Required, StringLength(100)] public string Title { get; set; } = string.Empty;
    [Range(1, 5)] public int Category { get; set; }
}
public sealed class RatingRequest { [Range(1, 5)] public int Rating { get; set; } }
public sealed class LoginRequest
{
    [Required, StringLength(80)] public string UserName { get; set; } = string.Empty;
    [Required, StringLength(256)] public string Password { get; set; } = string.Empty;
}
public sealed class NotflixUser
{
    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
}
public sealed record SessionUser(int Id, string UserName);
