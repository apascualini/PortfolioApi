using System.ComponentModel.DataAnnotations.Schema;

namespace PortfolioApi.Models;

public class Order
{
    public int Id { get; set; }

    // Datos del comprador (Facturación y logística Argentina)
    public string CustomerFullName { get; set; } = string.Empty;
    public string CustomerEmail { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string DniOrCuit { get; set; } = string.Empty;
    public string TaxCondition { get; set; } = "Consumidor Final";

    // Dirección de envío
    public string Street { get; set; } = string.Empty;
    public string StreetNumber { get; set; } = string.Empty;
    public string? FloorApartment { get; set; }
    public string PostalCode { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;

    // Totales y estado de pago
    [Column(TypeName = "decimal(18,2)")]
    public decimal ShippingCost { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalAmount { get; set; }

    public string PaymentMethod { get; set; } = "MercadoPago"; // "MercadoPago" o "Transferencia"
    public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected, Shipped, Delivered
    public string? MercadoPagoPreferenceId { get; set; }
    public string? MercadoPagoPaymentId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
}