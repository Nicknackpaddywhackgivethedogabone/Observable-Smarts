using SkyWatch.Api.Services;

namespace SkyWatch.Api.Workers;

public class FlightWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FlightWorker> _logger;

    public FlightWorker(IServiceProvider services, IConfiguration configuration,
        ILogger<FlightWorker> logger)
    {
        _services = services;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait briefly for app to start
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        var intervalSeconds = _configuration.GetValue("FlightRefreshIntervalSeconds", 15);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var flightService = scope.ServiceProvider.GetRequiredService<FlightService>();
                await flightService.RefreshFlightDataAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in flight worker");
            }

            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
        }
    }
}
