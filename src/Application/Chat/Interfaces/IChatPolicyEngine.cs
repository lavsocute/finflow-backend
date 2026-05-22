namespace FinFlow.Application.Chat.Interfaces;

public interface IChatPolicyEngine
{
    ChatPolicyDecision Decide(
        ChatAuthorizationProfile profile,
        ChatIntentClassification classification,
        string query);
}

public enum ChatPolicyDecisionKind
{
    ExecuteReporting,
    ExecuteRag,
    Clarify,
    Deny
}

public sealed record ChatPolicyDecision(
    ChatPolicyDecisionKind Kind,
    string? Message = null);
