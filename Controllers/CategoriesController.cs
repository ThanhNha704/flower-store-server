using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Web_HoaTuoi.Server.Data;
using Web_HoaTuoi.Server.DTOs;

namespace Web_HoaTuoi.Server.Controllers;

/// <summary>
/// GET /api/categories          — Danh sách tất cả danh mục (kèm số sản phẩm)
/// GET /api/categories/{slug}   — Chi tiết danh mục + sản phẩm con (paged)
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly AppDbContext _db;
    public CategoriesController(AppDbContext db) => _db = db;

    // GET /api/categories
    [HttpGet]
    public async Task<ActionResult<IEnumerable<CategoryDto>>> GetAll()
    {
        var cats = await _db.Categories
       .Where(c => c.IsActive && c.ParentCategoryId == null)
            .OrderBy(c => c.SortOrder)
  .Select(c => new CategoryDto(
    c.Id, c.Name, c.Slug, c.Description, c.ImageUrl, c.Icon, c.SortOrder,
    c.Products.Count(p => p.IsActive)
            ))
    .ToListAsync();

        return Ok(cats);
    }

    // GET /api/categories/{slug}
    [HttpGet("{slug}")]
    public async Task<ActionResult<CategoryDto>> GetBySlug(string slug)
    {
      var cat = await _db.Categories
            .Where(c => c.Slug == slug && c.IsActive)
     .Select(c => new CategoryDto(
       c.Id, c.Name, c.Slug, c.Description, c.ImageUrl, c.Icon, c.SortOrder,
    c.Products.Count(p => p.IsActive)
   ))
       .FirstOrDefaultAsync();

   if (cat is null) return NotFound();
   return Ok(cat);
    }
}
