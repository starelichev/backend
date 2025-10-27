using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using backend.Models;
using backend.Contracts;
using backend.Hubs;
using Microsoft.AspNetCore.SignalR;
using System.Linq;

namespace backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DashboardController : ControllerBase
    {
        private readonly BmsContext _context;
        private readonly IHubContext<NotificationHub> _hubContext;
        
        public DashboardController(BmsContext context, IHubContext<NotificationHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        [HttpGet("metrics")]
        public ActionResult<DashboardMetricsResponse> GetDashboardMetrics()
        {
            try
            {
                var now = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Utc);
                var todayStart = DateTime.SpecifyKind(new DateTime(now.Year, now.Month, now.Day, 0, 0, 0), DateTimeKind.Utc);
                var monthStart = DateTime.SpecifyKind(new DateTime(now.Year, now.Month, 1, 0, 0, 0), DateTimeKind.Utc);
                var yesterdayStart = todayStart.AddDays(-1);
                var yesterdayEnd = todayStart.AddSeconds(-1);
                
                // Площадки с их device_id
                var sites = new Dictionary<string, List<long>>
                {
                    { "КВТ-Юг", new List<long> { 18, 25 } },
                    { "ЛЦ", new List<long> { 17 } },
                    { "КВТ-Восток", new List<long> { 3 } },
                    { "КВТ-Север", new List<long> { 16 } },
                    { "РСК", new List<long> { 36, 35 } }
                };

                var response = new DashboardMetricsResponse
                {
                    // Расход за текущий месяц (из таблицы consumption_by_month)
                    MonthlyConsumption = GetConsumptionForPeriod(sites, "month", monthStart, now),
                    
                    // Расход за день (из consumption_by_today)
                    DailyConsumption = GetConsumptionForPeriod(sites, "today", todayStart, now),
                    
                    // Расход за вчера (из consumption_by_day)
                    PreviousDayConsumption = GetConsumptionForPeriod(sites, "day", yesterdayStart, yesterdayEnd)
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("metrics/update-today")]
        public async Task<IActionResult> UpdateTodayMetrics()
        {
            try
            {
                var now = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Utc);
                var todayStart = DateTime.SpecifyKind(new DateTime(now.Year, now.Month, now.Day, 0, 0, 0), DateTimeKind.Utc);
                
                // Площадки с их device_id
                var sites = new Dictionary<string, List<long>>
                {
                    { "КВТ-Юг", new List<long> { 18, 25 } },
                    { "ЛЦ", new List<long> { 17 } },
                    { "КВТ-Восток", new List<long> { 3 } },
                    { "КВТ-Север", new List<long> { 16 } },
                    { "РСК", new List<long> { 36, 35 } }
                };

                var dailyData = GetConsumptionForPeriod(sites, "today", todayStart, now);

                // Отправляем обновленные данные через SignalR
                await _hubContext.Clients.Group("notifications").SendAsync("UpdateConsumptionToday", dailyData);

                return Ok(new { message = "Данные расхода за сегодня обновлены", data = dailyData });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        private List<SiteConsumptionData> GetConsumptionForPeriod(Dictionary<string, List<long>> sites, string periodType, DateTime startDate, DateTime endDate)
        {
            var result = new List<SiteConsumptionData>();

            foreach (var site in sites)
            {
                decimal totalValue = 0;

                foreach (var deviceId in site.Value)
                {
                    decimal value = 0;

                    // Для месяца берем данные из consumption_by_day и суммируем все значения
                    // Для дня и сегодня - берем последнее значение
                    if (periodType == "month")
                    {
                        var monthStart = DateOnly.FromDateTime(startDate);
                        var monthEnd = DateOnly.FromDateTime(endDate);
    
                        value = _context.ConsumptionByDay
                            .Where(c => c.DeviceId == deviceId && c.Dt >= monthStart && c.Dt <= monthEnd)
                            .Sum(c => c.Value);
                    }
                    else if (periodType == "day")
                    {
                        var yesterday = startDate.Date; // startDate: 10/26/2025 00:00
                        var lastValue = _context.ConsumptionByDay
                            .Where(c => c.DeviceId == deviceId && c.Dt == DateOnly.FromDateTime(yesterday))
                            .Select(c => c.Value)
                            .FirstOrDefault();
    
                        value = lastValue;
                    }
                    else if (periodType == "today")
                    {
                        var now = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Utc);
                        var todayStart = DateTime.SpecifyKind(new DateTime(now.Year, now.Month, now.Day, 0, 0, 0), DateTimeKind.Utc);

                        var lastValue = _context.ConsumptionByToday
                            .Where(c => c.DeviceId == deviceId && 
                                        c.Dt >= todayStart && 
                                        c.Dt <= now)
                            .OrderByDescending(c => c.Dt)
                            .Select(c => c.Value)
                            .FirstOrDefault();

                        value = lastValue;
                    }

                    totalValue += value;
                }

                result.Add(new SiteConsumptionData
                {
                    SiteName = site.Key,
                    ElectricityConsumption = Math.Round(totalValue, 2),
                    GasConsumption = 0 // Для электричества газ = 0
                });
            }

            return result;
        }

        private List<SiteConsumptionData> GetSitesConsumptionData(DateTime startDate, DateTime endDate)
        {
            var sites = new List<SiteConsumptionData>
            {
                new SiteConsumptionData { SiteName = "КВТ-Юг" },
                new SiteConsumptionData { SiteName = "ЛЦ" },
                new SiteConsumptionData { SiteName = "КВТ-Восток" },
                new SiteConsumptionData { SiteName = "КВТ-Север" },
                new SiteConsumptionData { SiteName = "РСК" }
            };

            // Получаем данные по электричеству для каждой площадки
            foreach (var site in sites)
            {
                site.ElectricityConsumption = GetElectricityConsumptionForSite(site.SiteName, startDate, endDate);
            }

            // Получаем данные по газу для площадок (кроме ЛЦ и РСК)
            var gasSites = sites.Where(s => s.SiteName != "ЛЦ" && s.SiteName != "РСК").ToList();
            foreach (var site in gasSites)
            {
                site.GasConsumption = GetGasConsumptionForSite(site.SiteName, startDate, endDate);
            }

            return sites;
        }

        private decimal GetElectricityConsumptionForSite(string siteName, DateTime startDate, DateTime endDate)
        {
            try
            {
                Console.WriteLine($"=== GetElectricityConsumptionForSite: {siteName} ===");
                Console.WriteLine($"Period: {startDate} - {endDate}");
                
                // Определяем счетчики для каждой площадки
                var meterNames = siteName switch
                {
                    "КВТ-Юг" => new[] { "Трансформатор 1", "Трансформатор 2" },
                    "ЛЦ" => new[] { "Логистический центр" },
                    "КВТ-Восток" => new[] { "КТП" },
                    "КВТ-Север" => new[] { "КТП 808" },
                    "РСК" => new[] { "РСК" },
                    _ => new string[0]
                };

                Console.WriteLine($"Meter names for {siteName}: {string.Join(", ", meterNames)}");

                if (!meterNames.Any()) return 0;

                decimal totalConsumption = 0;

                // Для каждого счетчика получаем данные через хранимую функцию
                foreach (var meterName in meterNames)
                {
                    var device = _context.Devices.FirstOrDefault(d => d.Name == meterName);
                    if (device == null) continue;

                    Console.WriteLine($"Getting energy data for device: {meterName} (ID: {device.Id})");

                    // Вызываем хранимую функцию для получения данных о расходе энергии
                    var energyData = GetEnergyDataFromStoredFunction(device.Id);
                    
                    if (energyData != null)
                    {
                        // Определяем, какой период нас интересует
                        var now = DateTime.Now;
                        var currentMonth = new DateTime(now.Year, now.Month, 1);
                        var previousMonth = currentMonth.AddMonths(-1);
                        
                        decimal consumption = 0;
                        
                        if (startDate >= currentMonth)
                        {
                            // Запрашиваем данные за текущий месяц
                            consumption = energyData.AllEnergyCurrent;
                            Console.WriteLine($"  Current month consumption: {consumption}");
                        }
                        else if (startDate >= previousMonth && endDate < currentMonth)
                        {
                            // Запрашиваем данные за предыдущий месяц
                            consumption = energyData.AllEnergyLast;
                            Console.WriteLine($"  Previous month consumption: {consumption}");
                        }
                        else
                        {
                            // Для более старых данных используем таблицу consumption_by_month
                            consumption = GetHistoricalConsumption(device.Id, startDate, endDate);
                            Console.WriteLine($"  Historical consumption: {consumption}");
                        }
                        
                        totalConsumption += Math.Max(0, consumption);
                    }
                }

                Console.WriteLine($"Total consumption for {siteName}: {totalConsumption}");
                Console.WriteLine($"===============================================");

                return Math.Round(totalConsumption, 2);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при расчете потребления электричества для площадки {siteName}: {ex.Message}");
                return 0;
            }
        }

        private EnergyIntervalData? GetEnergyDataFromStoredFunction(long deviceId)
        {
            try
            {
                // Вызываем хранимую функцию _electro_get_energy_interval
                var result = _context.Database.SqlQueryRaw<EnergyIntervalData>(
                    "SELECT * FROM _electro_get_energy_interval({0})", deviceId).FirstOrDefault();
                
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при вызове хранимой функции для устройства {deviceId}: {ex.Message}");
                return null;
            }
        }

        private decimal GetHistoricalConsumption(long deviceId, DateTime startDate, DateTime endDate)
        {
            try
            {
                // Получаем данные из таблицы consumption_by_month
                var consumption = _context.Database.SqlQueryRaw<decimal>(
                    "SELECT COALESCE(SUM(consumption), 0) FROM consumption_by_month WHERE device_id = {0} AND consumption_date >= {1} AND consumption_date <= {2}",
                    deviceId, startDate, endDate).FirstOrDefault();
                
                return consumption;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при получении исторических данных для устройства {deviceId}: {ex.Message}");
                return 0;
            }
        }

        private decimal GetGasConsumptionForSite(string siteName, DateTime startDate, DateTime endDate)
        {
            try
            {
                Console.WriteLine($"=== GetGasConsumptionForSite: {siteName} ===");
                Console.WriteLine($"Period: {startDate} - {endDate}");
                
                // Определяем счетчики газа для каждой площадки
                var meterNames = siteName switch
                {
                    "КВТ-Юг" => new[] { "Газовый счетчик КВТ-Юг" }, // Нужно уточнить точное название счетчика
                    "КВТ-Восток" => new[] { "Газовый счетчик КВТ-Восток" }, // Нужно уточнить точное название счетчика
                    "КВТ-Север" => new[] { "Газовый счетчик КВТ-Север" }, // Нужно уточнить точное название счетчика
                    _ => new string[0]
                };

                Console.WriteLine($"Gas meter names for {siteName}: {string.Join(", ", meterNames)}");

                if (!meterNames.Any()) return 0;

                // Получаем начальные показания (самые ранние записи в периоде)
                var startData = _context.GasDeviceData
                    .Include(gd => gd.Device)
                    .Where(gd => gd.ReadingTime >= startDate && gd.ReadingTime <= endDate)
                    .Where(gd => meterNames.Contains(gd.Device.Name))
                    .OrderBy(gd => gd.ReadingTime)
                    .ToList();

                // Получаем конечные показания (самые поздние записи в периоде)
                var endData = _context.GasDeviceData
                    .Include(gd => gd.Device)
                    .Where(gd => gd.ReadingTime >= startDate && gd.ReadingTime <= endDate)
                    .Where(gd => meterNames.Contains(gd.Device.Name))
                    .OrderByDescending(gd => gd.ReadingTime)
                    .ToList();

                Console.WriteLine($"Gas start data count: {startData.Count}");
                Console.WriteLine($"Gas end data count: {endData.Count}");

                decimal totalConsumption = 0;

                // Для каждой площадки с несколькими счетчиками суммируем потребление
                foreach (var meterName in meterNames)
                {
                    var meterStartData = startData.FirstOrDefault(gd => gd.Device.Name == meterName);
                    var meterEndData = endData.FirstOrDefault(gd => gd.Device.Name == meterName);

                    Console.WriteLine($"Gas meter: {meterName}");
                    Console.WriteLine($"  Start data: {(meterStartData != null ? $"Time: {meterStartData.ReadingTime}, WorkingVolume: {meterStartData.WorkingVolume}" : "NULL")}");
                    Console.WriteLine($"  End data: {(meterEndData != null ? $"Time: {meterEndData.ReadingTime}, WorkingVolume: {meterEndData.WorkingVolume}" : "NULL")}");

                    if (meterStartData != null && meterEndData != null)
                    {
                        var consumption = meterEndData.WorkingVolume - meterStartData.WorkingVolume;
                        Console.WriteLine($"  Gas consumption: {consumption}");
                        totalConsumption += Math.Max(0, consumption);
                    }
                }

                Console.WriteLine($"Total gas consumption for {siteName}: {totalConsumption}");
                Console.WriteLine($"===============================================");

                return Math.Round(totalConsumption, 2);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при расчете потребления газа для площадки {siteName}: {ex.Message}");
                return 0;
            }
        }
    }
} 