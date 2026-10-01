using Shared.Models;

namespace Shared.Contracts;

public sealed record CoachResponse(ConversationRecord Conversation, string Revision, int PromptsUsed, int PromptLimit);
