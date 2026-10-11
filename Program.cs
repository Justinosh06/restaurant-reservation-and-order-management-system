using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Authentication;
using RestaurantReservation.Infrastructure.Navigation;
using RestaurantReservation.Models;
using RestaurantReservation.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddScoped<SidebarNavService>();

// Login / Register
builder.Services.AddScoped<IPasswordHasher<Admin>, PasswordHasher<Admin>>();
builder.Services.AddScoped<IPasswordHasher<Customer>, PasswordHasher<Customer>>();
builder.Services.AddSingleton<CaptchaService>();
builder.Services.AddSingleton<AdminSignInTicketService>();

// Session stores the current CAPTCHA answer on the server side.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(20);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Cookie login with the role as a claim. The login page only exists on the main site,
// so the admin subdomain sends anonymous users back to the main site's /Login.
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.AccessDeniedPath = "/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(2);
        options.SlidingExpiration = true;

        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.Redirect(SiteHosts.IsAdminHost(context.Request)
                ? SiteHosts.MainSiteUrl(context.Request, "/Login")
                : context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.Redirect(SiteHosts.IsAdminHost(context.Request)
                ? SiteHosts.MainSiteUrl(context.Request, "/AccessDenied")
                : context.RedirectUri);
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminPortal", policy => policy.RequireRole(AppRoles.Administrator, AppRoles.Staff));
    options.AddPolicy("CustomerOnly", policy => policy.RequireRole(AppRoles.Customer));
});

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Admin", "AdminPortal");
    options.Conventions.AllowAnonymousToPage("/Admin/SignIn");
    options.Conventions.AuthorizeFolder("/Customer", "CustomerOnly");
});
builder.Services.AddSingleton<IPageRouteModelProvider>(new AdminDomainPageRouteProvider(SiteHosts.AdminSubdomain));

var app = builder.Build();

await DbSeeder.SeedAsync(app.Services);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/info", () => "Main Domain API")
   .RequireHost("localhost");

app.MapGet("/api/info", () => "Admin Domain API")
   .RequireHost("admin.localhost");

app.MapRazorPages();

app.Run();
