namespace PortfolioApi.Models;

public class Customer
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string DniOrCuit { get; set; } = string.Empty;
    public string TaxCondition { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
}