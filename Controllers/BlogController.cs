using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Web_HoaTuoi.Server.Data;

namespace Web_HoaTuoi.Server.Controllers;

/// <summary>
/// GET /api/blog — Danh sách bài viết đã xuất bản (Blog + Lookbook)
/// GET /api/blog/{slug}   — Chi tiết bài viết theo slug
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class BlogController : ControllerBase
{
    private readonly AppDbContext _db;
 public BlogController(AppDbContext db) => _db = db;

    public record BlogPostDto(
        int Id, string Title, string Slug, string Summary,
      string? ImageUrl, DateTime CreatedAt);

    public record BlogPostDetailDto(
        int Id, string Title, string Slug, string Summary, string Content,
        string? ImageUrl, DateTime CreatedAt);

    // GET /api/blog?type=Blog&pageSize=6&page=1
    [HttpGet]
    public async Task<ActionResult<object>> GetAll(
        [FromQuery] string? type,
        [FromQuery] int page = 1,
      [FromQuery] int pageSize = 6)
    {
        var query = _db.BlogPosts
            .Where(b => b.IsPublished)
            .AsQueryable();

  var total = await query.CountAsync();
        var items = await query
   .OrderByDescending(b => b.CreatedAt)
 .Skip((page - 1) * pageSize)
    .Take(pageSize)
            .Select(b => new BlogPostDto(
 b.Id, b.Title, b.Slug, b.Summary,
             b.ImageUrl, b.CreatedAt))
            .ToListAsync();

        return Ok(new { Total = total, Page = page, PageSize = pageSize, Items = items });
    }

    // GET /api/blog/{slug}
    [HttpGet("{slug}")]
    public async Task<ActionResult<BlogPostDetailDto>> GetBySlug(string slug)
    {
        var post = await _db.BlogPosts
      .Where(b => b.Slug == slug && b.IsPublished)
  .Select(b => new BlogPostDetailDto(
     b.Id, b.Title, b.Slug, b.Summary, b.Content,
          b.ImageUrl, b.CreatedAt))
        .FirstOrDefaultAsync();

        if (post is null) return NotFound();
        return Ok(post);
    }
}
