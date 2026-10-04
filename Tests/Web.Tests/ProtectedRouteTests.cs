using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Web.Components;
using Web.Components.Pages;

namespace Web.Tests;

[TestFixture]
public sealed class ProtectedRouteTests : WorkspaceComponentTest
{
    [Test]
    public void AnonymousUserSeesConfigurationMessageInsteadOfProtectedPage()
    {
        Authorization.SetNotAuthorized();
        Navigation.NavigateTo("/");

        var cut = Render<Routes>();

        cut.WaitForElement("[role=alert]");
        Assert.That(cut.Markup, Does.Contain("Sign-in is unavailable"));
        Assert.That(cut.Markup, Does.Not.Contain("Make today count."));
    }

    [Test]
    public void ConfiguredAnonymousUserSeesSignInInsteadOfProtectedPage()
    {
        Configuration["Keycloak:Url"] = "auth.example.test";
        Configuration["Keycloak:ClientId"] = "test-client";
        Configuration["Keycloak:Realm"] = "test-api";
        Authorization.SetNotAuthorized();
        Navigation.NavigateTo("/settings");

        var cut = Render<Routes>();

        cut.WaitForElement(".auth-screen");
        Assert.That(cut.Markup, Does.Contain("Sign in to keep your projects"));
        Assert.That(cut.Markup, Does.Not.Contain("Your space, tuned to how you work."));
    }

    [Test]
    public void EveryApplicationRouteExceptErrorRequiresAuthorization()
    {
        var routedComponents = typeof(Web.Program).Assembly.GetTypes()
            .Where(type => type.GetCustomAttributes<RouteAttribute>(inherit: false).Any())
            .Where(type => type.Name != "Error");

        Assert.That(routedComponents, Is.Not.Empty);
        Assert.That(
            routedComponents.Where(type => !type.GetCustomAttributes<AuthorizeAttribute>(inherit: false).Any()).Select(type => type.FullName),
            Is.Empty);
    }
}
