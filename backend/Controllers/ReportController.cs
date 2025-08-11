using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using backend.Contracts;
using backend.Models;
using OfficeOpenXml;
using System.IO;

namespace backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReportController : ControllerBase
{
    private readonly BmsContext _context;
    private readonly IWebHostEnvironment _environment;

    public ReportController(BmsContext context, IWebHostEnvironment environment)
    {
        _context = context;
        _environment = environment;
        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
    }

    [HttpGet("list")]
    public async Task<ActionResult<ReportListResponse>> GetReports()
    {
        try
        {
            var reportsQuery = _context.Reports
                .Include(r => r.CreatedByUser)
                .OrderByDescending(r => r.CreatedAt);

            var reportsData = await reportsQuery.ToListAsync(); // Materialize data here

            var reports = reportsData.Select(r => new ReportResponse
            {
                Id = r.Id,
                Type = r.Type,
                Name = r.Name,
                Size = r.Size,
                Path = r.Path,
                CreatedAt = r.CreatedAt ?? DateTime.UtcNow,
                CreatedByUserName = r.CreatedByUser?.Name ?? "Неизвестный пользователь",
                CreatedByUserSurname = r.CreatedByUser?.Surname ?? ""
            }).ToList();

            return Ok(new ReportListResponse
            {
                Reports = reports,
                TotalCount = reports.Count()
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Ошибка при получении списка отчетов", details = ex.Message });
        }
    }

    [HttpPost("create")]
    public async Task<ActionResult<ReportResponse>> CreateReport([FromBody] CreateReportRequest request)
    {
        try
        {
            // Создаем Excel файл
            var fileName = $"report_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            var reportsPath = Path.Combine(_environment.ContentRootPath, "Reports");
            
            if (!Directory.Exists(reportsPath))
            {
                Directory.CreateDirectory(reportsPath);
            }

            var filePath = Path.Combine(reportsPath, fileName);

            using (var package = new ExcelPackage())
            {
                var worksheet = package.Workbook.Worksheets.Add("Данные");
                
                // Добавляем заголовки
                worksheet.Cells[1, 1].Value = "Дата и время";
                worksheet.Cells[1, 2].Value = "U_L1-N";
                worksheet.Cells[1, 3].Value = "U_L2-N";
                worksheet.Cells[1, 4].Value = "U_L3-N";
                worksheet.Cells[1, 5].Value = "U_L1-L2";
                worksheet.Cells[1, 6].Value = "U_L2-L3";
                worksheet.Cells[1, 7].Value = "U_L3-L1";
                worksheet.Cells[1, 8].Value = "I_L1";
                worksheet.Cells[1, 9].Value = "I_L2";
                worksheet.Cells[1, 10].Value = "I_L3";
                worksheet.Cells[1, 11].Value = "P_L1";
                worksheet.Cells[1, 12].Value = "P_L2";
                worksheet.Cells[1, 13].Value = "P_L3";
                worksheet.Cells[1, 14].Value = "P_Sum";
                worksheet.Cells[1, 15].Value = "Q_L1";
                worksheet.Cells[1, 16].Value = "Q_L2";
                worksheet.Cells[1, 17].Value = "Q_L3";
                worksheet.Cells[1, 18].Value = "Q_Sum";
                worksheet.Cells[1, 19].Value = "Energy";
                worksheet.Cells[1, 20].Value = "Reactive Energy";
                worksheet.Cells[1, 21].Value = "Frequency";

                // Стилизуем заголовки
                using (var range = worksheet.Cells[1, 1, 1, 21])
                {
                    range.Style.Font.Bold = true;
                    range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                    range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                }

                // Получаем реальные данные из базы (пример с ElectricityDeviceData)
                var data = await _context.ElectricityDeviceData
                    .OrderByDescending(d => d.TimeReading)
                    .Take(100)
                    .ToListAsync();

                int row = 2;
                foreach (var item in data)
                {
                    worksheet.Cells[row, 1].Value = item.TimeReading.ToString("dd.MM.yyyy HH:mm:ss");
                    worksheet.Cells[row, 2].Value = item.UL1N.ToString("F3");
                    worksheet.Cells[row, 3].Value = item.UL2N.ToString("F3");
                    worksheet.Cells[row, 4].Value = item.UL3N.ToString("F3");
                    worksheet.Cells[row, 5].Value = item.UL1L2.ToString("F3") ?? "0.000";
                    worksheet.Cells[row, 6].Value = item.UL2L3.ToString("F3") ?? "0.000";
                    worksheet.Cells[row, 7].Value = item.UL3L1.ToString("F3") ?? "0.000";
                    worksheet.Cells[row, 8].Value = item.IL1.ToString("F3") ?? "0.000";
                    worksheet.Cells[row, 9].Value = item.IL2.ToString("F3") ?? "0.000";
                    worksheet.Cells[row, 10].Value = item.IL3.ToString("F3") ?? "0.000";
                    worksheet.Cells[row, 11].Value = item.PL1.ToString("F3") ?? "0.000";
                    worksheet.Cells[row, 12].Value = item.PL2.ToString("F3") ?? "0.000";
                    worksheet.Cells[row, 13].Value = item.PL3.ToString("F3") ?? "0.000";
                    worksheet.Cells[row, 14].Value = item.PSum.ToString("F3") ?? "0.000";
                    worksheet.Cells[row, 15].Value = item.QL1.ToString("F3") ?? "0.000";
                    worksheet.Cells[row, 16].Value = item.QL2.ToString("F3") ?? "0.000";
                    worksheet.Cells[row, 17].Value = item.QL3.ToString("F3") ?? "0.000";
                    worksheet.Cells[row, 18].Value = item.QSum.ToString("F3");
                    worksheet.Cells[row, 19].Value = item.AllEnergy.ToString("F3");
                    worksheet.Cells[row, 20].Value = item.ReactiveEnergySum.ToString("F3");
                    worksheet.Cells[row, 21].Value = item.Freq.ToString("F3");
                    row++;
                }

                // Автоподбор ширины столбцов
                worksheet.Cells.AutoFitColumns();

                // Сохраняем файл
                var fileBytes = package.GetAsByteArray();
                await System.IO.File.WriteAllBytesAsync(filePath, fileBytes);
            }

            // Сохраняем информацию в базу данных
            var report = new Report
            {
                Type = request.Type,
                Name = request.Name,
                Path = fileName,
                Size = new FileInfo(filePath).Length,
                CreatedAt = DateTime.Now, // Изменено с DateTime.UtcNow на DateTime.Now
                CreatedByUserId = request.CreatedByUserId
            };

            _context.Reports.Add(report);
            await _context.SaveChangesAsync();

            // Получаем пользователя для ответа
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == request.CreatedByUserId);

            var response = new ReportResponse
            {
                Id = report.Id,
                Type = report.Type,
                Name = report.Name,
                Size = report.Size,
                Path = report.Path,
                CreatedAt = report.CreatedAt ?? DateTime.UtcNow,
                CreatedByUserName = user?.Name ?? "Неизвестный пользователь",
                CreatedByUserSurname = user?.Surname ?? ""
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Ошибка при создании отчета", details = ex.Message });
        }
    }

    [HttpPost("create-visualization")]
    public async Task<ActionResult<ReportResponse>> CreateVisualizationReport([FromBody] CreateVisualizationReportRequest request)
    {
        try
        {
            // Создаем Excel файл
            var fileName = $"report_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            var reportsPath = Path.Combine(_environment.ContentRootPath, "Reports");
            
            if (!Directory.Exists(reportsPath))
            {
                Directory.CreateDirectory(reportsPath);
            }

            var filePath = Path.Combine(reportsPath, fileName);

            using (var package = new ExcelPackage())
            {
                var worksheet = package.Workbook.Worksheets.Add("Данные");
                
                // Добавляем заголовки динамически
                worksheet.Cells[1, 1].Value = "Дата и время";
                for (int i = 0; i < request.Parameters.Count; i++)
                {
                    worksheet.Cells[1, i + 2].Value = request.Parameters[i];
                }

                // Стилизуем заголовки
                using (var range = worksheet.Cells[1, 1, 1, request.Parameters.Count + 1])
                {
                    range.Style.Font.Bold = true;
                    range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                    range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                }

                // Добавляем данные
                int row = 2;
                foreach (var dataRow in request.Data)
                {
                    worksheet.Cells[row, 1].Value = dataRow.Timestamp.ToString("dd.MM.yyyy HH:mm:ss");
                    
                    for (int i = 0; i < request.Parameters.Count; i++)
                    {
                        var paramName = request.Parameters[i];
                        var value = dataRow.Values.ContainsKey(paramName) ? dataRow.Values[paramName] : 0.0;
                        worksheet.Cells[row, i + 2].Value = value.ToString("F3");
                    }
                    row++;
                }

                // Автоподбор ширины столбцов
                worksheet.Cells.AutoFitColumns();

                // Сохраняем файл
                var fileBytes = package.GetAsByteArray();
                await System.IO.File.WriteAllBytesAsync(filePath, fileBytes);
            }

            // Сохраняем информацию в базу данных
            var report = new Report
            {
                Type = request.Type,
                Name = request.Name,
                Path = fileName,
                Size = new FileInfo(filePath).Length,
                CreatedAt = DateTime.Now, // Изменено с DateTime.UtcNow на DateTime.Now
                CreatedByUserId = request.CreatedByUserId
            };

            _context.Reports.Add(report);
            await _context.SaveChangesAsync();

            // Получаем пользователя для ответа
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == request.CreatedByUserId);

            var response = new ReportResponse
            {
                Id = report.Id,
                Type = report.Type,
                Name = report.Name,
                Size = report.Size,
                Path = report.Path,
                CreatedAt = report.CreatedAt ?? DateTime.UtcNow,
                CreatedByUserName = user?.Name ?? "Неизвестный пользователь",
                CreatedByUserSurname = user?.Surname ?? ""
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Ошибка при создании отчета", details = ex.Message });
        }
    }

    [HttpGet("download/{id}")]
    public async Task<IActionResult> DownloadReport(long id)
    {
        try
        {
            var report = await _context.Reports.FirstOrDefaultAsync(r => r.Id == id);
            if (report == null)
            {
                return NotFound("Отчет не найден");
            }

            var filePath = Path.Combine(_environment.ContentRootPath, "Reports", report.Path);
            if (!System.IO.File.Exists(filePath))
            {
                return NotFound("Файл отчета не найден");
            }

            var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
            return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", report.Name + ".xlsx");
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Ошибка при скачивании отчета", details = ex.Message });
        }
    }
} 