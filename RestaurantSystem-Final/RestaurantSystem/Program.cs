using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using RestaurantSystem;
using RestaurantSystem.Models; // Ensure namespace matches

var builder = WebApplication.CreateBuilder(args);

// ==========================================
// Phase 1: Register dependency injection services
// ==========================================
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

AppDomain.CurrentDomain.SetData("DataDirectory", builder.Environment.ContentRootPath);

// 1. Register local database connection (SQL Server Express)
builder.Services.AddDbContext<RestaurantDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// 2. Register manual Cookie authentication (Assignment 3.3 security requirement)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";               // Redirect here if not logged in
        options.AccessDeniedPath = "/Account/AccessDenied"; // Page for insufficient permissions
    });

//builder.Services.AddDistributedMemoryCache();
// Register Session service (used to store cart data)
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30); // Cart auto-clears after 30 min inactivity
    options.Cookie.HttpOnly = true;   // Cookie security attribute
    options.Cookie.IsEssential = true; // Mark as essential cookie
});

// 4. Register MVC controller and view services
builder.Services.AddControllersWithViews();

// 5. Inject custom security Helper and HttpContext accessor
builder.Services.AddScoped<Helper>();
builder.Services.AddHttpContextAccessor();

// 6. Email service — used by Password Recovery (Email) to send real reset links via SMTP
builder.Services.AddScoped<IEmailService, EmailService>();

// ==========================================
// Phase 2: Build app and configure middleware pipeline
// ==========================================
var app = builder.Build(); // App instance created after this line

// Configure HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting(); // 1. Enable routing

app.UseSession();  // 2. Enable Session

app.UseAuthentication(); // 3. Authentication
app.UseAuthorization();  // 4. Authorization

app.MapStaticAssets();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
