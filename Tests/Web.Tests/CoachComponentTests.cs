using System.Net;
using System.Net.Http.Json;
using Radzen.Blazor;
using Shared.Contracts;
using Shared.Enums;
using Web.Components.Features.Coach;

namespace Web.Tests;

public sealed class CoachComponentTests : WorkspaceComponentTest
{
    [Test]
    public void ConversationListCardSortsAndSelectsConversations()
    {
        Store.Data.Conversations.Add(new() { Title = "Recent conversation", UpdatedAt = DateTimeOffset.UtcNow.AddMinutes(1) });
        var cut = Render<ConversationListCard>();
        Assert.That(Content(cut.Find("strong")), Is.EqualTo("Recent conversation"));
        cut.Find(".conversation-item").Click();
        Assert.That(Coach.ActiveConversation.Title, Is.EqualTo("Recent conversation"));
        Assert.That(cut.Find(".conversation-item").ClassName, Does.Contain("selected"));
    }

    [Test]
    public void CoachChatCardSuggestionsSendMessagesAndLoadSavedHistory()
    {
        var cut = Render<CoachChatCard>();
        Assert.That(cut.FindAll(".suggestion-chips button").Count, Is.EqualTo(2));
        cut.Find(".suggestion-chips button").Click();
        Assert.That(Coach.ActiveConversation.Messages.Count, Is.EqualTo(2));
        Assert.That(Coach.ActiveConversation.Messages[1].Text, Does.Contain("Choose one thing"));
        Assert.That(cut.FindComponent<RadzenChat>().Instance.Messages.Count(), Is.EqualTo(2));
        Assert.That(cut.Find(".coach-note").TextContent, Does.Contain("Coach can suggest, never change your work."));
    }

    [Test]
    public async Task CoachChatCardKeepsAnInFlightRequestAcrossComponentDisposal()
    {
        await SignInAsync();
        await Billing.InitializeAsync();
        var response = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        Api.Respond = (_, _) => response.Task;
        var cut = Render<CoachChatCard>();
        var conversation = Coach.ActiveConversation;
        var sending = cut.InvokeAsync(() => Coach.SendMessage(new() { Content = "Review my week", UserId = "user" }));
        cut.WaitForState(() => cut.FindComponent<RadzenChat>().Instance.Disabled);
        Assert.That(cut.FindComponent<RadzenChat>().Instance.Disabled, Is.True);
        var list = Render<ConversationListCard>();

        foreach (var button in list.FindAll("button"))
        {
            Assert.That(button.HasAttribute("disabled"), Is.True);
        }
        cut.Dispose();
        var savedConversation = new ConversationRecord
        {
            Id = conversation.Id,
            Title = conversation.Title,
            Messages = [new() { Role = ChatMessageRole.Assistant, Text = "Saved coach response" }]
        };
        response.SetResult(new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new CoachResponse(savedConversation, "revision", 7, 10), options: ChrisUsher.Core.Shared.SharedCommon.JsonOptions)
        });
        await sending;
        Assert.That(Coach.Sending, Is.False);
        Assert.That(Coach.ActiveConversation.Messages.Single().Text, Is.EqualTo("Saved coach response"));
        Assert.That(Coach.AiPromptsUsed, Is.EqualTo(7));
        var reopened = Render<CoachChatCard>();
        Assert.That(reopened.FindComponent<RadzenChat>().Instance.Messages.Single().Content, Does.Contain("Saved coach response"));
    }

    [Test]
    public async Task CoachChatCardShowsApiFailureAndDoesNotModifyTasks()
    {
        await SignInAsync();
        var cut = Render<CoachChatCard>();
        cut.Find(".suggestion-chips button").Click();
        Assert.That(Coach.ChatMessages[1].Content, Does.Contain("I couldn't reach the focus coach just now."));
        Assert.That(Store.Data.Tasks.Count, Is.EqualTo(2));
        Assert.That(PlannedTask.IsComplete, Is.False);
    }
}
