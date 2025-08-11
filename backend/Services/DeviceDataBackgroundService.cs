using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace backend.Services;

public class DeviceDataBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DeviceDataBackgroundService> _logger;

    public DeviceDataBackgroundService(
        IServiceProvider serviceProvider,
        ILogger<DeviceDataBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DeviceDataBackgroundService is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var deviceDataService = scope.ServiceProvider.GetRequiredService<IDeviceDataService>();
                
                // Запускаем опрос данных
                await deviceDataService.StartDataPolling();
                
                // Ждем до следующего цикла
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while running DeviceDataBackgroundService");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        _logger.LogInformation("DeviceDataBackgroundService is stopping.");
    }
} 