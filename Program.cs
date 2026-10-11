using Stripe;
using Stripe.Checkout;
using RestaurantReservation.Infrastructure.Stripe;

using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Services;
using RestaurantReservation.Infrastructure.Navigation;

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

var app = builder.Build();

// 2. Configure HTTP Pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Handles serving CSS, JS, and image files from wwwroot
app.UseStaticFiles();

app.UseRouting();

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