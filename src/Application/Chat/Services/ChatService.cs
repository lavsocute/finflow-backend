using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Chat;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace FinFlow.Application.Chat.Services;

public class GroqChatOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1";
    public string ChatModel { get; set; } = "llama-3.3-70b-versatile";
}

public sealed class ChatService : IChatService
{
    private const string AuthorizedNoContextMessage = "There is not enough authorized context to answer that question.";
    private const string ForbiddenScopeMessage = "Chat access denied: the requested chat scope is outside your scope and allowed department or ownership boundary.";
    private const string MissingDepartmentBoundaryMessage = "Chat access denied: your membership is missing a required department boundary for this chat scope.";
    private const string OutOfScopeChunkMessage = "Chat retrieval returned out-of-scope document chunks.";

    private readonly IChatRepository _chatRepository;
    private readonly IChatAuthorizationService _chatAuthorizationService;
    private readonly ISubscriptionFeatureGate _subscriptionFeatureGate;
    private readonly ITenantUsageService _tenantUsageService;
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly IRerankService _rerankService;
    private readonly IPromptBuilder _promptBuilder;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWork _unitOfWork;
    private readonly HttpClient _httpClient;
    private readonly GroqChatOptions _options;
    private readonly ILogger<ChatService> _logger;
    private readonly Uri _chatCompletionsUri;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ChatService(
        IChatRepository chatRepository,
        IChatAuthorizationService chatAuthorizationService,
        ISubscriptionFeatureGate subscriptionFeatureGate,
        ITenantUsageService tenantUsageService,
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        IRerankService rerankService,
        IPromptBuilder promptBuilder,
        ICurrentTenant currentTenant,
        IUnitOfWork unitOfWork,
        HttpClient httpClient,
        IOptions<GroqChatOptions> options,
        ILogger<ChatService> logger)
    {
        _chatRepository = chatRepository;
        _chatAuthorizationService = chatAuthorizationService;
        _subscriptionFeatureGate = subscriptionFeatureGate;
        _tenantUsageService = tenantUsageService;
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _rerankService = rerankService;
        _promptBuilder = promptBuilder;
        _currentTenant = currentTenant;
        _unitOfWork = unitOfWork;
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _chatCompletionsUri = BuildChatCompletionsUri(_options.BaseUrl);
    }

    public async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            throw new InvalidOperationException("Query cannot be empty.");

        var subscriptionCheck = await _subscriptionFeatureGate.EnsureChatbotAllowedAsync(request.TenantId, 1, ct);
        if (subscriptionCheck.IsFailure)
            throw new InvalidOperationException(subscriptionCheck.Error.Description);

        var accessScope = await _chatAuthorizationService.GetChatAccessScopeAsync(request.MembershipId, ct);
        EnsureTenantAccess(request, accessScope);
        var effectiveDepartmentId = ResolveDepartmentFilter(request.DepartmentId, accessScope);
        var ownerFilter = ResolveOwnerFilter(accessScope);

        Guid actualSessionId;
        ChatSession activeSession;

        if (request.SessionId.HasValue)
        {
            activeSession = await _chatRepository.GetSessionByIdAndMembershipAsync(request.SessionId.Value, request.MembershipId, ct)
                ?? throw new InvalidOperationException("Chat access denied: session was not found for the current membership.");
            actualSessionId = activeSession.Id;
        }
        else
        {
            activeSession = ChatSession.Create(request.TenantId, request.MembershipId, $"Chat {DateTime.UtcNow:yyyy-MM-dd HH:mm}");
            actualSessionId = activeSession.Id;
        }

        var queryEmbedding = await _embeddingService.EmbedAsync(request.Query, ct);

        if (queryEmbedding == null || queryEmbedding.Length == 0)
            throw new InvalidOperationException("Failed to generate embedding for query.");

        var searchChunks = await _vectorStore.SearchAsync(
            queryEmbedding,
            request.TenantId,
            effectiveDepartmentId,
            ownerFilter,
            accessScope.AllowedChunkTypes,
            20,
            ct);

        EnsureChunksWithinScope(searchChunks, request, accessScope, effectiveDepartmentId, ownerFilter);

        var rerankedResults = await _rerankService.RerankAsync(request.Query, searchChunks, 5, ct);
        var topChunks = rerankedResults.Select(r => r.Chunk).ToList();
        EnsureChunksWithinScope(topChunks, request, accessScope, effectiveDepartmentId, ownerFilter);

        LogRetrievalAudit(
            actualSessionId,
            request,
            accessScope,
            effectiveDepartmentId,
            ownerFilter,
            searchChunks,
            topChunks);

