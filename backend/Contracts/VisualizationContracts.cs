using System;
using System.Collections.Generic;

namespace backend.Contracts
{
    public class TimeInterval
    {
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
    }

    public class VisualizationDataResponse
    {
        public List<VisualizationDataPoint> Data { get; set; } = new List<VisualizationDataPoint>();
        public string Period { get; set; } = "";
        public DateTime DateFrom { get; set; }
        public DateTime DateTo { get; set; }
        public string MeterType { get; set; } = "";
        public long[] ObjectIds { get; set; } = new long[0];
        public long[] MeterIds { get; set; } = new long[0];
        public string[] Parameters { get; set; } = new string[0];
    }

    public class VisualizationDataPoint
    {
        public DateTime Timestamp { get; set; }
        public long DeviceId { get; set; }
        public string DeviceName { get; set; } = "";
        public string ObjectName { get; set; } = "";
        public Dictionary<string, decimal> Values { get; set; } = new Dictionary<string, decimal>();
    }

    public class VisualizationObject
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public List<VisualizationDevice> Devices { get; set; } = new List<VisualizationDevice>();
    }

    public class VisualizationDevice
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
    }

    public class VisualizationParameter
    {
        public string Name { get; set; } = "";
        public string Key { get; set; } = "";
        public string[] Parameters { get; set; } = new string[0];
    }

    // Новые контракты для данных о расходе и прогнозах
    public class ConsumptionDataResponse
    {
        public decimal ElectricityConsumption { get; set; }
        public decimal GasConsumption { get; set; }
        public string Period { get; set; } = "";
        public DateTime DateFrom { get; set; }
        public DateTime DateTo { get; set; }
    }

    public class ForecastDataResponse
    {
        public decimal ElectricityForecast { get; set; }
        public decimal GasForecast { get; set; }
        public string Period { get; set; } = ""; // "day", "week", "month"
        public DateTime ForecastDate { get; set; }
    }

    public class DashboardMetricsResponse
    {
        public ConsumptionDataResponse MonthlyConsumption { get; set; } = new ConsumptionDataResponse();
        public ConsumptionDataResponse DailyConsumption { get; set; } = new ConsumptionDataResponse();
        public ForecastDataResponse DayForecast { get; set; } = new ForecastDataResponse();
        public ForecastDataResponse WeekForecast { get; set; } = new ForecastDataResponse();
        public ForecastDataResponse MonthForecast { get; set; } = new ForecastDataResponse();
    }
} 