using Microsoft.AspNetCore.Mvc;
using backend.Models;
using backend.Contracts;
using backend.Helpers;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DeviceController : ControllerBase
    {
        private readonly BmsContext _context;
        
        public DeviceController(BmsContext context)
        {
            _context = context;
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

            return parameters.Take(7).ToList();
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
            var properties = typeof(ElectricityDeviceDatum).GetProperties()
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
            }

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

        [HttpGet("device/{id}/details")] // /api/Device/device/{id}/details
        public ActionResult<DeviceDetailsResponse> GetDeviceDetails(long id)
        {
            var device = _context.Devices
                .Include(x => x.Parent)
                .Include(x => x.DeviceType)
                .FirstOrDefault(d => d.Id == id);
            
            if (device == null)
                return NotFound();

            // Получаем параметры в зависимости от типа устройства
            var parameters = device.DeviceType?.Type.ToLower() switch
            {
                "electrical" => GetElectricalDeviceDetailParameters(id),
                "gas" => GetGasDeviceDetailParameters(id),
                _ => new List<DeviceDetailParam>()
            };

            // Определяем время последнего чтения
            DateTime? lastReading = device.DeviceType?.Type.ToLower() switch
            {
                "electrical" => _context.ElectricityDeviceData
                    .Where(ed => ed.DeviceId == id)
                    .OrderByDescending(ed => ed.TimeReading)
                    .Select(ed => ed.TimeReading)
                    .FirstOrDefault(),
                "gas" => _context.GasDeviceData
                    .Where(gd => gd.DeviceId == id)
                    .OrderByDescending(gd => gd.ReadingTime)
                    .Select(gd => gd.ReadingTime)
                    .FirstOrDefault(),
                _ => null
            };

            return Ok(new DeviceDetailsResponse
            {
                DeviceId = device.Id,
                DeviceName = device.Name,
                ObjectName = device.Parent?.Name,
                IsActive = device.Active,
                LastReading = lastReading,
                Parameters = parameters
            });
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