using Microsoft.AspNetCore.Mvc.ApplicationModels;
using RestaurantReservation.Infrastructure.Navigation;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<SidebarNavService>();

// 1. Register Razor Pages and the custom subdomain route provider
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

// 4. Map Razor Pages endpoints directly
app.MapRazorPages();

app.Run();