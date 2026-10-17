using Shared.Enums;

namespace Shared.Models;

public sealed class WorkspaceSnapshot
{
    public string DisplayName { get; set; } = "";
    public string TimeZone { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter<BillingPlan>))]
    public BillingPlan Plan { get; set; } = BillingPlan.Free;
    public string ClientId { get; set; } = "";
    public bool Muted { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter<OnboardingState>))]
    public OnboardingState FirstRunState { get; set; } = OnboardingState.NotStarted;
    public List<ProjectRecord> Projects { get; set; } = [];
    public List<TaskRecord> Tasks { get; set; } = [];
    public List<FocusSessionRecord> Sessions { get; set; } = [];
    public TimerSnapshot Timer { get; set; } = new();
    public List<ConversationRecord> Conversations { get; set; } = [];
}
