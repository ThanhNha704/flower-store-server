using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Dapper;
using System.Data;

namespace Web_HoaTuoi.Server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AnalyticsController : ControllerBase
    {
        private readonly string _connectionString;

        public AnalyticsController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")!;
        }

        // 1. Biểu đồ doanh thu: GET /api/analytics/revenue-chart
        [HttpGet("revenue-chart")]
        public IActionResult GetRevenueChart([FromQuery] string type = "week")
        {
            using var connection = new SqlConnection(_connectionString);
            var now = DateTime.UtcNow;
            
            if (type == "week")
            {
                var start = now.Date.AddDays(-6);
                var sql = @"
                    SELECT CreatedAt, FinalAmount
                    FROM Orders
                    WHERE (Status = 3 OR CAST(Status AS NVARCHAR(50)) = 'Completed') AND CreatedAt >= @start";
                
                var orders = connection.Query<(DateTime CreatedAt, decimal FinalAmount)>(sql, new { start }).ToList();
                var grouped = orders.GroupBy(o => o.CreatedAt.Date)
                    .Select(g => new { Label = g.Key.ToString("dd/MM"), Revenue = g.Sum(o => o.FinalAmount) })
                    .ToList();

                var result = Enumerable.Range(0, 7)
                    .Select(i => start.AddDays(i))
                    .Select(d => new
                    {
                        Label = d.ToString("dd/MM"),
                        Revenue = grouped.FirstOrDefault(g => g.Label == d.ToString("dd/MM"))?.Revenue ?? 0
                    }).ToList();

                return Ok(result);
            }
            else if (type == "month")
            {
                var start = new DateTime(now.Year, now.Month, 1);
                var daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
                var sql = @"
                    SELECT CreatedAt, FinalAmount
                    FROM Orders
                    WHERE (Status = 3 OR CAST(Status AS NVARCHAR(50)) = 'Completed') AND CreatedAt >= @start";
                
                var orders = connection.Query<(DateTime CreatedAt, decimal FinalAmount)>(sql, new { start }).ToList();
                var grouped = orders.GroupBy(o => o.CreatedAt.Date)
                    .Select(g => new { Label = g.Key.ToString("dd/MM"), Revenue = g.Sum(o => o.FinalAmount) })
                    .ToList();

                var result = Enumerable.Range(1, daysInMonth)
                    .Select(day => new DateTime(now.Year, now.Month, day))
                    .Select(d => new
                    {
                        Label = d.ToString("dd/MM"),
                        Revenue = grouped.FirstOrDefault(g => g.Label == d.ToString("dd/MM"))?.Revenue ?? 0
                    }).ToList();

                return Ok(result);
            }
            else // year
            {
                var start = new DateTime(now.Year, 1, 1);
                var sql = @"
                    SELECT CreatedAt, FinalAmount
                    FROM Orders
                    WHERE (Status = 3 OR CAST(Status AS NVARCHAR(50)) = 'Completed') AND CreatedAt >= @start";
                
                var orders = connection.Query<(DateTime CreatedAt, decimal FinalAmount)>(sql, new { start }).ToList();
                var grouped = orders.GroupBy(o => o.CreatedAt.Month)
                    .Select(g => new { Label = $"Tháng {g.Key}", Revenue = g.Sum(o => o.FinalAmount) })
                    .ToList();

                var result = Enumerable.Range(1, 12)
                    .Select(month => new
                    {
                        Label = $"Tháng {month}",
                        Revenue = grouped.FirstOrDefault(g => g.Label == $"Tháng {month}")?.Revenue ?? 0
                    }).ToList();

                return Ok(result);
            }
        }

        // 2. API vinh danh sản phẩm: GET /api/analytics/top-products
        [HttpGet("top-products")]
        public IActionResult GetTopProducts()
        {
            using var connection = new SqlConnection(_connectionString);
            var sql = @"
                SELECT TOP 5 
                    od.ProductId,
                    SUM(od.Quantity) AS TongSoLuongBan,
                    SUM(od.Quantity * od.UnitPrice) AS TongDoanhThu
                FROM OrderItems od
                JOIN Orders o ON od.OrderId = o.Id
                WHERE (o.Status = 3 OR CAST(o.Status AS NVARCHAR(50)) = 'Completed')
                GROUP BY od.ProductId
                ORDER BY TongSoLuongBan DESC;";
            
            var data = connection.Query(sql);
            return Ok(data);
        }

        // 3. API biểu đồ tròn trạng thái: GET /api/analytics/order-status
        [HttpGet("order-status")]
        public IActionResult GetOrderStatus()
        {
            using var connection = new SqlConnection(_connectionString);
            var sql = @"
                SELECT 
                    Status,
                    COUNT(Id) AS SoLuong
                FROM Orders
                GROUP BY Status;";
            
            var data = connection.Query(sql);
            return Ok(data);
        }
    }
}
