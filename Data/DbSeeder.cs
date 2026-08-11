using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Web_HoaTuoi.Server.Models;

namespace Web_HoaTuoi.Server.Data;

/// <summary>
/// Seed dữ liệu mẫu hoa tươi vào SQL Server.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(
        AppDbContext db,
        UserManager<AppUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        // ── 1. Roles ──────────────────────────────────────────────
        foreach (var role in new[] { "Admin", "Customer" })
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        // ── 2. Admin user ─────────────────────────────────────────
        const string adminEmail = "admin@hoatuoi.vn";
        var adminUser = await userManager.FindByEmailAsync(adminEmail);
        if (adminUser is null)
        {
            var admin = new AppUser
            {
                UserName = adminEmail,
                Email = adminEmail,
                FullName = "Quản trị viên",
                EmailConfirmed = true
            };
            await userManager.CreateAsync(admin, "00000000");
            await userManager.AddToRoleAsync(admin, "Admin");
        }
        else
        {
            // Đảm bảo mật khẩu luôn là 00000000 theo yêu cầu
            await userManager.RemovePasswordAsync(adminUser);
            await userManager.AddPasswordAsync(adminUser, "00000000");
        }

        // ── 3. System Settings ──────────────────────────────────
        if (!await db.SystemSettings.AnyAsync())
        {
            db.SystemSettings.AddRange(
                new SystemSetting { Key = "ShopName", Value = "Lyp Flower" },
                new SystemSetting { Key = "Hotline", Value = "0922 222 686" },
                new SystemSetting { Key = "HeroBanner", Value = "/banner.png" },
                new SystemSetting { Key = "LogoUrl", Value = "🌸" }
            );
            await db.SaveChangesAsync();
        }

        // ── 4. Categories & Products ──────────────────────────────
        // Cập nhật tất cả các sản phẩm có giá < 2000đ thành 2000đ theo yêu cầu
        var cheapProducts = await db.Products.Where(p => p.Price < 2000 || (p.SalePrice.HasValue && p.SalePrice < 2000)).ToListAsync();
        if (cheapProducts.Any())
        {
            foreach (var p in cheapProducts)
            {
                p.Price = 2000;
                if (p.SalePrice.HasValue) p.SalePrice = 2000;
            }
            await db.SaveChangesAsync();
        }

        // Chỉ seed nếu chưa có sản phẩm nào
        if (await db.Products.AnyAsync()) 
        {
            return; // Đã có dữ liệu, không xóa/seed lại để tránh lỗi khóa ngoại
        }

        var catHoaHong = new Category { Name = "Hoa Hồng", Slug = "hoa-hong", Description = "Bó hoa hồng rực rỡ, biểu tượng của tình yêu và sự đam mê.", ImageUrl = "/hoahong/hh1.jpg", Icon = "🌹", SortOrder = 1, IsActive = true };
        var catTulip = new Category { Name = "Hoa Tulip", Slug = "hoa-tulip", Description = "Hoa Tulip nhập khẩu sang trọng, mang nét đẹp thanh lịch.", ImageUrl = "/hoatulip/tl1.jpg", Icon = "🌷", SortOrder = 2, IsActive = true };
        var catHuongDuong = new Category { Name = "Hoa Hướng Dương", Slug = "hoa-huong-duong", Description = "Hoa hướng dương rực rỡ ánh mặt trời, mang lại niềm vui và hy vọng.", ImageUrl = "/hoahuongduong/hhd1.jpg", Icon = "🌻", SortOrder = 3, IsActive = true };
        var catCamTuCau = new Category { Name = "Hoa Cẩm Tú Cầu", Slug = "hoa-cam-tu-cau", Description = "Bó hoa cẩm tú cầu bồng bềnh, tượng trưng cho sự chân thành.", ImageUrl = "/hoacamtucau/ctc1.jpg", Icon = "🌸", SortOrder = 4, IsActive = true };
        var catHoaCuoi = new Category { Name = "Hoa Cầm Tay Cô Dâu", Slug = "hoa-cuoi", Description = "Thiết kế hoa cưới tinh tế cho ngày trọng đại.", ImageUrl = "/hoacuoi/hc1.jpg", Icon = "💐", SortOrder = 5, IsActive = true };
        var catGioHoa = new Category { Name = "Giỏ Hoa / Lẵng Hoa", Slug = "gio-hoa", Description = "Giỏ hoa chúc mừng sang trọng cho các dịp đặc biệt.", ImageUrl = "/giohoa/gh1.jpg", Icon = "🧺", SortOrder = 6, IsActive = true };
        var catLan = new Category { Name = "Lan Hồ Điệp", Slug = "hoa-lan", Description = "Lan hồ điệp quý phái, món quà đẳng cấp và bền lâu.", ImageUrl = "/lan/l1.jpg", Icon = "🪴", SortOrder = 7, IsActive = true };
        var catValiHoa = new Category { Name = "Vali Hoa Độc Đáo", Slug = "vali-hoa", Description = "Vali hoa sáng tạo, phong cách hiện đại và mới lạ.", ImageUrl = "/valihoa/1.jpg", Icon = "🧳", SortOrder = 8, IsActive = true };

        var allCategories = new List<Category> { catHoaHong, catTulip, catHuongDuong, catCamTuCau, catHoaCuoi, catGioHoa, catLan, catValiHoa };
        db.Categories.AddRange(allCategories);
        await db.SaveChangesAsync();



        // ── 4. Products (Dynamic generation from assets) ─────────
        var products = new List<Product>();

        // Helper to add products in bulk
        void AddProducts(Category cat, string folder, string prefix, int count, string color, decimal basePrice)
        {
            var now = DateTime.UtcNow;
            for (int i = 1; i <= count; i++)
            {
                var fileName = string.IsNullOrEmpty(prefix) ? $"{i}.jpg" : $"{prefix}{i}.jpg";
                
                string flowerType = cat.Slug switch {
                    "hoa-hong" => "Hoa Hồng",
                    "hoa-tulip" => "Hoa Tulip",
                    "hoa-huong-duong" => "Hoa Hướng Dương",
                    "hoa-cam-tu-cau" => "Hoa Cẩm Tú Cầu",
                    "hoa-lan" => "Lan Hồ Điệp",
                    _ => "Kết hợp (Hồng, Lan, Cẩm Chướng...)"
                };

                string occasion = cat.Slug switch
                {
                    "hoa-cuoi" => "Lễ cưới, Chụp ảnh cưới",
                    "gio-hoa" => "Khai trương, Chúc mừng, Tân gia",
                    "vali-hoa" => "Quà tặng sinh nhật, Sự kiện",
                    "hoa-lan" => "Tặng đối tác, Chúc mừng, Trang trí sảnh",
                    _ => "Sinh nhật, Tình yêu, Kỷ niệm"
                };

                products.Add(new Product
                {
                    Name = $"{cat.Name} #{i:D2}",
                    Slug = $"{cat.Slug}-{i}",
                    Description = $"{cat.Name} mẫu số {i} với phong cách thiết kế hiện đại, sử dụng những bông hoa tươi mới nhất trong ngày.",
                    Price = basePrice + (i * 10000), 
                    CategoryId = cat.Id,
                    FlowerType = flowerType,
                    Color = color,
                    Occasion = occasion,
                    BouquetSize = cat.Slug switch {
                        "gio-hoa" or "vali-hoa" => "Size L (40x50cm)",
                        "hoa-lan" => "Chậu tiêu chuẩn (3-5 cành)",
                        _ => "Size M (35x45cm)"
                    },
                    WeightKg = cat.Slug switch {
                        "hoa-hong" => 1.2,
                        "hoa-tulip" => 1.0,
                        "hoa-huong-duong" => 1.5,
                        "hoa-cam-tu-cau" => 1.8,
                        "hoa-cuoi" => 0.9,
                        "gio-hoa" => 2.5,
                        "hoa-lan" => 5.0,
                        "vali-hoa" => 3.2,
                        _ => 1.2
                    },
                    Stock = 10 + i,
                    SoldCount = i * 5,
                    MainImageUrl = $"/{folder}/{fileName}",
                    IsActive = true,
                    CreatedAt = now.AddMinutes(i), // Tăng dần để cái sau cùng (#11) là mới nhất
                    UpdatedAt = now
                });
            }
        }

        AddProducts(catHoaHong, "hoahong", "hh", 20, "Đỏ, Hồng, Trắng", 450_000);
        AddProducts(catTulip, "hoatulip", "tl", 20, "Trắng, Hồng, Vàng", 1_200_000);
        AddProducts(catHuongDuong, "hoahuongduong", "hhd", 4, "Vàng rực", 350_000);
        AddProducts(catCamTuCau, "hoacamtucau", "ctc", 20, "Xanh, Tím, Hồng", 450_000);
        AddProducts(catHoaCuoi, "hoacuoi", "hc", 20, "Trắng tinh khôi, Pastel", 800_000);
        AddProducts(catGioHoa, "giohoa", "gh", 17, "Đa sắc rực rỡ", 1_500_000);
        AddProducts(catLan, "lan", "l", 11, "Trắng, Tím cánh sen", 1_690_000);
        AddProducts(catValiHoa, "valihoa", "", 20, "Hài hòa, Sang trọng", 2_000_000);

        db.Products.AddRange(products);
        await db.SaveChangesAsync();
    }
}
