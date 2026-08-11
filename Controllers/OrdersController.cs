using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Web_HoaTuoi.Server.Data;
using Web_HoaTuoi.Server.DTOs;
using Web_HoaTuoi.Server.Models;
using Web_HoaTuoi.Server.Services;

namespace Web_HoaTuoi.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    // === Thông tin tài khoản ngân hàng để tạo VietQR ===
    private const string QrBankId = "VCB";               // Vietcombank
    private const string QrAccountNumber = "1029045872"; // Số tài khoản thật
    private const string QrAccountName = "PHAN THI KIM LY"; // Tên tài khoản

    private readonly AppDbContext _db;
    private readonly IInventoryService _inventory;
    private readonly IZaloPayService _zaloPay;
    private readonly ILogger<OrdersController> _logger;
    private readonly IConfiguration _config;

    public OrdersController(
        AppDbContext db,
        IInventoryService inventory,
        IZaloPayService zaloPay,
        ILogger<OrdersController> logger,
        IConfiguration config)
    {
        _db = db;
        _inventory = inventory;
        _zaloPay = zaloPay;
        _logger = logger;
        _config = config;
    }

    // POST /api/orders
    [HttpPost]
    [Authorize]
    [EnableRateLimiting("OrderLimit")]
    public async Task<ActionResult<object>> CreateOrder([FromBody] CreateOrderRequest req)
    {
        if (!req.Items.Any())
            return BadRequest(new { message = "Giỏ hàng trống." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var stockItems = req.Items
            .Select(i => (i.ProductId, i.Quantity))
            .ToList();

        var stockDecremented = false;

        try
        {
            // ===== Trừ kho Redis =====
            var outOfStock = await _inventory.DecrementStockAsync(stockItems);

            if (outOfStock.Any())
            {
                var names = await _db.Products
                    .Where(p => outOfStock.Contains(p.Id))
                    .Select(p => p.Name)
                    .ToListAsync();

                return Conflict(new
                {
                    message = $"Hết hàng: {string.Join(", ", names)}"
                });
            }

            stockDecremented = true;

            // ===== Tạo OrderItems =====
            var orderItems = new List<OrderItem>();
            decimal totalAmount = 0;

            foreach (var item in req.Items)
            {
                totalAmount += item.UnitPrice * item.Quantity;

                orderItems.Add(new OrderItem
                {
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    ProductImage = item.MainImageUrl,
                    UnitPrice = item.UnitPrice,
                    Quantity = item.Quantity
                });
            }

            // ===== Tạo mã đơn =====
            var orderCode =
                $"ORD-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";

            var order = new Order
            {
                OrderCode = orderCode,
                UserId = userId,
                Status = OrderStatus.Pending,
                ReceiverName = req.ReceiverName,
                ReceiverPhone = req.ReceiverPhone,
                ReceiverAddress = req.ReceiverAddress,
                MessageCard = req.MessageCard,
                DeliveryTime = req.DeliveryTime,
                IsStorePickup = req.IsStorePickup,
                ShippingFee = req.ShippingFee,
                TotalAmount = totalAmount,
                FinalAmount = totalAmount + req.ShippingFee,
                IsPaid = false,
                CreatedAt = DateTime.UtcNow,
                Items = orderItems
            };

            _db.Orders.Add(order);
            await _db.SaveChangesAsync();

            // ===== Update Stock SQL =====
            foreach (var item in req.Items)
            {
                await _db.Products
                    .Where(p => p.Id == item.ProductId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.Stock,
                            p => p.Stock - item.Quantity)
                        .SetProperty(p => p.SoldCount,
                            p => p.SoldCount + item.Quantity));
            }

            var summary = new OrderSummaryDto(
                order.Id,
                order.OrderCode,
                order.Status.ToString(),
                order.FinalAmount,
                order.IsPaid,
                order.CreatedAt,
                order.Items.Select(i => new CartItemDto(
                    i.ProductId,
                    i.ProductName,
                    i.ProductImage ?? "",
                    i.UnitPrice,
                    i.Quantity)));

            // ===== Trả về thông tin QR nếu chọn thanh toán QR hoặc ZaloPay =====
            object? qrInfo = null;
            object? zaloPayInfo = null;

            if (req.PaymentMethod == "QrCode")
            {
                var qrAmount = Math.Max(2000, (long)order.FinalAmount);
                qrInfo = new
                {
                    bankId = QrBankId,
                    accountNumber = QrAccountNumber,
                    accountName = QrAccountName,
                    amount = qrAmount,
                    description = $"DH {order.OrderCode}",
                    orderCode = order.OrderCode,
                    orderId = order.Id
                };
            }
            else if (req.PaymentMethod == "ZaloPay")
            {
                var zpResult = await _zaloPay.CreateOrderAsync(order.OrderCode, order.FinalAmount, $"Thanh toán đơn hàng {order.OrderCode}");
                zaloPayInfo = new
                {
                    success = zpResult.Success,
                    orderUrl = zpResult.OrderUrl,
                    qrCode = zpResult.QrCode,
                    appTransId = zpResult.AppTransId,
                    message = zpResult.Message,
                    orderCode = order.OrderCode,
                    orderId = order.Id,
                    amount = (long)order.FinalAmount
                };
            }

            return Ok(new
            {
                orderSummary = summary,
                qrInfo,
                zaloPayInfo
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi tạo đơn hàng");

            if (stockDecremented)
                await _inventory.RestoreStockAsync(stockItems);

            return StatusCode(500, new
            {
                message = "Lỗi hệ thống khi tạo đơn hàng."
            });
        }
    }

    // GET /api/orders/my
    [HttpGet("my")]
    [Authorize]
    public async Task<ActionResult<IEnumerable<OrderSummaryDto>>> GetMyOrders()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        var orders = await _db.Orders
            .Where(o => o.UserId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new OrderSummaryDto(
                o.Id,
                o.OrderCode,
                o.Status.ToString(),
                o.FinalAmount,
                (bool?)o.IsPaid ?? false,
                o.CreatedAt,
                o.Items.Select(i => new CartItemDto(
                    i.ProductId,
                    i.ProductName,
                    i.ProductImage ?? "",
                    i.UnitPrice,
                    i.Quantity))))
            .ToListAsync();

        return Ok(orders);
    }

    // GET /api/orders/{id}
    [HttpGet("{id:int}")]
    [Authorize]
    public async Task<ActionResult<OrderDetailDto>> GetOrder(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isAdmin = User.IsInRole("Admin");

        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o =>
                o.Id == id && (isAdmin || o.UserId == userId));

        if (order is null)
            return NotFound();

        var dto = new OrderDetailDto(
            order.Id,
            order.OrderCode,
            order.Status.ToString(),
            order.ReceiverName,
            order.ReceiverPhone,
            order.ReceiverAddress,
            order.MessageCard,
            order.DeliveryTime,
            order.IsStorePickup,
            order.ShippingFee,
            order.TotalAmount,
            order.FinalAmount,
            order.IsPaid,
            order.VnpayTransactionId,
            order.Items.Select(i => new CartItemDto(
                i.ProductId,
                i.ProductName,
                i.ProductImage ?? "",
                i.UnitPrice,
                i.Quantity)),
            order.CreatedAt);

        return Ok(dto);
    }

    // GET /api/orders  (Admin)
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<object>> GetAllOrders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null)
    {
        var query = _db.Orders.AsQueryable();

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<OrderStatus>(status, out var parsedStatus))
        {
            query = query.Where(o => o.Status == parsedStatus);
        }

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new
            {
                o.Id,
                o.OrderCode,
                Status = o.Status.ToString(),
                o.ReceiverName,
                o.ReceiverPhone,
                o.FinalAmount,
                IsPaid = (bool?)o.IsPaid ?? false,
                o.CreatedAt
            })
            .ToListAsync();

        return Ok(new
        {
            Total = total,
            Page = page,
            PageSize = pageSize,
            Items = items
        });
    }

    // PUT /api/orders/{id}/cancel
    [HttpPut("{id:int}/cancel")]
    [Authorize]
    public async Task<ActionResult> CancelOrder(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        
        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

        if (order is null)
            return NotFound();

        if (order.Status != OrderStatus.Pending && order.Status != OrderStatus.Processing)
            return BadRequest(new { message = "Chỉ có thể huỷ đơn hàng chưa được giao." });

        order.Status = OrderStatus.Cancelled;

        // Restore stock
        foreach (var item in order.Items)
        {
            await _db.Products
                .Where(p => p.Id == item.ProductId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.Stock, p => p.Stock + item.Quantity)
                    .SetProperty(p => p.SoldCount, p => p.SoldCount - item.Quantity));
        }

        await _db.SaveChangesAsync();

        return Ok(new { message = "Huỷ đơn hàng thành công" });
    }

    // PUT /api/orders/{id}/status
    [HttpPut("{id:int}/status")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult> UpdateStatus(
        int id,
        [FromBody] UpdateOrderStatusRequest req)
    {
        var order = await _db.Orders.FindAsync(id);

        if (order is null)
            return NotFound();

        if (!Enum.TryParse<OrderStatus>(req.Status, out var newStatus))
            return BadRequest(new { message = "Status không hợp lệ." });

        order.Status = newStatus;

        await _db.SaveChangesAsync();

        return NoContent();
    }

    // PUT /api/orders/{id}/confirm-paid
    [HttpPut("{id:int}/confirm-paid")]
    [Authorize]
    public async Task<IActionResult> ConfirmPaid(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);
        if (order is null) return NotFound();

        order.IsPaid = true;
        if (order.Status == OrderStatus.Pending)
        {
            order.Status = OrderStatus.Processing;
        }
        await _db.SaveChangesAsync();

        return Ok(new { success = true, message = "Xác nhận đã thanh toán thành công!" });
    }

    // POST /api/orders/webhook/casso — Casso.vn tự động gọi khi có tiền vào tài khoản VCB
    [HttpPost("webhook/casso")]
    [AllowAnonymous]
    public async Task<IActionResult> CassoWebhook(
        [FromBody] CassoWebhookRequest req,
        [FromHeader(Name = "secure-token")] string? secureToken)
    {
        // Xác thực webhook bằng Secure Token (cấu hình trên Casso dashboard)
        var configToken = _config["Casso:WebhookToken"];

        if (!string.IsNullOrEmpty(configToken) &&
            !string.Equals(secureToken, configToken, StringComparison.Ordinal))
        {
            _logger.LogWarning("Casso Webhook: Token không hợp lệ");
            return Unauthorized(new { message = "Token không hợp lệ" });
        }

        if (req.Data == null || req.Data.Count == 0)
            return Ok(new { success = true, message = "Không có giao dịch nào." });

        var confirmedCount = 0;

        foreach (var txn in req.Data)
        {
            // Chỉ xử lý giao dịch tiền VÀO (amount > 0)
            if (txn.Amount <= 0) continue;

            var description = txn.Description ?? "";
            _logger.LogInformation(
                "Casso Webhook: Nhận giao dịch Amount={Amount}, Description={Description}",
                txn.Amount, description);

            // Tìm đơn hàng chưa thanh toán có mã trùng với nội dung chuyển khoản
            var unpaidOrders = await _db.Orders
                .Where(o => !o.IsPaid)
                .ToListAsync();

            var matchedOrder = unpaidOrders.FirstOrDefault(o =>
                description.Contains(o.OrderCode, StringComparison.OrdinalIgnoreCase));

            if (matchedOrder != null)
            {
                matchedOrder.IsPaid = true;
                matchedOrder.Status = OrderStatus.Processing;
                await _db.SaveChangesAsync();
                confirmedCount++;
                _logger.LogInformation(
                    "Casso Webhook: Đã tự động xác nhận đơn hàng {OrderCode} — {Amount}đ",
                    matchedOrder.OrderCode, txn.Amount);
            }
        }

        return Ok(new { success = true, message = $"Đã xử lý {confirmedCount} đơn hàng." });
    }

    // POST /api/orders/webhook/zalopay
    [HttpPost("webhook/zalopay")]
    [AllowAnonymous]
    public async Task<IActionResult> ZaloPayCallback([FromBody] System.Text.Json.JsonElement body)
    {
        try
        {
            _logger.LogInformation("ZaloPay Callback Body: {Body}", body.GetRawText());
            var dataStr = body.GetProperty("data").GetString();
            var reqMac = body.GetProperty("mac").GetString();

            if (dataStr != null && reqMac != null && _zaloPay.VerifyCallback(dataStr, reqMac))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(dataStr);
                var root = doc.RootElement;
                var appTransId = root.GetProperty("app_trans_id").GetString();

                if (!string.IsNullOrEmpty(appTransId))
                {
                    var orders = await _db.Orders.Where(o => !o.IsPaid).ToListAsync();
                    var matchedOrder = orders.FirstOrDefault(o => appTransId.Contains(o.OrderCode.Replace("-", ""), StringComparison.OrdinalIgnoreCase));
                    if (matchedOrder != null)
                    {
                        matchedOrder.IsPaid = true;
                        matchedOrder.Status = OrderStatus.Processing;
                        await _db.SaveChangesAsync();
                    }
                }

                return Ok(new { return_code = 1, return_message = "success" });
            }

            return Ok(new { return_code = -1, return_message = "mac not equal" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi xử lý ZaloPay Callback");
            return Ok(new { return_code = 0, return_message = ex.Message });
        }
    }
}

public record UpdateOrderStatusRequest(string Status);

// DTO cho Casso.vn Webhook
public class CassoWebhookRequest
{
    public int Error { get; set; }
    public List<CassoTransaction> Data { get; set; } = new();
}

public class CassoTransaction
{
    public long Id { get; set; }
    public string? Tid { get; set; }
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public decimal CusumBalance { get; set; }
    public string? When { get; set; }
    public string? BankSubAccId { get; set; }
    public string? SubAccId { get; set; }
    public string? CorresponsiveName { get; set; }
    public string? CorresponsiveAccount { get; set; }
    public string? CorresponsiveBankId { get; set; }
    public string? CorresponsiveBankName { get; set; }
}