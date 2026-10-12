using Stripe;
using Stripe.Checkout;

using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Authentication;
using RestaurantReservation.Infrastructure.Navigation;
using RestaurantReservation.Infrastructure.Stripe;

using RestaurantReservation.Models;
using RestaurantReservation.Data;
using RestaurantReservation.Services;

var builder = WebApplication.CreateBuilder(args);

// Bind and Register Stripe Settings
var stripeSettings = builder.Configuration.GetSection("Stripe").Get<StripeSettings>();
builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection("Stripe"));

// Set global API key for Stripe.net
StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"] ?? stripeSettings?.SecretKey;

// Register Stripe Services for Dependency Injection
builder.Services.AddScoped<SessionService>();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddScoped<SidebarNavService>();

builder.Services.AddScoped<IPaymentService, StripePaymentService>();

// Register Controllers (Required for Stripe Webhook Controller)
builder.Services.AddControllers();

// Register Razor Pages and the custom subdomain route provider
builder.Services.AddRazorPages();
builder.Services.AddSingleton<IPageRouteModelProvider>(new AdminDomainPageRouteProvider("admin"));

// 2. Configure HTTP Pipeline
// Login / Register
builder.Services.AddScoped<IPasswordHasher<Admin>, PasswordHasher<Admin>>();
builder.Services.AddScoped<IPasswordHasher<RestaurantReservation.Models.Customer>, PasswordHasher<RestaurantReservation.Models.Customer>>();
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

await DbSeeder.SeedAsync(app.Services);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Handles serving CSS, JS, and image files from wwwroot
app.UseStaticFiles();

app.UseRouting();

app.UseSession();
app.UseAuthentication();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

// 3. Minimal API endpoints filtered by host header
app.MapGet("/api/info", () => "Main Domain API")
   .RequireHost("localhost");

app.MapGet("/api/info", () => "Admin Domain API")
   .RequireHost("admin.localhost");

// 4. Map API Controllers (For Stripe Webhooks) and Razor Pages
app.MapControllers();
app.MapRazorPages();

app.Run();

