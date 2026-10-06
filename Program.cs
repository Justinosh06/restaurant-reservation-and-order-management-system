using Stripe;
using RestaurantReservation.Infrastructure.Stripe;

using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.EntityFrameworkCore;
using RestaurantReservation.Data;
using RestaurantReservation.Infrastructure.Navigation;

var builder = WebApplication.CreateBuilder(args);

// Bind and Register Stripe Settings
var stripeSettings = builder.Configuration.GetSection("Stripe").Get<StripeSettings>();
builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection("Stripe"));

// Set global API key for Stripe.net
StripeConfiguration.ApiKey = stripeSettings?.SecretKey;

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddScoped<SidebarNavService>();

// builder.Services.AddScoped<PaymentService>();

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

app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

// 3. Minimal API endpoints filtered by host header
app.MapGet("/api/info", () => "Main Domain API")
   .RequireHost("localhost");

app.MapGet("/api/info", () => "Admin Domain API")
   .RequireHost("admin.localhost");

// 4. Map Razor Pages endpoints directly
app.MapRazorPages();

app.Run();