using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using backend.Hubs;
using backend.Models;
using backend.Helpers;

namespace backend.Services;

public interface IDeviceDataService
{
    Task SendAverageDeviceData();
    Task UpdateScanInterval(int newIntervalMs);
}

public class DeviceDataService : IDeviceDataService
{
    private readonly BmsContext _context;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<DeviceDataService> _logger;

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
                    var deviceParameters = new List<object>();

                    // Извлекаем все числовые значения из последней записи
                    foreach (var prop in latestDatum.GetType().GetProperties())
                    {
                        // Проверяем, является ли свойство числовым типом (double, decimal, float)
                        // И исключаем Id, DeviceId, TimeReading и навигационные свойства
                        if ((prop.PropertyType == typeof(double) || prop.PropertyType == typeof(decimal) || prop.PropertyType == typeof(float) ||
                             prop.PropertyType == typeof(double?) || prop.PropertyType == typeof(decimal?) || prop.PropertyType == typeof(float?)) &&
                            prop.Name != "Id" && prop.Name != "DeviceId" && prop.Name != "TimeReading" && prop.Name != "Device")
                        {
                            var value = prop.GetValue(latestDatum);
                            // Отправляем все параметры, даже если значение null
                            var numericValue = value != null ? Math.Round(Convert.ToDouble(value), 3) : 0.0;
                            
                            // Используем готовые хелперы для названий и единиц измерения
                            deviceParameters.Add(new
                            {
                                parameterName = NameHelper.GetParameterFullName(prop.Name),
                                parameterShortName = NameHelper.GetParameterShortName(prop.Name),
                                parameterCode = prop.Name,
                                value = numericValue,
                                unit = GetParameterUnit(prop.Name),
                                hasValue = value != null
                            });
                        }
                    }

                    latestDeviceData.Add(new
                    {
                        deviceId = device.Id,
                        deviceName = device.Name,
                        objectName = device.Parent?.Name,
                        statusColor = device.Active ? "green" : "red",
                        averageValues = deviceParameters.ToDictionary(p => ((dynamic)p).parameterCode, p => ((dynamic)p).value), // Для совместимости с фронтендом
                        parameters = deviceParameters, // Полная информация о параметрах
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

    private string GetParameterUnit(string parameterName)
    {
        return parameterName switch
        {
            var p when p.StartsWith("U") => "В",
            var p when p.StartsWith("I") => "А",
            var p when p.StartsWith("P") || p.StartsWith("Q") || p.StartsWith("Aq") => "Вт",
            var p when p.Contains("Energy") => "кВт⋅ч",
            "Freq" => "Гц",
            var p when p.StartsWith("FundPfCf") => "",
            var p when p.StartsWith("HU") || p.StartsWith("HI") => "%",
            var p when p.StartsWith("Angle") => "°",
            _ => ""
        };
    }

    public async Task UpdateScanInterval(int newIntervalMs)
    {
        try
        {
            // Обновляем scan_interval у всех устройств в базе данных
            var deviceSettings = await _context.DeviceSettings.ToListAsync();
            
            foreach (var setting in deviceSettings)
            {
                setting.ScanInterval = newIntervalMs;
            }
            await _context.SaveChangesAsync();
            
            _logger.LogInformation($"Scan interval updated to: {newIntervalMs}ms in database");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating scan interval in database");
        }
    }
} 