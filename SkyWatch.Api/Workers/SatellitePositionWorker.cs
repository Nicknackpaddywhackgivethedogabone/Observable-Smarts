using SkyWatch.Api.Services;

namespace SkyWatch.Api.Workers;

public class SatellitePositionWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SatellitePositionWorker> _logger;
    private readonly DataCaptureService _capture;
    private readonly WorkerToggleService _workerToggle;

    public SatellitePositionWorker(IServiceProvider services, IConfiguration configuration,
        ILogger<SatellitePositionWorker> logger, DataCaptureService capture, WorkerToggleService workerToggle)
    {
        _services = services;
        _configuration = configuration;
        _logger = logger;
        _capture = capture;
        _workerToggle = workerToggle;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Always load TLE data once at startup (lightweight catalog fetch)
        await RefreshTleData(stoppingToken);

        var intervalHours = _configuration.GetValue("TleRefreshIntervalHours", 4);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromHours(intervalHours), stoppingToken);
            if (_workerToggle.IsEnabled("satellites"))
            {
                await RefreshTleData(stoppingToken);
            }
        }
    }

    private async Task RefreshTleData(CancellationToken ct)
    {
        try
        {
            using var scope = _services.CreateScope();
            var tleService = scope.ServiceProvider.GetRequiredService<TleService>();
            await tleService.RefreshTleDataAsync(ct);

            if (_capture.IsEnabled)
                _capture.LogData("satellites", tleService.GetCurrentPositions());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in satellite position worker");
            var apiStatus = _services.GetRequiredService<ApiStatusService>();
            apiStatus.ReportFailure("Celestrak", ex.Message);
        }
    }
}
