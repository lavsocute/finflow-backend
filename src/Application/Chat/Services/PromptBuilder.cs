using FinFlow.Application.Chat.Interfaces;
using FinFlow.Domain.Chat;
using FinFlow.Domain.Documents;
using System.Text;

namespace FinFlow.Application.Chat.Services;

public sealed class PromptBuilder : IPromptBuilder
{
    public Prompt BuildFullPrompt(
        string query,
        IReadOnlyList<DocumentChunk> retrievedChunks,
        ChatAccessScope scope,
        IReadOnlyList<ChatMessage> conversationHistory)
    {
        var systemPrompt = BuildSystemPrompt(scope);
        var userPrompt = BuildUserPrompt(query, retrievedChunks, scope);

        return new Prompt(systemPrompt, userPrompt, conversationHistory);
    }

    private static string BuildSystemPrompt(ChatAccessScope scope)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are FinFlow, an AI assistant for expense management.");

        if (!scope.CanAccessAllTenantData)
        {
            sb.AppendLine($"You are helping a user in department with ID {scope.DepartmentId}.");
            sb.AppendLine("You should only provide information relevant to their department and permissions.");
        }

        if (scope.AllowedChunkTypes.Count > 0)
        {
            var allowedTypes = string.Join(", ", scope.AllowedChunkTypes.OrderBy(x => x).Select(x => x.ToString()));
            sb.AppendLine($"You may only answer from these document categories: {allowedTypes}.");
        }

        switch (scope.BudgetAccess)
        {
            case BudgetAccessLevel.None:
                sb.AppendLine("Budget information is not available to this user.");
                break;
            case BudgetAccessLevel.LimitOnly:
                sb.AppendLine("You can show the user's expense limit but not aggregate spending.");
                break;
            case BudgetAccessLevel.AggregateSpent:
                sb.AppendLine("You can show aggregate spending figures but not granular expense details.");
                break;
            case BudgetAccessLevel.FullBudget:
                sb.AppendLine("You have access to full budget and expense information.");
                break;
        }

        switch (scope.ApprovalAccess)
        {
            case ApprovalAccessLevel.None:
                sb.AppendLine("Approval information is not available.");
                break;
            case ApprovalAccessLevel.OwnOnly:
                sb.AppendLine("You can only show the user's own approval history.");
                break;
            case ApprovalAccessLevel.DeptApproval:
                sb.AppendLine("You can show department-level approval information.");
                break;
            case ApprovalAccessLevel.AllApprovals:
                sb.AppendLine("You have access to all approval information in the tenant.");
                break;
        }

        sb.AppendLine("Be concise and helpful. Do not make up information.");
        return sb.ToString();
    }

    private static string BuildUserPrompt(string query, IReadOnlyList<DocumentChunk> chunks, ChatAccessScope scope)
    {
        var sb = new StringBuilder();

        if (chunks.Count > 0)
        {
            sb.AppendLine("Context from documents:");
            foreach (var chunk in chunks)
            {
                sb.AppendLine($"- [{chunk.Type}] {chunk.Content}");
            }
            sb.AppendLine();
        }

        sb.AppendLine($"User question: {query}");
        return sb.ToString();
    }
}
