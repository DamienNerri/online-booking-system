using OnlineBooking.Api.Repositories;

namespace OnlineBooking.Api.Workers;

/// <summary>
/// Tâche de fond périodique qui libère les réservations temporaires (HOLD)
/// expirées (Req 4.3). L'opération est atomique côté base (une seule requête CTE),
/// donc sûre même si plusieurs nœuds exécutent ce worker en parallèle.
/// </summary>
public sealed class HoldExpirationWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<HoldExpirationWorker> _logger;
    private readonly TimeSpan _interval;

    public HoldExpirationWorker(IServiceProvider services, IConfiguration config,
        ILogger<HoldExpirationWorker> logger)
    {
        _services = services;
        _logger = logger;
        var seconds = config.GetValue<int?>("Booking:SweepIntervalSeconds") ?? 30;
        _interval = TimeSpan.FromSeconds(Math.Max(1, seconds));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<BookingRepository>();
                var released = await repo.ReleaseExpiredHoldsAsync(stoppingToken);
                if (released > 0)
                {
                    _logger.LogInformation("Holds expirés libérés : {Count}", released);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Échec de la libération des holds expirés");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
