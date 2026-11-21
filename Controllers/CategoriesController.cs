using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PortfolioApi.Data;
using PortfolioApi.DTOs;
using PortfolioApi.Models;

namespace PortfolioApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly AppDbContext _context;

    public CategoriesController(AppDbContext context)
    {
        _context = context;
    }

    // GET: api/categories
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CategoryDto>>> GetCategories()
    {
        var categories = await _context.Categories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Slug))
            .ToListAsync();

        return Ok(categories);
    }

    // GET: api/categories/5
    [HttpGet("{id:int}")]
    public async Task<ActionResult<CategoryDto>> GetCategory(int id)
    {
        var category = await _context.Categories.FindAsync(id);
        if (category is null) return NotFound();

        return Ok(new CategoryDto(category.Id, category.Name, category.Slug));
    }

    // POST: api/categories
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<CategoryDto>> CreateCategory(CreateCategoryDto dto)
    {
        var normalizedSlug = dto.Slug.Trim().ToLowerInvariant();

        var exists = await _context.Categories.AnyAsync(c => c.Slug == normalizedSlug);
        if (exists)
            return Conflict(new { message = $"A category with slug '{normalizedSlug}' already exists." });

        var category = new Category
        {
            Name = dto.Name.Trim(),
            Slug = normalizedSlug
        };

        _context.Categories.Add(category);
        await _context.SaveChangesAsync();

        var result = new CategoryDto(category.Id, category.Name, category.Slug);
        return CreatedAtAction(nameof(GetCategory), new { id = category.Id }, result);
    }

    // PUT: api/categories/5
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateCategory(int id, CreateCategoryDto dto)
    {
        var category = await _context.Categories.FindAsync(id);
        if (category is null) return NotFound();

        var normalizedSlug = dto.Slug.Trim().ToLowerInvariant();
        var slugTaken = await _context.Categories.AnyAsync(c => c.Slug == normalizedSlug && c.Id != id);
        if (slugTaken)
            return Conflict(new { message = $"Slug '{normalizedSlug}' is already in use." });

        category.Name = dto.Name.Trim();
        category.Slug = normalizedSlug;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    // DELETE: api/categories/5
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        var category = await _context.Categories.FindAsync(id);
        if (category is null) return NotFound();

        var hasProducts = await _context.Products.AnyAsync(p => p.CategoryId == id);
        if (hasProducts)
            return BadRequest(new { message = "Cannot delete a category that still has products assigned." });

        _context.Categories.Remove(category);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
