using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PortfolioApi.Data;
using PortfolioApi.DTOs;
using PortfolioApi.Models;

namespace PortfolioApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CheckoutController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CheckoutController> _logger;

    public CheckoutController(
        AppDbContext context,
        IConfiguration config,
        IHttpClientFactory httpClientFactory,
        ILogger<CheckoutController> logger)
    {
        _context = context;
        _config = config;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    // POST: api/checkout
    [HttpPost]
    public async Task<ActionResult<CheckoutResponse>> CreateOrder(CreateCheckoutRequest request)
    {
        var accessToken = _config["MercadoPago:AccessToken"]
            ?? throw new InvalidOperationException("MercadoPago:AccessToken is missing.");

        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _context.Products
            .Where(p => productIds.Contains(p.Id) && p.IsActive)
            .ToDictionaryAsync(p => p.Id);

        if (products.Count != productIds.Count)
            return BadRequest(new { message = "Uno o más productos no están disponibles." });

        var orderItems = new List<OrderItem>();
        var mpItems = new List<object>();
        decimal itemsTotal = 0;

        foreach (var item in request.Items)
        {
            var product = products[item.ProductId];
            if (product.Stock < item.Quantity)
                return BadRequest(new { message = $"Stock insuficiente para '{product.Name}'. Disponible: {product.Stock}." });

            orderItems.Add(new OrderItem
            {
                ProductId = product.Id,
                Quantity = item.Quantity,
                UnitPrice = product.Price
            });

            // En Orders API los montos van formateados como string decimal con punto ("1500.00")
            mpItems.Add(new
            {
                external_code = product.Id.ToString(),
                title = product.Name,
                quantity = item.Quantity,
                unit_price = product.Price.ToString("F2", CultureInfo.InvariantCulture)
            });

            itemsTotal += product.Price * item.Quantity;
        }

        if (request.ShippingCost > 0)
        {
            mpItems.Add(new
            {
                external_code = "SHIPPING",
                title = "Costo de envío",
                quantity = 1,
                unit_price = request.ShippingCost.ToString("F2", CultureInfo.InvariantCulture)
            });
        }

        decimal totalAmount = itemsTotal + request.ShippingCost;

        // 1. Guardar la orden pendiente en PostgreSQL
        var order = new Order
        {
            CustomerFullName = request.CustomerFullName.Trim(),
            CustomerEmail = request.CustomerEmail.Trim().ToLowerInvariant(),
            CustomerPhone = request.CustomerPhone.Trim(),
            DniOrCuit = request.DniOrCuit.Trim(),
            TaxCondition = request.TaxCondition.Trim(),
            Street = request.Street.Trim(),
            StreetNumber = request.StreetNumber.Trim(),
            FloorApartment = request.FloorApartment?.Trim(),
            PostalCode = request.PostalCode.Trim(),
            City = request.City.Trim(),
            Province = request.Province.Trim(),
            ShippingCost = request.ShippingCost,
            TotalAmount = totalAmount,
            PaymentMethod = "MercadoPago",
            Status = "Pending",
            Items = orderItems
        };

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        // 2. Construir el payload oficial para POST https://api.mercadopago.com/v1/orders
        var configuredFrontend = _config["FrontendUrl"] ?? "";
        var safeFrontendUrl = configuredFrontend.Contains("localhost") || string.IsNullOrWhiteSpace(configuredFrontend)
            ? "https://vercel.app"
            : configuredFrontend.TrimEnd('/');

        var mpPayload = new
        {
            type = "online",
            processing_mode = "manual", // Obligatorio "manual" para Checkout Pro (devuelve checkout_url)
            total_amount = totalAmount.ToString("F2", CultureInfo.InvariantCulture),
            external_reference = order.Id.ToString(),
            description = $"Pedido #{order.Id}",
            payer = new
            {
                email = order.CustomerEmail
            },
            items = mpItems,
            config = new
            {
                online = new
                {
                    success_url = $"{safeFrontendUrl}/checkout/success?orderId={order.Id}",
                    failure_url = $"{safeFrontendUrl}/checkout/failure?orderId={order.Id}",
                    pending_url = $"{safeFrontendUrl}/checkout/pending?orderId={order.Id}",
                    auto_return = "approved"
                }
            }
        };

        var client = _httpClientFactory.CreateClient();
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.mercadopago.com/v1/orders");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        // Header obligatorio en Orders API para evitar cobros duplicados
        httpRequest.Headers.Add("X-Idempotency-Key", $"order-{order.Id}-{Guid.NewGuid()}");
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(mpPayload),
            Encoding.UTF8,
            "application/json");

        var response = await client.SendAsync(httpRequest);
        var responseJson = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Mercado Pago Orders API error ({StatusCode}): {Response}", response.StatusCode, responseJson);
            return StatusCode((int)response.StatusCode, new { message = "Error al crear la orden en Mercado Pago.", detail = responseJson });
        }

        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        var mpOrderId = root.GetProperty("id").GetString() ?? string.Empty;
        var checkoutUrl = root.GetProperty("checkout_url").GetString() ?? string.Empty;

        // Guardamos el ID de la Order de Mercado Pago (ej. ORDTST01...)
        order.MercadoPagoPreferenceId = mpOrderId;
        await _context.SaveChangesAsync();

        return Ok(new CheckoutResponse(order.Id, mpOrderId, checkoutUrl));
    }


    // POST: api/checkout/webhook
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook(
        [FromQuery] string? type,
        [FromQuery] string? topic,
        [FromQuery] string? id,
        [FromQuery(Name = "data.id")] string? dataId)
    {
        var accessToken = _config["MercadoPago:AccessToken"];
        if (string.IsNullOrEmpty(accessToken))
            return StatusCode(500);

        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync();
        _logger.LogInformation("Webhook MP recibido. Query: {Query} | Body: {Body}", Request.QueryString, rawBody);

        string? eventType = type ?? topic;
        string? mpOrderId = dataId ?? id;

        if (!string.IsNullOrWhiteSpace(rawBody))
        {
            try
            {
                using var bodyDoc = JsonDocument.Parse(rawBody);
                var rootBody = bodyDoc.RootElement;

                if (string.IsNullOrEmpty(eventType))
                {
                    if (rootBody.TryGetProperty("type", out var t)) eventType = t.GetString();
                    else if (rootBody.TryGetProperty("topic", out var top)) eventType = top.GetString();
                }

                if (string.IsNullOrEmpty(mpOrderId) &&
                    rootBody.TryGetProperty("data", out var d) &&
                    d.TryGetProperty("id", out var dId))
                {
                    mpOrderId = dId.GetString();
                }
            }
            catch { /* Ignorar si el body no es JSON */ }
        }

        if (eventType != "order" || string.IsNullOrWhiteSpace(mpOrderId))
            return Ok();

        await SyncOrderWithMercadoPago(mpOrderId, accessToken);
        return Ok();
    }

    private async Task SyncOrderWithMercadoPago(string mpOrderId, string accessToken)
    {
        var client = _httpClientFactory.CreateClient();
        using var getReq = new HttpRequestMessage(HttpMethod.Get, $"https://mercadopago.com{mpOrderId}");
        getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var mpRes = await client.SendAsync(getReq);
        var mpJson = await mpRes.Content.ReadAsStringAsync();

        if (!mpRes.IsSuccessStatusCode)
        {
            _logger.LogWarning("Error consultando order {MpOrderId}: {Response}", mpOrderId, mpJson);
            return;
        }

        using var doc = JsonDocument.Parse(mpJson);
        var root = doc.RootElement;

        var status = root.GetProperty("status").GetString();
        var externalRef = root.TryGetProperty("external_reference", out var extProp) ? extProp.GetString() : null;

        if (!int.TryParse(externalRef, out var orderId))
            return;

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var order = await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order is null || order.Status == "Approved")
            return;

        if (root.TryGetProperty("transactions", out var txProp) &&
            txProp.TryGetProperty("payments", out var paymentsArr) &&
            paymentsArr.ValueKind == JsonValueKind.Array &&
            paymentsArr.GetArrayLength() > 0)
        {
            order.MercadoPagoPaymentId = paymentsArr[0].GetProperty("id").GetString();
        }

        if (status is "processed" or "paid")
        {
            order.Status = "Approved";
            foreach (var item in order.Items)
            {
                var product = await _context.Products.FindAsync(item.ProductId);
                if (product is not null)
                    product.Stock = Math.Max(0, product.Stock - item.Quantity);
            }
        }
        else if (status is "failed" or "canceled" or "expired")
        {
            order.Status = "Rejected";
        }

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

}