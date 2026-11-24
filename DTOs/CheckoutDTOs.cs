using System.ComponentModel.DataAnnotations;

namespace PortfolioApi.DTOs;

public record CartItemRequest(
    [Required] int ProductId,
    [Range(1, 100)] int Quantity
);

public class CreateCheckoutRequest
{
    // Datos del comprador (Argentina)
    [Required, MaxLength(150)]
    public string CustomerFullName { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(150)]
    public string CustomerEmail { get; set; } = string.Empty;

    [Required, MaxLength(30)]
    public string CustomerPhone { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string DniOrCuit { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string TaxCondition { get; set; } = "Consumidor Final";

    // Dirección de envío
    [Required, MaxLength(150)]
    public string Street { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string StreetNumber { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? FloorApartment { get; set; }

    [Required, MaxLength(15)]
    public string PostalCode { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string City { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string Province { get; set; } = string.Empty;

    [Range(0, 999999)]
    public decimal ShippingCost { get; set; }

    [Required, MinLength(1)]
    public List<CartItemRequest> Items { get; set; } = new();
}

public record CheckoutResponse(
    int OrderId,
    string MercadoPagoOrderId,
    string CheckoutUrl
);
