using OnlineBooking.Api.Common;

namespace OnlineBooking.Api.Middleware;

/// <summary>
/// Convertit les exceptions en réponses JSON avec le bon code HTTP et journalise
/// les anomalies (Req 9.4). Empêche toute fuite de détail interne au client.
/// </summary>
public sealed class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (AppException ex)
        {
            _logger.LogWarning("Anomalie métier [{Code}] {Message} — {Method} {Path}",
                ex.Code, ex.Message, context.Request.Method, context.Request.Path);
            await WriteAsync(context, ex.StatusCode, ex.Code, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur non gérée — {Method} {Path}",
                context.Request.Method, context.Request.Path);
            await WriteAsync(context, 500, "INTERNAL_ERROR", "Une erreur interne est survenue.");
        }
    }

    private static async Task WriteAsync(HttpContext context, int status, string code, string message)
    {
        if (context.Response.HasStarted)
        {
            return;
        }
        context.Response.Clear();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { error = new { code, message } });
    }
}
