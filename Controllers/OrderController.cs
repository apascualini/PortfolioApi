using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PortfolioApi.Data;
using PortfolioApi.DTOs;

namespace PortfolioApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly AppDbContext _context;

    private static readonly HashSet<string> ValidStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Pending",
        "Approved",
        "Preparing",
        "Shipped",
        "Delivered",
        "Rejected",
        "Cancelled"
    };

    public OrdersController(AppDbContext context)
    {
        _context = context;
    }

    // GET: api/orders?status=Approved&search=perez&page=1&pageSize=20
    // Solo Admin: lista ventas filtrables por estado (ej. Approved para ver lo pendiente de despachar)
    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<ActionResult<PagedResult<OrderSummaryDto>>> GetOrders(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _context.Orders
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim();
            query = query.Where(o => o.Status.ToLower() == normalizedStatus.ToLower());
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            bool isOrderId = int.TryParse(term.TrimStart('#'), out var parsedId);

            query = query.Where(o =>
                (isOrderId && o.Id == parsedId) ||
                o.CustomerFullName.ToLower().Contains(term) ||
                o.CustomerEmail.ToLower().Contains(term) ||
                o.DniOrCuit.Contains(term));
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new OrderSummaryDto(
                o.Id,
                o.CustomerFullName,
                o.CustomerEmail,
                o.CustomerPhone,
                o.DniOrCuit,
                o.Province,
                o.City,
                o.TotalAmount,
                o.Status,
                o.CreatedAt
            ))
            .ToListAsync();

        return Ok(new PagedResult<OrderSummaryDto>(items, totalCount, page, pageSize));
    }

    // GET: api/orders/5
    // Solo Admin: devuelve DNI/CUIT, condición fiscal, dirección exacta e ítems para armar el paquete y facturar
    [Authorize(Roles = "Admin")]
    [HttpGet("{id:int}")]
    public async Task<ActionResult<OrderDetailDto>> GetOrderDetail(int id)
    {
        var order = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order is null)
            return NotFound(new { message = $"Pedido #{id} no encontrado." });

        var itemDtos = order.Items.Select(i => new OrderItemDetailDto(
            i.ProductId,
            i.Product?.Name ?? $"Producto #{i.ProductId}",
            i.Quantity,
            i.UnitPrice,
            i.Quantity * i.UnitPrice
        ));

        return Ok(new OrderDetailDto(
            order.Id,
            order.CustomerFullName,
            order.CustomerEmail,
            order.CustomerPhone,
            order.DniOrCuit,
            order.TaxCondition,
            order.Street,
            order.StreetNumber,
            order.FloorApartment,
            order.PostalCode,
            order.City,
            order.Province,
            order.ShippingCost,
            order.TotalAmount,
            order.PaymentMethod,
            order.Status,
            order.MercadoPagoPreferenceId,
            order.MercadoPagoPaymentId,
            order.CreatedAt,
            itemDtos
        ));
    }

    // PATCH: api/orders/5/status
    // Solo Admin: permite marcar el pedido como "Preparing", "Shipped", "Delivered" o "Cancelled"
    [Authorize(Roles = "Admin")]
    [HttpPatch("{id:int}/status")]
    public async Task<IActionResult> UpdateOrderStatus(int id, UpdateOrderStatusDto dto)
    {
        var matchedStatus = ValidStatuses.FirstOrDefault(s =>
            s.Equals(dto.Status.Trim(), StringComparison.OrdinalIgnoreCase));

        if (matchedStatus is null)
        {
            return BadRequest(new
            {
                message = "Estado inválido.",
                allowedStatuses = ValidStatuses
            });
        }

        var order = await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order is null)
            return NotFound(new { message = $"Pedido #{id} no encontrado." });

        // Si cancelás manualmente un pedido que ya había descontado stock, devolvemos el stock
        if (matchedStatus == "Cancelled" &&
            order.Status is "Approved" or "Preparing")
        {
            foreach (var item in order.Items)
            {
                var product = await _context.Products.FindAsync(item.ProductId);
                if (product is not null)
                {
                    product.Stock += item.Quantity;
                }
            }
        }

        order.Status = matchedStatus;
        await _context.SaveChangesAsync();

        return Ok(new { id = order.Id, status = order.Status });
    }

    // GET: api/orders/5/public?email=cliente@mail.com
    // Público (para la pantalla /checkout/success en React): valida el email para evitar enumeración de pedidos
    [HttpGet("{id:int}/public")]
    public async Task<ActionResult<PublicOrderStatusDto>> GetPublicOrderStatus(
        int id,
        [FromQuery] string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return BadRequest(new { message = "El email es requerido para consultar el pedido." });

        var normalizedEmail = email.Trim().ToLowerInvariant();

        var order = await _context.Orders
            .AsNoTracking()
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
            .FirstOrDefaultAsync(o => o.Id == id && o.CustomerEmail == normalizedEmail);

        if (order is null)
            return NotFound(new { message = "Pedido no encontrado." });

        var itemDtos = order.Items.Select(i => new OrderItemDetailDto(
            i.ProductId,
            i.Product?.Name ?? $"Producto #{i.ProductId}",
            i.Quantity,
            i.UnitPrice,
            i.Quantity * i.UnitPrice
        ));

        return Ok(new PublicOrderStatusDto(
            order.Id,
            order.Status,
            order.TotalAmount,
            order.CreatedAt,
            itemDtos
        ));
    }
}
