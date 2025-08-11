using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using backend.Hubs;
using backend.Models;

namespace backend.Services;

public interface IDeviceDataService
{
    Task SendAverageDeviceData();
    Task StartDataPolling();
    Task StopDataPolling();
    Task UpdateScanInterval(int newIntervalMs);
}

public class DeviceDataService : IDeviceDataService, IDisposable
{
    private readonly BmsContext _context;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<DeviceDataService> _logger;
    private Timer? _pollingTimer;
    private int _currentScanInterval = 5000; // По умолчанию 5 секунд

    public DeviceDataService(
        BmsContext context, 
        IHubContext<NotificationHub> hubContext,
        ILogger<DeviceDataService> logger)
    {
        _context = context;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task SendAverageDeviceData()
    {
        try
        {
            // Получаем все устройства
            var devices = await _context.Devices
                .Include(d => d.DeviceSettings)
                .Include(d => d.Parent)
                .ToListAsync();

            var latestDeviceData = new List<object>();

            foreach (var device in devices)
            {
                // Получаем последнюю запись электрических данных для устройства напрямую
                var latestDatum = await _context.ElectricityDeviceData
                    .Where(ed => ed.DeviceId == device.Id)
                    .OrderByDescending(ed => ed.TimeReading)
                    .FirstOrDefaultAsync();

                if (latestDatum != null)
                {
                    var actualValues = new Dictionary<string, double>();

                    // Извлекаем ВСЕ числовые значения из последней записи
                    foreach (var prop in latestDatum.GetType().GetProperties())
                    {
                        // Проверяем, является ли свойство числовым типом (double, decimal, float)
                        // И исключаем Id, DeviceId, TimeReading и навигационные свойства
                        if ((prop.PropertyType == typeof(double) || prop.PropertyType == typeof(decimal) || prop.PropertyType == typeof(float)) &&
                            prop.Name != "Id" && prop.Name != "DeviceId" && prop.Name != "TimeReading" && prop.Name != "Device")
                        {
                            var value = prop.GetValue(latestDatum);
                            if (value != null)
                            {
                                // Получаем значение свойства и округляем до 3 знаков после запятой
                                actualValues[prop.Name] = Math.Round(Convert.ToDouble(value), 3);
                            }
                        }
                    }

                    latestDeviceData.Add(new
                    {
                        deviceId = device.Id,
                        deviceName = device.Name,
                        objectName = device.Parent?.Name,
                        statusColor = device.Active ? "green" : "red",
                        averageValues = actualValues, // Все параметры из electricity_device_data
                        lastUpdate = DateTime.Now
                    });
                }
            }

            // Отправляем данные через SignalR
            await _hubContext.Clients.Group("notifications").SendAsync("DeviceDataUpdate", latestDeviceData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при отправке данных устройств");
        }
    }

    public async Task StartDataPolling()
    {
        try
        {
            // Получаем среднее значение scan_interval из базы данных
            var averageScanInterval = await _context.DeviceSettings
                .Where(ds => ds.ScanInterval > 0)
                .AverageAsync(ds => (double)ds.ScanInterval);

            // Если нет данных, используем значение по умолчанию
            _currentScanInterval = (int)Math.Round(averageScanInterval > 0 ? averageScanInterval : 5000);
            
            // Останавливаем предыдущий таймер, если он существует
            _pollingTimer?.Dispose();

            // Создаем новый таймер с текущим интервалом опроса
            _pollingTimer = new Timer(async _ => await SendAverageDeviceData(), null, 0, _currentScanInterval);
            
            _logger.LogInformation($"Device data polling started with interval: {_currentScanInterval}ms (average from database)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting device data polling");
            // В случае ошибки используем значение по умолчанию
            _currentScanInterval = 5000;
            _pollingTimer = new Timer(async _ => await SendAverageDeviceData(), null, 0, _currentScanInterval);
        }
    }

    public async Task StopDataPolling()
    {
        _pollingTimer?.Dispose();
        _pollingTimer = null;
    }

    public async Task UpdateScanInterval(int newIntervalMs)
    {
        try
        {
            // Получаем новое среднее значение scan_interval из базы данных
            var averageScanInterval = await _context.DeviceSettings
                .Where(ds => ds.ScanInterval > 0)
                .AverageAsync(ds => (double)ds.ScanInterval);

            // Если нет данных, используем переданное значение
            _currentScanInterval = (int)Math.Round(averageScanInterval > 0 ? averageScanInterval : newIntervalMs);
            
            // Перезапускаем опрос с новым интервалом
            if (_pollingTimer != null)
            {
                await StopDataPolling();
                await StartDataPolling();
            }
            
            _logger.LogInformation($"Scan interval updated to: {_currentScanInterval}ms (average from database)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating scan interval");
            // В случае ошибки используем переданное значение
            _currentScanInterval = newIntervalMs;
            if (_pollingTimer != null)
            {
                await StopDataPolling();
                await StartDataPolling();
            }
        }
    }

    public void Dispose()
    {
        _pollingTimer?.Dispose();
    }
} 