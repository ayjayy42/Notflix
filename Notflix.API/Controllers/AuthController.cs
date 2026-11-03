using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Notflix.API.Models;
using Notflix.API.Repositories;
namespace Notflix.API.Controllers;

[ApiController, Route("api/auth")]
public sealed class AuthController(IUserRepository users, IPasswordHasher<NotflixUser> hasher, IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("csrf")]
    public IActionResult Csrf()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
        { HttpOnly = false, Secure = Request.IsHttps, SameSite = SameSiteMode.Strict, Path = "/", IsEssential = true });
        return NoContent();
    }
    [HttpPost("login"), EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var user = await users.FindAsync(request.UserName, ct);
        if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password) == PasswordVerificationResult.Failed)
            return Unauthorized(new { message = "Invalid username or password." });
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Name, user.UserName) }, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return Ok(new SessionUser(user.Id, user.UserName));
    }
    [HttpGet("me"), Authorize]
    public IActionResult Me() => Ok(new SessionUser(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!), User.Identity!.Name!));
    [HttpPost("logout"), Authorize]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        Response.Cookies.Delete("XSRF-TOKEN", new CookieOptions { Path = "/" });
        return NoContent();
    }
}
