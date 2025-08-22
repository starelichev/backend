using Microsoft.AspNetCore.Mvc;
using backend.Models;
using backend.Contracts;
using backend.Helpers;
using backend.Services;
using backend.Hubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;

namespace backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DeviceController : ControllerBase
    {
        private readonly BmsContext _context;
        private readonly IRabbitMQService _rabbitMQService;
        private readonly IHubContext<NotificationHub> _hubContext;
        
        public DeviceController(BmsContext context, IRabbitMQService rabbitMQService, IHubContext<NotificationHub> hubContext)
        {
            _context = context;
            _rabbitMQService = rabbitMQService;
            _hubContext = hubContext;
        }

        [HttpGet("dashboard")] // /api/Device/dashboard
        public ActionResult<DeviceDashboardResponse> GetDashboard()
        {
            var objects = _context.Objects
                .Select(o => new DeviceDashboardObject
                {
                    Id = o.Id,
                    Name = o.Name,
                    Devices = o.Devices.Select(d => new DeviceDashboardDevice
                    {
                        Id = d.Id,
                        Name = d.Name,
                        StatusColor = d.Active ? "green" : "red"
                    }).ToList()
                }).ToList();

            foreach (var dev in objects.SelectMany(obj => obj.Devices))
            {
                dev.Params = GetDeviceParameters(dev.Id);
            }

            return Ok(new DeviceDashboardResponse { Objects = objects });
        }

        private List<DeviceDashboardParam> GetDeviceParameters(long deviceId)
        {
            // Получаем устройство с его типом
            var device = _context.Devices
                .Include(d => d.DeviceType)
                .FirstOrDefault(d => d.Id == deviceId);

            if (device?.DeviceType == null)
                return new List<DeviceDashboardParam>();

            // Получаем параметры в зависимости от типа устройства
            var parameters = device.DeviceType.Type.ToLower() switch
            {
                "electrical" => GetElectricalDeviceParameters(deviceId),
                "gas" => GetGasDeviceParameters(deviceId),
                _ => new List<DeviceDashboardParam>()
            };

            return parameters.Take(6).ToList(); // Максимум 6 параметров
        }

        private List<DeviceDashboardParam> GetElectricalDeviceParameters(long deviceId)
        {
            var latestData = _context.ElectricityDeviceData
                .Where(ed => ed.DeviceId == deviceId)
                .OrderByDescending(ed => ed.TimeReading)
                .FirstOrDefault();

            if (latestData == null)
                return new List<DeviceDashboardParam>();

            var parameters = new List<DeviceDashboardParam>();
            
            // Определяем приоритетные параметры для отображения на карточках
            var priorityParams = new[] { "IL1", "IL2", "IL3", "PSum", "QSum", "AllEnergy" };
            
            // Сначала добавляем приоритетные параметры
            foreach (var priorityParam in priorityParams)
            {
                var prop = typeof(ElectricityDeviceDatum).GetProperty(priorityParam);
                if (prop != null)
                {
                    var value = prop.GetValue(latestData);
                    if (value != null && value is decimal decimalValue)
                    {
                        parameters.Add(new DeviceDashboardParam
                        {
                            Name = NameHelper.GetParameterShortName(priorityParam),
                            Value = decimalValue.ToString("F3")
                        });
                        Console.WriteLine($"✅ Added priority parameter: {priorityParam} = {decimalValue}");
                    }
                    else
                    {
                        Console.WriteLine($"❌ Priority parameter {priorityParam} has no value or is null");
                    }
                }
                else
                {
                    Console.WriteLine($"❌ Priority parameter {priorityParam} property not found");
                }
            }
            
            // Затем добавляем остальные параметры до достижения максимума в 6 штук
            var remainingProperties = typeof(ElectricityDeviceDatum).GetProperties()
                .Where(p => p.PropertyType == typeof(decimal) || p.PropertyType == typeof(decimal?))
                .Where(p => p.Name != "Id" && p.Name != "DeviceId" && !priorityParams.Contains(p.Name));

            foreach (var prop in remainingProperties)
            {
                if (parameters.Count >= 6) break; // Максимум 6 параметров
                
                var value = prop.GetValue(latestData);
                if (value != null && value is decimal decimalValue)
                {
                    parameters.Add(new DeviceDashboardParam
                    {
                        Name = NameHelper.GetParameterShortName(prop.Name),
                        Value = decimalValue.ToString("F3")
                    });
                    Console.WriteLine($"➕ Added additional parameter: {prop.Name} = {decimalValue}");
                }
            }

            Console.WriteLine($"🎯 Device {deviceId}: Total parameters returned: {parameters.Count}");
            return parameters;
        }

        private List<DeviceDashboardParam> GetGasDeviceParameters(long deviceId)
        {
            var latestData = _context.GasDeviceData
                .Where(gd => gd.DeviceId == deviceId)
                .OrderByDescending(gd => gd.ReadingTime)
                .FirstOrDefault();

            if (latestData == null)
                return new List<DeviceDashboardParam>();

            var parameters = new List<DeviceDashboardParam>();
            var properties = typeof(GasDeviceDatum).GetProperties()
                .Where(p => p.PropertyType == typeof(decimal) || p.PropertyType == typeof(decimal?))
                .Where(p => p.Name != "Id" && p.Name != "DeviceId");

            foreach (var prop in properties)
            {
                var value = prop.GetValue(latestData);
                if (value != null && value is decimal decimalValue)
                {
                    parameters.Add(new DeviceDashboardParam
                    {
                        Name = NameHelper.GetParameterShortName(prop.Name),
                        Value = decimalValue.ToString("F3")
                    });
                }
                else if (value != null && value is DateTime dateTimeValue && prop.Name == "ReadingTime")
                {
                    // Для времени чтения показываем только время
                    parameters.Add(new DeviceDashboardParam
                    {
                        Name = "Время",
                        Value = dateTimeValue.ToString("HH:mm:ss")
                    });
                }
            }

            return parameters;
        }

        [HttpGet("details/{id}")]
        public ActionResult<DeviceDetails> GetDeviceDetails(long id)
        {
            try
            {
                var device = _context.Devices
                    .Include(d => d.DeviceSettings)
                    .Include(d => d.Channel)
                    .FirstOrDefault(d => d.Id == id);

                if (device == null)
                    return NotFound(new { error = "Устройство не найдено" });

                var deviceSetting = device.DeviceSettings.FirstOrDefault();
                
                var response = new DeviceDetails
                {
                    Id = device.Id,
                    Name = device.Name,
                    Comment = device.Comment,
                    TrustedBefore = device.TrustedBefore,
                    IpAddress = device.Channel?.Ip,
                    NetworkPort = device.Channel?.Port,
                    KoeffTrans = deviceSetting?.KoeffTrans ?? 1.0,
                    ScanInterval = deviceSetting?.ScanInterval ?? 10000,
                    ChannelId = device.ChannelId,
                    ChannelName = device.Channel?.Name,
                    Active = device.Active,
                    SerialNo = device.SerialNo,
                    InstallationDate = device.InstallationDate?.ToDateTime(TimeOnly.MinValue),
                    LastReceive = device.LastReceive
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPut("edit")]
        public async Task<ActionResult<DeviceEditResponse>> EditDevice([FromBody] DeviceEditRequest request)
        {
            try
            {
                var device = _context.Devices
                    .Include(d => d.DeviceSettings)
                    .Include(d => d.Channel)
                    .FirstOrDefault(d => d.Id == request.Id);

                if (device == null)
                    return NotFound(new { error = "Устройство не найдено" });

                // Обновляем основные поля устройства
                if (!string.IsNullOrEmpty(request.Name))
                    device.Name = request.Name;
                
                if (request.Comment != null)
                    device.Comment = request.Comment;
                
                if (request.TrustedBefore.HasValue)
                    device.TrustedBefore = request.TrustedBefore.Value;

                // Обновляем IP-адрес и порт в канале
                if (device.Channel != null)
                {
                    if (!string.IsNullOrEmpty(request.IpAddress))
                        device.Channel.Ip = request.IpAddress;
                    
                    if (request.NetworkPort.HasValue)
                        device.Channel.Port = request.NetworkPort.Value;
                }

                // Обновляем коэффициент трансформации и время опроса
                var deviceSetting = device.DeviceSettings.FirstOrDefault();
                bool scanIntervalChanged = false;
                long? oldScanInterval = deviceSetting?.ScanInterval;
                
                if (deviceSetting != null)
                {
                    if (request.KoeffTrans.HasValue)
                        deviceSetting.KoeffTrans = request.KoeffTrans.Value;
                    
                    if (request.ScanInterval.HasValue)
                    {
                        scanIntervalChanged = deviceSetting.ScanInterval != request.ScanInterval.Value;
                        deviceSetting.ScanInterval = request.ScanInterval.Value;
                    }
                }
                else if (request.KoeffTrans.HasValue || request.ScanInterval.HasValue)
                {
                    // Создаем новую настройку если её нет
                    scanIntervalChanged = request.ScanInterval.HasValue;
                    deviceSetting = new DeviceSetting
                    {
                        DeviceId = device.Id,
                        KoeffTrans = request.KoeffTrans ?? 1.0,
                        ScanInterval = request.ScanInterval ?? 10000, // По умолчанию 10 секунд
                        TypeLink = 2, // TCP по умолчанию
                        Parity = 'N',
                        ProtServiceCode = 0,
                        DayDataLive = 365,
                        SuccessReceive = 0,
                        BadReceive = 0
                    };
                    _context.DeviceSettings.Add(deviceSetting);
                }

                _context.SaveChanges();

                // Отправляем сообщение в RabbitMQ и логируем действие при изменении времени опроса
                if (scanIntervalChanged && request.ScanInterval.HasValue)
                {
                    try
                    {
                        // Логируем действие пользователя
                        var userAction = new UserAction
                        {
                            UserId = request.UserId ?? 0,
                            ActionId = 6, // ID действия "Изменение времени опроса устройства"
                            Date = DateTime.Now,
                            Description = $"Время опроса устройства '{device.Name}' изменено с {oldScanInterval ?? 10000} мс на {request.ScanInterval.Value} мс"
                        };
                        _context.UserActions.Add(userAction);
                        await _context.SaveChangesAsync();

                        // Отправляем уведомление через SignalR
                        await _hubContext.Clients.Group("notifications").SendAsync("UserActionCreated", new
                        {
                            id = userAction.Id,
                            userId = userAction.UserId,
                            actionId = userAction.ActionId,
                            date = userAction.Date,
                            description = userAction.Description
                        });

                        // Отправляем сообщение в RabbitMQ
                        _rabbitMQService.SendMessage("device_scan_interval_update", new
                        {
                            device_id = device.Id,
                            device_name = device.Name,
                            old_scan_interval_ms = oldScanInterval,
                            new_scan_interval_ms = request.ScanInterval.Value,
                            timestamp = DateTime.UtcNow,
                            channel_id = device.ChannelId
                        });
                        Console.WriteLine($"RabbitMQ message sent: Device {device.Id} scan interval changed to {request.ScanInterval.Value}ms");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error sending RabbitMQ message: {ex.Message}");
                    }
                }

                var response = new DeviceEditResponse
                {
                    Success = true,
                    Message = "Устройство успешно обновлено",
                    Device = new DeviceDetails
                    {
                        Id = device.Id,
                        Name = device.Name,
                        Comment = device.Comment,
                        TrustedBefore = device.TrustedBefore,
                        IpAddress = device.Channel?.Ip,
                        NetworkPort = device.Channel?.Port,
                        KoeffTrans = deviceSetting?.KoeffTrans ?? 1.0,
                        ScanInterval = deviceSetting?.ScanInterval ?? 10000,
                        ChannelId = device.ChannelId,
                        ChannelName = device.Channel?.Name,
                        Active = device.Active,
                        SerialNo = device.SerialNo,
                        InstallationDate = device.InstallationDate?.ToDateTime(TimeOnly.MinValue),
                        LastReceive = device.LastReceive
                    }
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        private List<DeviceDetailParam> GetElectricalDeviceDetailParameters(long deviceId)
        {
            var latestData = _context.ElectricityDeviceData
                .Where(ed => ed.DeviceId == deviceId)
                .OrderByDescending(ed => ed.TimeReading)
                .FirstOrDefault();

            if (latestData == null)
                return new List<DeviceDetailParam>();

            var parameters = new List<DeviceDetailParam>();
            var properties = typeof(ElectricityDeviceDatum).GetProperties()
                .Where(p => p.PropertyType == typeof(decimal) || p.PropertyType == typeof(decimal?))
                .Where(p => p.Name != "Id" && p.Name != "DeviceId");

            foreach (var prop in properties)
            {
                var value = prop.GetValue(latestData);
                if (value != null && value is decimal decimalValue)
                {
                    parameters.Add(new DeviceDetailParam
                    {
                        ShortName = NameHelper.GetParameterShortName(prop.Name),
                        FullName = NameHelper.GetParameterFullName(prop.Name),
                        Value = decimalValue.ToString("F3"),
                        Unit = GetParameterUnit(prop.Name)
                    });
                }
            }

            return parameters;
        }

        private List<DeviceDetailParam> GetGasDeviceDetailParameters(long deviceId)
        {
            var latestData = _context.GasDeviceData
                .Where(gd => gd.DeviceId == deviceId)
                .OrderByDescending(gd => gd.ReadingTime)
                .FirstOrDefault();

            if (latestData == null)
                return new List<DeviceDetailParam>();

            var parameters = new List<DeviceDetailParam>();
            var properties = typeof(GasDeviceDatum).GetProperties()
                .Where(p => p.PropertyType == typeof(decimal) || p.PropertyType == typeof(decimal?))
                .Where(p => p.Name != "Id" && p.Name != "DeviceId");

            foreach (var prop in properties)
            {
                var value = prop.GetValue(latestData);
                if (value != null && value is decimal decimalValue)
                {
                    parameters.Add(new DeviceDetailParam
                    {
                        ShortName = NameHelper.GetParameterShortName(prop.Name),
                        FullName = NameHelper.GetParameterFullName(prop.Name),
                        Value = decimalValue.ToString("F3"),
                        Unit = GetGasParameterUnit(prop.Name)
                    });
                }
            }

            return parameters;
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

        private string GetGasParameterUnit(string parameterName)
        {
            return parameterName switch
            {
                "TemperatureGas" => "°C",
                "WorkingVolume" => "м³",
                "StandardVolume" => "м³",
                "InstantaneousFlow" => "м³/ч",
                "BatteryLive" => "%",
                "PressureGas" => "Па",
                "Power" => "Вт",
                _ => ""
            };
        }

        [HttpPut("device/{id}/update")]
        public async Task<ActionResult> UpdateDevice(long id, [FromBody] UpdateDeviceRequest request)
        {
            try
            {
                var device = await _context.Devices.FindAsync(id);
                if (device == null)
                    return NotFound(new { error = "Устройство не найдено" });

                // Обновляем комментарий если он передан
                if (request.Comment != null)
                {
                    device.Comment = request.Comment;
                }

                // Обновляем дату последней поверки если она передана
                if (request.TrustedBefore.HasValue)
                {
                    device.TrustedBefore = request.TrustedBefore.Value;
                }

                await _context.SaveChangesAsync();

                return Ok(new { message = "Устройство успешно обновлено" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Ошибка при обновлении устройства", details = ex.Message });
            }
        }

        [HttpGet("scan-interval")]
        public async Task<IActionResult> GetAverageScanInterval()
        {
            try
            {
                var averageScanInterval = await _context.DeviceSettings
                    .Where(ds => ds.ScanInterval > 0)
                    .AverageAsync(ds => (double)ds.ScanInterval);

                return Ok(new { averageScanInterval = (int)Math.Round(averageScanInterval > 0 ? averageScanInterval : 5000) });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = $"Ошибка при получении времени опроса: {ex.Message}" });
            }
        }
    }
} 