        string assistantResponse;
        if (topChunks.Count == 0)
        {
            _logger.LogWarning(
                "Chat retrieval returned no authorized context for session {SessionId} and membership {MembershipId}.",
                actualSessionId,
                request.MembershipId);
            assistantResponse = AuthorizedNoContextMessage;
        }
        else
        {
            var history = await _chatRepository.GetMessagesBySessionAsync(actualSessionId, ct);
            var prompt = _promptBuilder.BuildFullPrompt(request.Query, topChunks, accessScope, history);
            assistantResponse = await CallOpenRouterAsync(prompt, ct);
        }

        if (!request.SessionId.HasValue)
            await _chatRepository.AddSessionAsync(activeSession, ct);

        var userMessage = ChatMessage.Create(actualSessionId, request.MembershipId, ChatMessageRole.User, request.Query);
        var assistantMessage = ChatMessage.Create(actualSessionId, request.MembershipId, ChatMessageRole.Assistant, assistantResponse);

        await _chatRepository.AddMessageAsync(userMessage, ct);
        await _chatRepository.AddMessageAsync(assistantMessage, ct);

        activeSession.UpdateTitle(TruncateForTitle(request.Query));
        await _unitOfWork.SaveChangesAsync(ct);

        var periodStart = new DateOnly(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var periodEnd = periodStart.AddMonths(1).AddDays(-1);
        await _tenantUsageService.RecordChatbotUsageAsync(request.TenantId, 2, periodStart, periodEnd, ct);

        return new ChatResponse(
            assistantResponse,
            actualSessionId,
            assistantMessage.Id,
            topChunks.Count,
            EstimateTokenCount(assistantResponse));
    }

    private void EnsureChunksWithinScope(
        IReadOnlyList<DocumentChunk> chunks,
        ChatRequest request,
        ChatAccessScope scope,
        Guid? effectiveDepartmentId,
        Guid? ownerFilter)
    {
        var invalidChunk = chunks.FirstOrDefault(chunk =>
            !IsChunkWithinScope(chunk, request.TenantId, scope, effectiveDepartmentId, ownerFilter));

        if (invalidChunk is null)
            return;

        _logger.LogError(
            "Chat retrieval returned out-of-scope chunk metadata. ChunkId: {ChunkId}, ChunkTenantId: {ChunkTenantId}, ChunkOwnerMembershipId: {ChunkOwnerMembershipId}, ChunkDepartmentId: {ChunkDepartmentId}, ChunkType: {ChunkType}, MembershipId: {MembershipId}, ExpectedTenantId: {ExpectedTenantId}, EffectiveDepartmentId: {EffectiveDepartmentId}, OwnerFilter: {OwnerFilter}",
            invalidChunk.Id,
            invalidChunk.IdTenant,
            invalidChunk.OwnerMembershipId,
            invalidChunk.DepartmentId,
            invalidChunk.Type,
            request.MembershipId,
            scope.TenantId,
            effectiveDepartmentId,
            ownerFilter);

        throw new InvalidOperationException(OutOfScopeChunkMessage);
    }

    private static void EnsureTenantAccess(ChatRequest request, ChatAccessScope scope)
    {
        if (request.MembershipId == Guid.Empty)
            throw new InvalidOperationException("Chat access denied: membership is required.");

        if (request.TenantId == Guid.Empty)
            throw new InvalidOperationException("Chat access denied: tenant is required.");

        if (scope.TenantId != request.TenantId)
            throw new InvalidOperationException("Chat access denied: tenant scope mismatch.");
    }

    private static Guid? ResolveDepartmentFilter(Guid? requestedDepartmentId, ChatAccessScope scope)
    {
        if (scope.CanAccessAllTenantData)
        {
            if (!requestedDepartmentId.HasValue)
                return null;

            if (scope.PermittedDepartmentIds.Count == 0 || scope.PermittedDepartmentIds.Contains(requestedDepartmentId.Value))
                return requestedDepartmentId;

            throw new InvalidOperationException(ForbiddenScopeMessage);
        }

        if (!scope.DepartmentId.HasValue)
        {
            if (scope.ApprovalAccess != ApprovalAccessLevel.OwnOnly)
                throw new InvalidOperationException(MissingDepartmentBoundaryMessage);

            if (requestedDepartmentId.HasValue)
                throw new InvalidOperationException(ForbiddenScopeMessage);

            return null;
        }

        if (!requestedDepartmentId.HasValue || requestedDepartmentId == scope.DepartmentId)
            return scope.DepartmentId;

        throw new InvalidOperationException(ForbiddenScopeMessage);
    }

    private static Guid? ResolveOwnerFilter(ChatAccessScope scope)
    {
        if (scope.CanAccessAllTenantData)
            return null;

        return scope.ApprovalAccess == ApprovalAccessLevel.OwnOnly
            ? scope.OwnerMembershipId
            : null;
    }

