using Radzen.Blazor;

namespace Web.Components.Features.Coach;

public partial class CoachChatCard
{
    protected override void OnInitialized()
    {
        base.OnInitialized();
        Observe(Coach);
    }

    private RadzenChat? _chat;

    private async Task UsePrompt(string prompt)
    {
        if (Coach.Sending || _chat is null)
        {
            return;
        }

        await _chat.SendMessage(prompt, "user");
    }
}
