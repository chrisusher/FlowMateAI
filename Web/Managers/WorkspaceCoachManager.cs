using Radzen.Blazor;
using Shared.Contracts;
using Shared.Enums;
using Shared.Models;
using Web.Clients;

namespace Web.Managers;

public sealed class WorkspaceCoachManager(WorkspaceStore store, AuthClient auth, WorkspaceStatistics statistics, WorkspaceBillingManager billing) : WorkspaceManager
{
    private WorkspaceStore Store => store;

    public bool Sending { get; private set; }
    public List<ChatMessage> ChatMessages { get; private set; } = [];
    public List<ChatUser> ChatUsers { get; } = [new() { Id = "user", Name = "You" }, new() { Id = "coach", Name = "FlowMate" }];

    public void Initialize()
    {
        if (Store.Data.Conversations.Count == 0)
        {
            Store.Data.Conversations.Add(new()
            {
                Title = "Today, with intention"
            });
        }

        if (!Sending)
        {
            LoadChatMessages();
        }
    }

    public int AiPromptsUsed => billing.BillingState?.PromptsUsed ?? Store.Data.Conversations.Sum(c => c.Messages.Count(m => m.Role == ChatMessageRole.User));

    public ConversationRecord ActiveConversation => Store.Data.Conversations.FirstOrDefault() ?? new() { Title = "Today, with intention" };

    public async Task NewConversation()
    {
        Store.Data.Conversations.Insert(0, new()
        {
            Title = "A fresh perspective"
        });
        ChatMessages = [];
        await Store.SaveAsync();
    }

    public async Task SelectConversation(ConversationRecord c)
    {
        if (Store.Data.Conversations.Remove(c))
        {
            Store.Data.Conversations.Insert(0, c);
        }

        LoadChatMessages();
        await Store.SaveAsync();
    }

    private void LoadChatMessages() => ChatMessages = ActiveConversation.Messages.Select(message => new ChatMessage
    {
        Content = message.Text,
        UserId = message.Role == ChatMessageRole.User ? "user" : "coach",
        Timestamp = message.CreatedAt.LocalDateTime
    }).ToList();

    public async Task SendMessage(ChatMessage message)
    {
        var text = message.Content.Trim();

        if (text.Length == 0)
        {
            return;
        }

        if (Sending)
        {
            return;
        }

        Sending = true;
        var conversation = ActiveConversation;
        ChatMessages.Add(message);
        NotifyChanged();

        try
        {
            if (auth.Session.SignedIn)
            {
                CoachResponse? saved = null;

                try
                {
                    saved = await Store.AskCoachAsync(text, conversation.Id);
                }
                catch (HttpRequestException) { }

                if (saved is not null)
                {
                    billing.UpdatePromptUsage(saved.PromptsUsed, saved.PromptLimit);

                    if (ActiveConversation.Id == conversation.Id)
                    {
                        LoadChatMessages();
                    }

                    NotifyChanged();

                    return;
                }
            }

            conversation.Messages.Add(new()
            {
                Role = ChatMessageRole.User,
                Text = text
            });
            conversation.UpdatedAt = DateTimeOffset.UtcNow;
            conversation.Messages.Add(new()
            {
                Role = ChatMessageRole.Assistant,
                Text = auth.IsSessionExpired
                    ? "Your session expired before the coach could reply. Your message is saved locally; sign in again and try once more."
                    : auth.Session.SignedIn
                        ? "I couldn't reach the focus coach just now. Please try again."
                        : CoachReply(text)
            });
            await Store.SaveAsync();

            if (ActiveConversation.Id == conversation.Id)
            {
                LoadChatMessages();
            }
        }
        finally { Sending = false; NotifyChanged(); }
    }

    private string CoachReply(string prompt)
    {
        var next = statistics.PlannedTasks.Where(t => !t.IsComplete).OrderByDescending(t => t.Priority).FirstOrDefault();
        var projectText = Store.Data.Projects.Count == 0 ? "your next step" : string.Join(", ", Store.Data.Projects.Take(2).Select(p => p.Name));

        if (prompt.Contains("week", StringComparison.OrdinalIgnoreCase))
        {
            return $"You've logged {statistics.PeriodMinutes} focused minutes this week across {statistics.PeriodSessions} sessions. Your work has been spread across {projectText}. What felt easiest to return to?";
        }

        if (next is null)
        {
            return $"Your plan is clear for now. You've already put {statistics.TodayMinutes} minutes into focused work today. Would a short reset help you choose what comes next?";
        }

        var priorityReason = next.Priority == TaskPriority.High ? " because you marked it high priority" : "";

        return $"Looking at what you've planned, **{next.Title}** feels like a useful next step{priorityReason} . "
            + $"You have {statistics.TodayMinutes} focused minutes behind you today. Could you give this one a single 25-minute session?";
    }
}
