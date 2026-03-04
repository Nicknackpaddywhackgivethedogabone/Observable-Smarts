using SkyWatch.Api.Services;

namespace SkyWatch.Api.Workers;

public class SatellitePositionWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SatellitePositionWorker> _logger;

    public SatellitePositionWorker(IServiceProvider services, IConfiguration configuration,
        ILogger<SatellitePositionWorker> logger)
    {
        _services = services;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial load
        await RefreshTleData(stoppingToken);

        var intervalHours = _configuration.GetValue("TleRefreshIntervalHours", 4);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromHours(intervalHours), stoppingToken);
            await RefreshTleData(stoppingToken);
        }
    }

    private async Task RefreshTleData(CancellationToken ct)
    {
        try
        {
            using var scope = _services.CreateScope();
            var tleService = scope.ServiceProvider.GetRequiredService<TleService>();
            await tleService.RefreshTleDataAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in satellite position worker");
            var apiStatus = _services.GetRequiredService<ApiStatusService>();
            apiStatus.ReportFailure("Celestrak", ex.Message);
        }
    }
}
