using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using System;

// 1. Host Name Constraint for Admin Subdomain
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class HostNameConstraintAttribute : Attribute, IActionConstraint
{
    private readonly string _expectedHost;

    public HostNameConstraintAttribute(string expectedHost)
    {
        _expectedHost = expectedHost;
    }

    public int Order => 0;

    public bool Accept(ActionConstraintContext context)
    {
        var host = context.RouteContext.HttpContext.Request.Host.Host;

        // Matches exact subdomain or full host (e.g., "admin" in "admin.localhost")
        return string.Equals(host, _expectedHost, StringComparison.OrdinalIgnoreCase) 
               || host.StartsWith(_expectedHost + ".", StringComparison.OrdinalIgnoreCase);
    }
}

// 2. Main Domain Constraint to exclude Admin Subdomain from main routes
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class MainDomainConstraintAttribute : Attribute, IActionConstraint
{
    private readonly string _adminSubdomain;

    public MainDomainConstraintAttribute(string adminSubdomain)
    {
        _adminSubdomain = adminSubdomain;
    }

    public int Order => 0;

    public bool Accept(ActionConstraintContext context)
    {
        var host = context.RouteContext.HttpContext.Request.Host.Host;

        // Rejects requests on main site routes if the host header is the admin subdomain
        bool isAdminHost = string.Equals(host, _adminSubdomain, StringComparison.OrdinalIgnoreCase) 
                          || host.StartsWith(_adminSubdomain + ".", StringComparison.OrdinalIgnoreCase);

        return !isAdminHost;
    }
}

// 3. Page Route Model Provider
public class AdminDomainPageRouteProvider : IPageRouteModelProvider
{
    private readonly string _adminSubdomain;

    public AdminDomainPageRouteProvider(string adminSubdomain = "admin")
    {
        _adminSubdomain = adminSubdomain;
    }

    public int Order => 100; // Runs after default Razor Page route generation

    public void OnProvidersExecuting(PageRouteModelProviderContext context)
    {
        foreach (var model in context.RouteModels)
        {
            bool isAdminPage = model.RelativePath.StartsWith("/Pages/Admin", StringComparison.OrdinalIgnoreCase);

            if (isAdminPage)
            {
                // Constrain all /Pages/Admin/* routes to ONLY accept the admin subdomain
                foreach (var selector in model.Selectors)
                {
                    selector.ActionConstraints.Add(new HostNameConstraintAttribute(_adminSubdomain));
                }

                // Map /Pages/Admin/Index.cshtml to serve as the root path "/" on the admin subdomain
                if (model.RelativePath.Equals("/Pages/Admin/Index.cshtml", StringComparison.OrdinalIgnoreCase))
                {
                    model.Selectors.Add(new SelectorModel
                    {
                        AttributeRouteModel = new AttributeRouteModel
                        {
                            Template = "" // Maps to root "/"
                        },
                        ActionConstraints = { new HostNameConstraintAttribute(_adminSubdomain) }
                    });
                }
            }
            else
            {
                // Prevent main site pages from responding on the admin subdomain
                foreach (var selector in model.Selectors)
                {
                    selector.ActionConstraints.Add(new MainDomainConstraintAttribute(_adminSubdomain));
                }
            }
        }
    }

    public void OnProvidersExecuted(PageRouteModelProviderContext context) { }
}