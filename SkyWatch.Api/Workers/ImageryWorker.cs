using SkyWatch.Api.Services;

namespace SkyWatch.Api.Workers;

public class ImageryWorker : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ImageryWorker> _logger;

    public ImageryWorker(IServiceProvider services, IConfiguration configuration,
        ILogger<ImageryWorker> logger)
    {
        _services = services;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        var intervalMinutes = _configuration.GetValue("ImageryRefreshIntervalMinutes", 30);

        // Initial fetch
        await FetchImagery(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
            await FetchImagery(stoppingToken);
        }
    }

    private async Task FetchImagery(CancellationToken ct)
    {
        try
        {
            using var scope = _services.CreateScope();
            var imageryService = scope.ServiceProvider.GetRequiredService<OpenSourceImageryService>();
            await imageryService.RefreshImageryAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in imagery worker");
        }
    }
}
