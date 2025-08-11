using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using backend.Models;
using backend.Contracts;
using System.Linq;

namespace backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DashboardController : ControllerBase
    {
        private readonly BmsContext _context;
        
        public DashboardController(BmsContext context)
        {
            _context = context;
        }

        [HttpGet("metrics")]
        public ActionResult<DashboardMetricsResponse> GetDashboardMetrics()
        {
            try
            {
                var now = DateTime.Now;
                var today8am = new DateTime(now.Year, now.Month, now.Day, 8, 0, 0);
                
                // Если сейчас до 8 утра, то "сегодня" начинается с 8 утра вчера
                var dayStart = now.Hour < 8 ? today8am.AddDays(-1) : today8am;
                var dayEnd = now;

                var response = new DashboardMetricsResponse
                {
                    // Расход за месяц (с начала текущего месяца)
                    MonthlyConsumption = GetConsumptionData(
                        new DateTime(now.Year, now.Month, 1),
                        now,
                        "month"
                    ),
                    
                    // Расход за день (с 8 утра)
                    DailyConsumption = GetConsumptionData(
                        dayStart,
                        dayEnd,
                        "day"
                    ),
                    
                    // Прогнозы
                    DayForecast = GetForecastData("day", now),
                    WeekForecast = GetForecastData("week", now),
                    MonthForecast = GetForecastData("month", now)
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        private ConsumptionDataResponse GetConsumptionData(DateTime startDate, DateTime endDate, string period)
        {
            // Получаем данные электрических счетчиков
            var electricityData = _context.ElectricityDeviceData
                .Where(ed => ed.TimeReading >= startDate && ed.TimeReading <= endDate)
                .ToList();

            // Получаем данные газовых счетчиков
            var gasData = _context.GasDeviceData
                .Where(gd => gd.ReadingTime >= startDate && gd.ReadingTime <= endDate)
                .ToList();

            // Вычисляем потребление электричества (сумма AllEnergy)
            var electricityConsumption = electricityData.Any() 
                ? electricityData.Sum(ed => ed.AllEnergy)
                : 0;

            // Вычисляем потребление газа (сумма WorkingVolume)
            var gasConsumption = gasData.Any() 
                ? gasData.Sum(gd => gd.WorkingVolume)
                : 0;

            return new ConsumptionDataResponse
            {
                ElectricityConsumption = Math.Round(electricityConsumption, 2),
                GasConsumption = Math.Round(gasConsumption, 2),
                Period = period,
                DateFrom = startDate,
                DateTo = endDate
            };
        }

        private ForecastDataResponse GetForecastData(string period, DateTime currentDate)
        {
            // Определяем количество дней для расчета среднего
            int daysToAverage = period switch
            {
                "day" => 7,    // Среднее за последние 7 дней
                "week" => 30,  // Среднее за последние 30 дней
                "month" => 90, // Среднее за последние 90 дней
                _ => 7
            };

            var startDate = currentDate.AddDays(-daysToAverage);
            var endDate = currentDate;

            // Получаем исторические данные
            var electricityData = _context.ElectricityDeviceData
                .Where(ed => ed.TimeReading >= startDate && ed.TimeReading <= endDate)
                .ToList();

            var gasData = _context.GasDeviceData
                .Where(gd => gd.ReadingTime >= startDate && gd.ReadingTime <= endDate)
                .ToList();

            // Группируем данные по дням
            var electricityByDay = electricityData
                .GroupBy(ed => ed.TimeReading.Date)
                .Select(g => g.Sum(ed => ed.AllEnergy))
                .ToList();

            var gasByDay = gasData
                .GroupBy(gd => gd.ReadingTime.Date)
                .Select(g => g.Sum(gd => gd.WorkingVolume))
                .ToList();

            // Вычисляем среднее потребление за день
            var avgElectricityPerDay = electricityByDay.Any() 
                ? electricityByDay.Average() 
                : 0;

            var avgGasPerDay = gasByDay.Any() 
                ? gasByDay.Average() 
                : 0;

            // Вычисляем прогноз в зависимости от периода
            var multiplier = period switch
            {
                "day" => 1,
                "week" => 7,
                "month" => 30,
                _ => 1
            };

            return new ForecastDataResponse
            {
                ElectricityForecast = Math.Round(avgElectricityPerDay * multiplier, 2),
                GasForecast = Math.Round(avgGasPerDay * multiplier, 2),
                Period = period,
                ForecastDate = currentDate
            };
        }
    }
} 