using System;

namespace backend.Contracts;

public class CreateReportRequest
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long? CreatedByUserId { get; set; }
}

public class CreateVisualizationReportRequest
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long? CreatedByUserId { get; set; }
    public List<string> Parameters { get; set; } = new();
    public List<VisualizationDataRow> Data { get; set; } = new();
}

public class VisualizationDataRow
{
    public DateTime Timestamp { get; set; }
    public Dictionary<string, double> Values { get; set; } = new();
}

public class ReportResponse
{
    public long Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
    public string Path { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string CreatedByUserName { get; set; } = string.Empty;
    public string CreatedByUserSurname { get; set; } = string.Empty;
}

public class ReportListResponse
{
    public List<ReportResponse> Reports { get; set; } = new();
    public int TotalCount { get; set; }
} 