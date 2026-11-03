using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Notflix.API.Models;
using Notflix.API.Repositories.Interfaces;
namespace Notflix.API.Controllers;

[ApiController, Authorize, Route("api/movies")]
public sealed class MoviesController(IMovieRepository repository) : ControllerBase
{
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => Ok(await repository.GetAllAsync(UserId, ct));
    [HttpGet("{id:int}")] public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var movie = await repository.GetAsync(id, UserId, ct);
        return movie is null ? NotFound() : Ok(movie);
    }
    [HttpPost] public async Task<IActionResult> Add(MovieRequest request, CancellationToken ct)
    {
        var movie = await repository.AddAsync(request, UserId, ct);
        return CreatedAtAction(nameof(Get), new { id = movie.Id }, movie);
    }
    [HttpPut("{id:int}")] public async Task<IActionResult> Edit(int id, MovieRequest request, CancellationToken ct)
    {
        var movie = await repository.UpdateAsync(id, request, UserId, ct);
        return movie is null ? NotFound() : Ok(movie);
    }
    [HttpPut("{id:int}/rating")] public async Task<IActionResult> Rate(int id, RatingRequest request, CancellationToken ct)
    {
        var movie = await repository.RateAsync(id, UserId, request.Rating, ct);
        return movie is null ? NotFound() : Ok(movie);
    }
    [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id, CancellationToken ct) =>
        await repository.DeleteAsync(id, ct) ? NoContent() : NotFound();
}
