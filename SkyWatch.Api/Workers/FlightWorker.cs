using SkyWatch.Api.Services;

namespace SkyWatch.Api.Workers;

public class FlightWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FlightWorker> _logger;
    private readonly DataCaptureService _capture;
    private readonly WorkerToggleService _workerToggle;

    public FlightWorker(IServiceProvider services, IConfiguration configuration,
        ILogger<FlightWorker> logger, DataCaptureService capture, WorkerToggleService workerToggle)
    {
        _services = services;
        _configuration = configuration;
        _logger = logger;
        _capture = capture;
        _workerToggle = workerToggle;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        var intervalSeconds = _configuration.GetValue("FlightRefreshIntervalSeconds", 15);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (_workerToggle.IsEnabled("flights"))
            {
                try
                {
                    using var scope = _services.CreateScope();
                    var flightService = scope.ServiceProvider.GetRequiredService<FlightService>();
                    await flightService.RefreshFlightDataAsync(stoppingToken);

                    if (_capture.IsEnabled)
                        _capture.LogData("flights", flightService.GetFlights());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in flight worker");
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
        }
    }
}
