using System.ComponentModel.DataAnnotations;

namespace PortfolioApi.DTOs;

// --- Categories ---
public record CategoryDto(int Id, string Name, string Slug);

public class CreateCategoryDto
{
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(120)]
    public string Slug { get; set; } = string.Empty;
}

// --- Products ---
public record ProductDto(
    int Id,
    string Name,
    string Description,
    decimal Price,
    int Stock,
    string? ImageUrl,
    bool IsActive,
    int CategoryId,
    string CategoryName
);

public class CreateProductDto
{
    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Description { get; set; } = string.Empty;

    [Range(0.01, 999999999)]
    public decimal Price { get; set; }

    [Range(0, 100000)]
    public int Stock { get; set; }

    public string? ImageUrl { get; set; }
    public bool IsActive { get; set; } = true;

    [Required]
    public int CategoryId { get; set; }
}

public class UpdateProductDto : CreateProductDto { }

public record PagedResult<T>(
    IEnumerable<T> Items,
    int TotalCount,
    int Page,
    int PageSize
);
