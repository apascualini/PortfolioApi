using System.ComponentModel.DataAnnotations;

namespace PortfolioApi.DTOs;

public record OrderItemDetailDto(
    int ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal Subtotal
);

public record OrderSummaryDto(
    int Id,
    string CustomerFullName,
    string CustomerEmail,
    string CustomerPhone,
    string DniOrCuit,
    string Province,
    string City,
    decimal TotalAmount,
    string Status,
    DateTime CreatedAt
);

public record OrderDetailDto(
    int Id,
    // Datos fiscales y de contacto (Argentina)
    string CustomerFullName,
    string CustomerEmail,
    string CustomerPhone,
    string DniOrCuit,
    string TaxCondition,
    // Dirección completa para etiqueta de envío
    string Street,
    string StreetNumber,
    string? FloorApartment,
    string PostalCode,
    string City,
    string Province,
    // Totales y referencias de Mercado Pago
    decimal ShippingCost,
    decimal TotalAmount,
    string PaymentMethod,
    string Status,
    string? MercadoPagoOrderId,
    string? MercadoPagoPaymentId,
    DateTime CreatedAt,
    IEnumerable<OrderItemDetailDto> Items
);

public class UpdateOrderStatusDto
{
    [Required]
    public string Status { get; set; } = string.Empty; // "Approved", "Preparing", "Shipped", "Delivered", "Cancelled"
}

// Vista pública acotada para que el comprador vea su comprobante tras pagar (sin exponer datos sensibles)
public record PublicOrderStatusDto(
    int Id,
    string Status,
    decimal TotalAmount,
    DateTime CreatedAt,
    IEnumerable<OrderItemDetailDto> Items
);
