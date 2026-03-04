using SkyWatch.Api.Services;

namespace SkyWatch.Api.Workers;

public class ImageryWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ImageryWorker> _logger;
    private readonly DataCaptureService _capture;
    private readonly WorkerToggleService _workerToggle;

    public ImageryWorker(IServiceProvider services, IConfiguration configuration,
        ILogger<ImageryWorker> logger, DataCaptureService capture, WorkerToggleService workerToggle)
    {
        _services = services;
        _configuration = configuration;
        _logger = logger;
        _capture = capture;
        _workerToggle = workerToggle;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        var intervalMinutes = _configuration.GetValue("ImageryRefreshIntervalMinutes", 30);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_workerToggle.IsEnabled("imagery"))
            {
                await FetchImagery(stoppingToken);
            }

            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }

    private async Task FetchImagery(CancellationToken ct)
    {
        try
        {
            using var scope = _services.CreateScope();
            var imageryService = scope.ServiceProvider.GetRequiredService<OpenSourceImageryService>();
            await imageryService.RefreshImageryAsync(ct);

            if (_capture.IsEnabled)
                _capture.LogData("imagery", imageryService.GetRecentScenes());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in imagery worker");
        }
    }
}