    private static bool IsChunkWithinScope(
        DocumentChunk chunk,
        Guid tenantId,
        ChatAccessScope scope,
        Guid? effectiveDepartmentId,
        Guid? ownerFilter)
    {
        if (chunk.IdTenant != tenantId || chunk.IdTenant != scope.TenantId)
            return false;

        if (!scope.AllowedChunkTypes.Contains(chunk.Type))
            return false;

        if (ownerFilter.HasValue && chunk.OwnerMembershipId != ownerFilter.Value)
            return false;

        if (effectiveDepartmentId.HasValue)
            return chunk.DepartmentId == effectiveDepartmentId.Value;

        if (scope.PermittedDepartmentIds.Count > 0)
            return chunk.DepartmentId.HasValue && scope.PermittedDepartmentIds.Contains(chunk.DepartmentId.Value);

        return true;
    }

    private void LogRetrievalAudit(
        Guid sessionId,
        ChatRequest request,
        ChatAccessScope accessScope,
        Guid? effectiveDepartmentId,
        Guid? ownerFilter,
        IReadOnlyList<DocumentChunk> retrievedChunks,
        IReadOnlyList<DocumentChunk> rerankedChunks)
    {
        var allowedChunkTypes = accessScope.AllowedChunkTypes
            .OrderBy(static type => type.ToString(), StringComparer.Ordinal)
            .Select(static type => type.ToString())
            .ToArray();

        var topChunkIds = rerankedChunks
            .Select(static chunk => chunk.Id)
            .Distinct()
            .Take(5)
            .ToArray();

        _logger.LogInformation(
            "Chat retrieval audit {@RetrievalAudit}",
            new
            {
                SessionId = sessionId,
                TenantId = accessScope.TenantId,
                MembershipId = request.MembershipId,
                Role = accessScope.Role.ToString(),
                RequestedDepartmentId = request.DepartmentId,
                EffectiveDepartmentId = effectiveDepartmentId,
                OwnerFilter = ownerFilter,
                AllowedChunkTypes = allowedChunkTypes,
                RetrievedChunkCount = retrievedChunks.Count,
                TopChunkIds = topChunkIds
            });
    }

    public async Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(Guid sessionId, Guid membershipId, CancellationToken ct = default)
    {
        var session = await _chatRepository.GetSessionByIdAndMembershipAsync(sessionId, membershipId, ct)
            ?? throw new InvalidOperationException("Chat access denied: session was not found for the current membership.");

        return await _chatRepository.GetMessagesBySessionAsync(sessionId, ct);
    }

    public async Task<IReadOnlyList<FinFlow.Domain.Interfaces.ChatSessionSummary>> GetSessionsAsync(Guid membershipId, int limit = 20, CancellationToken ct = default)
    {
        return await _chatRepository.GetSessionsAsync(membershipId, limit, ct);
    }

    private async Task<string> CallOpenRouterAsync(Prompt prompt, CancellationToken ct)
    {
        var messages = new List<object>();

        if (!string.IsNullOrWhiteSpace(prompt.System))
        {
            messages.Add(new { role = "system", content = prompt.System });
        }

        foreach (var msg in prompt.History)
        {
            var role = msg.Role == ChatMessageRole.User ? "user" : "assistant";
            messages.Add(new { role, content = msg.Content });
        }

        messages.Add(new { role = "user", content = prompt.User });

        var requestBody = new
        {
            model = _options.ChatModel,
            messages
        };

        var content = new StringContent(JsonSerializer.Serialize(requestBody, SerializerOptions), Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(_chatCompletionsUri, content, ct);
            var responseText = await response.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("OpenRouter response status: {Status}, body: {Body}", response.StatusCode, responseText);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"OpenRouter returned {response.StatusCode}: {responseText}");

            var json = JsonSerializer.Deserialize<OpenRouterChatResponse>(responseText, SerializerOptions);

            if (json?.Choices == null || json.Choices.Count == 0)
                throw new InvalidOperationException("No response from OpenRouter");

            return json.Choices[0].Message.Content;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to get response from OpenRouter");
            throw;
        }
    }

    private static string TruncateForTitle(string query) =>
        query.Length <= 50 ? query : query[..47] + "...";

    private static int EstimateTokenCount(string text) =>
        (int)Math.Ceiling(text.Length / 4.0);

    private static Uri BuildChatCompletionsUri(string baseUrl)
    {
        var normalizedBaseUrl = string.IsNullOrWhiteSpace(baseUrl)
            ? throw new InvalidOperationException("Chat base URL is not configured.")
            : baseUrl.TrimEnd('/') + "/";

        return new Uri(new Uri(normalizedBaseUrl, UriKind.Absolute), "chat/completions");
    }

    private record OpenRouterChatResponse(
        List<OpenRouterChoice> Choices
    );

    private record OpenRouterChoice(
        OpenRouterMessage Message
    );

    private record OpenRouterMessage(
        string Content
    );
}
