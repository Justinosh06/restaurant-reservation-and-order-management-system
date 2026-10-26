using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using RestaurantReservation.Infrastructure.Authentication;

namespace RestaurantReservation.Infrastructure.Admin;

// For pages Staff may read but not change (Announcements, Promotions, Menu): GET handlers run for everyone in
// the admin portal, while POST handlers (create, edit, hide, delete) answer 403 unless the user is an
// Administrator. StaffHandlers names the POST handlers Staff may still use, e.g. "Toggle" for a dish's
// availability on the Menu page. The pages also hide the other buttons from Staff; this stops a request sent by hand.
[AttributeUsage(AttributeTargets.Class)]
public class AdministratorChangesOnlyAttribute : Attribute, IAsyncPageFilter
{
    public string[] StaffHandlers { get; set; } = [];

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var http = context.HttpContext;
        var readsOnly = HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method);
        var allowedForStaff = context.HandlerMethod?.Name is { } handler
            && StaffHandlers.Contains(handler, StringComparer.OrdinalIgnoreCase);
        if (!readsOnly && !allowedForStaff && !http.User.IsInRole(AppRoles.Administrator))
        {
            context.Result = new ObjectResult(new { error = "Only Administrators can make changes here." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}
