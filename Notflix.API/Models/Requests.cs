using System.ComponentModel.DataAnnotations;
namespace Notflix.API.Models;
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
