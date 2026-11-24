using System.Reflection;
using API.Functions.V1.Projects;
using Microsoft.Azure.Functions.Worker;

namespace API.Tests;

[TestFixture]
public sealed class WorkspaceItemRoutesTests
{
    [TestCase("CreateProject", "v1/projects", "post")]
    [TestCase("RenameProject", "v1/projects/{id}", "put")]
    [TestCase("RestoreProject", "v1/projects/{id}/restore", "post")]
    [TestCase("ArchiveProject", "v1/projects/{id}/archive", "post")]
    public void RoutesExposeProjectWriteFunctions(string functionName, string route, string verb)
    {
        var method = typeof(ListProjects).Assembly.GetTypes().Single(type => type.Name == functionName).GetMethod("Run")!;
        var function = method.GetCustomAttribute<FunctionAttribute>();
        var trigger = method.GetParameters().Select(parameter => parameter.GetCustomAttribute<HttpTriggerAttribute>()).Single(attribute => attribute is not null)!;

        Assert.That(function!.Name, Is.EqualTo(functionName));
        Assert.That(trigger.Route, Is.EqualTo(route));
        Assert.That(trigger.Methods, Is.EquivalentTo(new[] { verb }));
    }

    [TestCase("ListProjects", "v1/projects")]
    [TestCase("GetProject", "v1/projects/{id}")]
    [TestCase("ListTasks", "v1/tasks")]
    [TestCase("GetTask", "v1/tasks/{id}")]
    [TestCase("ListSessions", "v1/sessions")]
    [TestCase("GetSession", "v1/sessions/{id}")]
    [TestCase("ListConversations", "v1/conversations")]
    [TestCase("GetConversation", "v1/conversations/{id}")]
    [TestCase("GetWorkspaceMetadata", "v1/workspace/metadata")]
    [TestCase("GetWorkspaceTimer", "v1/workspace/timer")]
    public void RoutesExposeGetFunctions(string functionName, string route)
    {
        var method = typeof(ListProjects).Assembly.GetTypes().Single(type => type.Name == functionName).GetMethod("Run")!;
        var function = method.GetCustomAttribute<FunctionAttribute>();
        var trigger = method.GetParameters().Select(parameter => parameter.GetCustomAttribute<HttpTriggerAttribute>()).Single(attribute => attribute is not null)!;

        Assert.That(function!.Name, Is.EqualTo(functionName));
        Assert.That(trigger.Route, Is.EqualTo(route));
        Assert.That(trigger.Methods, Is.EquivalentTo(new[] { "get" }));
    }
}
