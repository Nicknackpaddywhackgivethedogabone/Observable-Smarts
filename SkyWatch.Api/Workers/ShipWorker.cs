using SkyWatch.Api.Services;

namespace SkyWatch.Api.Workers;

public class ShipWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ShipWorker> _logger;
    private readonly DataCaptureService _capture;

    public ShipWorker(IServiceProvider services, IConfiguration configuration,
        ILogger<ShipWorker> logger, DataCaptureService capture)
    {
        _services = services;
        _configuration = configuration;
        _logger = logger;
        _capture = capture;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        var intervalSeconds = _configuration.GetValue("ShipRefreshIntervalSeconds", 60);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var shipService = scope.ServiceProvider.GetRequiredService<ShipService>();
                await shipService.RefreshShipDataAsync(stoppingToken);

                if (_capture.IsEnabled)
                    _capture.LogData("ships", shipService.GetShips());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ship worker");
            }

            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
        }
    }
}
