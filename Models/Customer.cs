using System.ComponentModel.DataAnnotations;

namespace PortfolioApi.Models;

public class Customer
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string DniOrCuit { get; set; } = string.Empty;
    public string TaxCondition { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;

    [Required, MaxLength(150)]
    public string Email { get; set; } = string.Empty;
    
    public string? GoogleSubjectId { get; set; }
    public bool IsEmailVerified { get; set; }

    // Para verificación con código OTP de 6 dígitos
    public string? OtpCodeHash { get; set; }
    public DateTime? OtpExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<Order> Orders { get; set; } = new List<Order>();
}






