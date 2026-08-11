using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Web_HoaTuoi.Server.Models;

namespace Web_HoaTuoi.Server.Data;

public class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductBundle> ProductBundles => Set<ProductBundle>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Order> Orders => Set<Order>();
 public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<ReviewImage> ReviewImages => Set<ReviewImage>();
    public DbSet<WishlistItem> WishlistItems => Set<WishlistItem>();
    public DbSet<BlogPost> BlogPosts => Set<BlogPost>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<ShippingZone> ShippingZones => Set<ShippingZone>();
    public DbSet<UserAddress> UserAddresses => Set<UserAddress>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // ── Category ──────────────────────────────────────────
      builder.Entity<Category>()
    .HasIndex(c => c.Slug).IsUnique();
      // self-reference: tránh cascade cycle
        builder.Entity<Category>()
            .HasOne(c => c.ParentCategory)
  .WithMany(c => c.SubCategories)
.HasForeignKey(c => c.ParentCategoryId)
      .OnDelete(DeleteBehavior.Restrict);

        // ── Product ───────────────────────────────────────────
    builder.Entity<Product>()
     .HasIndex(p => p.Slug).IsUnique();
   builder.Entity<Product>()
       .Property(p => p.Price).HasPrecision(18, 2);
        builder.Entity<Product>()
   .Property(p => p.SalePrice).HasPrecision(18, 2);

// ── ProductBundle - tránh cascade cycle ───────────────
        builder.Entity<ProductBundle>()
            .HasOne(b => b.BundledProduct)
       .WithMany()
            .HasForeignKey(b => b.BundledProductId)
            .OnDelete(DeleteBehavior.Restrict);

     // ── Order ─────────────────────────────────────────────
        builder.Entity<Order>()
        .Property(o => o.TotalAmount).HasPrecision(18, 2);
        builder.Entity<Order>()
  .Property(o => o.FinalAmount).HasPrecision(18, 2);

        // ── OrderItem ─────────────────────────────────────────
    builder.Entity<OrderItem>()
     .Property(o => o.UnitPrice).HasPrecision(18, 2);


     // ── FlashSaleItem ─────────────────────────────────────



// ── WishlistItem ──────────────────────────────────────
     builder.Entity<WishlistItem>()
            .HasIndex(w => new { w.UserId, w.ProductId }).IsUnique();
    }
}
