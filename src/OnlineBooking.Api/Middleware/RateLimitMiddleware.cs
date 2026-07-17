using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using OnlineBooking.Api.Configuration;

namespace OnlineBooking.Api.Middleware;

/// <summary>
/// Limitation de débit par client (fenêtre glissante en mémoire), identifié par
/// l'utilisateur ou à défaut par l'IP (Req 9.3). Locale au nœud : un comptage
/// inter-nœuds nécessiterait un magasin partagé (Redis).
/// </summary>
public sealed class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RateLimitOptions _options;
    private readonly ILogger<RateLimitMiddleware> _logger;

    private static readonly ConcurrentDictionary<string, Queue<DateTime>> _hits = new();

    public RateLimitMiddleware(RequestDelegate next, IOptions<RateLimitOptions> options,
        ILogger<RateLimitMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var key = context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                  ?? context.User?.FindFirst("sub")?.Value
                  ?? context.Connection.RemoteIpAddress?.ToString()
                  ?? "unknown";

        var now = DateTime.UtcNow;
        var window = TimeSpan.FromSeconds(_options.WindowSeconds);
        var queue = _hits.GetOrAdd(key, _ => new Queue<DateTime>());

        bool limited;
        lock (queue)
        {
            while (queue.Count > 0 && now - queue.Peek() > window)
            {
                queue.Dequeue();
            }
            if (queue.Count >= _options.MaxRequests)
            {
                limited = true;
            }
            else
            {
                queue.Enqueue(now);
                limited = false;
            }
        }

        if (limited)
        {
            _logger.LogWarning("Rate limit dépassé pour {Key} — {Path}", key, context.Request.Path);
            context.Response.StatusCode = 429;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(
                new { error = new { code = "RATE_LIMITED", message = "Trop de requêtes." } });
            return;
        }

        await _next(context);
    }
}
