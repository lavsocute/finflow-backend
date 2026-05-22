using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Common.Abstractions;
using FinFlow.Application.Subscriptions;
using FinFlow.Domain.Abstractions;
using FinFlow.Domain.Audit;
using FinFlow.Domain.Chat;
using FinFlow.Domain.Documents;
using FinFlow.Domain.Entities;
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
    private const string ResponsePresentationVersion = PromptBuilder.PromptVersion + "|" + ChatRagBusinessFormatter.FormatVersion;
    private const string AuthorizedNoContextMessage = "Tôi chưa tìm thấy đủ thông tin phù hợp trong các tài liệu bạn được phép truy cập để trả lời câu hỏi này.";
    private const string GreetingMessage = "Xin chào! Tôi là FinFlow. Tôi có thể hỗ trợ bạn về chi phí, ngân sách, chứng từ, báo cáo và phân tích chi tiêu. Bạn có thể hỏi như: \"Tháng này tôi đã tiêu bao nhiêu?\", \"Cho tôi xem chứng từ gần đây\", hoặc \"Phòng ban tôi đã chi bao nhiêu?\"";
    private const string LowSignalClarificationMessage = "Bạn có thể hỏi cụ thể hơn không? Ví dụ: \"Tháng này tôi đã tiêu bao nhiêu?\", \"Cho tôi xem chứng từ gần đây\", hoặc \"Viết lại câu này cho lịch sự hơn\".";
    private const string ScopeClarificationMessage = "Bạn muốn xem trong phạm vi nào: của bạn, phòng ban của bạn, hay toàn công ty?";
    private const string ScopeDeniedMessage = "Tôi không thể hỗ trợ yêu cầu này vì quyền hiện tại chỉ cho phép xem dữ liệu trong phạm vi được phép của bạn.";
    private const string ForbiddenScopeMessage = "Chat access denied: the requested chat scope is outside your scope and allowed department or ownership boundary.";
    private const string MissingDepartmentBoundaryMessage = "Chat access denied: your membership is missing a required department boundary for this chat scope.";
    private const string OutOfScopeChunkMessage = "Chat retrieval returned out-of-scope document chunks.";
    private const int MaxQueryLength = 4000;
    private const int MaxHistoryMessages = 20;
    private const int RateLimitSeconds = 2;
    private const int TenantRateLimitWindowSeconds = 60;
    private const int TenantRateLimitMaxRequests = 60;

    private readonly IChatRepository _chatRepository;
    private readonly IChatAuthorizationService _chatAuthorizationService;
    private readonly ISubscriptionQuotaGate _subscriptionQuotaGate;
    private readonly IEmbeddingService _embeddingService;
    private readonly IVectorStore _vectorStore;
    private readonly IRerankService _rerankService;
    private readonly IPromptBuilder _promptBuilder;
    private readonly IChatIntentRouter _intentRouter;
    private readonly IChatPolicyEngine _chatPolicyEngine;
    private readonly IChatReportingService _chatReportingService;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheService _cacheService;
    private readonly IAuditLogRepository? _auditLogRepository;
    private readonly IChatOutputFilter? _outputFilter;
    private readonly IContentModerator? _contentModerator;
    private readonly IQueryRewriter? _queryRewriter;
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
        ISubscriptionQuotaGate subscriptionQuotaGate,
        IEmbeddingService embeddingService,
        IVectorStore vectorStore,
        IRerankService rerankService,
        IPromptBuilder promptBuilder,
        IChatIntentRouter intentRouter,
        IChatReportingService chatReportingService,
        ICurrentTenant currentTenant,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        HttpClient httpClient,
        IOptions<GroqChatOptions> options,
        ILogger<ChatService> logger,
        IAuditLogRepository? auditLogRepository = null,
        IChatOutputFilter? outputFilter = null,
        IContentModerator? contentModerator = null,
        IQueryRewriter? queryRewriter = null,
        IChatPolicyEngine? chatPolicyEngine = null)
    {
        _chatRepository = chatRepository;
        _chatAuthorizationService = chatAuthorizationService;
        _subscriptionQuotaGate = subscriptionQuotaGate;
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _rerankService = rerankService;
        _promptBuilder = promptBuilder;
        _intentRouter = intentRouter;
        _chatPolicyEngine = chatPolicyEngine ?? new ChatPolicyEngine();
        _chatReportingService = chatReportingService;
        _currentTenant = currentTenant;
        _unitOfWork = unitOfWork;
        _cacheService = cacheService;
        _auditLogRepository = auditLogRepository;
        _outputFilter = outputFilter;
        _contentModerator = contentModerator;
        _queryRewriter = queryRewriter;
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
        _chatCompletionsUri = BuildChatCompletionsUri(_options.BaseUrl);
    }

    public async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken ct = default)
    {
        var prepared = await PrepareExecutionAsync(request, streamed: false, ct);
        var policyMessage = ResolvePolicyMessage(prepared.PolicyDecision);

        if (policyMessage is not null)
        {
            var session = await ResolveSessionAsync(prepared.Request, ct);
            var policyAssistantMessage = await PersistConversationAsync(prepared.Request, session, policyMessage, ct);
            await RecordUsageAsync(prepared.Request, prepared.QuotaDecision, null, ct);

            return new ChatResponse(
                policyMessage,
                session.SessionId,
                policyAssistantMessage.Id,
                0,
                0,
                ChatAnswerSource.Reporting,
                null);
        }

        if (prepared.Intent.Mode == ChatExecutionMode.Greeting)
        {
            var session = await ResolveSessionAsync(prepared.Request, ct);
            var greetingAssistantMessage = await PersistConversationAsync(prepared.Request, session, GreetingMessage, ct);
            await RecordUsageAsync(prepared.Request, prepared.QuotaDecision, null, ct);

            return new ChatResponse(
                GreetingMessage,
                session.SessionId,
                greetingAssistantMessage.Id,
                0,
                0,
                ChatAnswerSource.General,
                []);
        }

        if (prepared.Intent.Mode == ChatExecutionMode.General)
        {
            var session = await ResolveSessionAsync(prepared.Request, ct);
            var general = await ExecuteGeneralAsync(prepared.Request.Query, prepared.Intent, session.SessionId, ct);
            var generalAssistantMessage = await PersistConversationAsync(prepared.Request, session, general.Answer, ct);
            await RecordUsageAsync(prepared.Request, prepared.QuotaDecision, general.TokenUsage, ct);

            return new ChatResponse(
                general.Answer,
                session.SessionId,
                generalAssistantMessage.Id,
                0,
                general.TokenUsage ?? 0,
                ChatAnswerSource.General,
                null);
        }

        if (prepared.Intent.Mode == ChatExecutionMode.Reporting)
        {
            var session = await ResolveSessionAsync(prepared.Request, ct);
            var reporting = await ExecuteReportingAsync(prepared.AuthorizationProfile, prepared.Request.Query, prepared.Intent, ct);
            var reportingAssistantMessage = await PersistConversationAsync(prepared.Request, session, reporting.Answer, ct);
            await RecordUsageAsync(prepared.Request, prepared.QuotaDecision, null, ct);

            return new ChatResponse(
                reporting.Answer,
                session.SessionId,
                reportingAssistantMessage.Id,
                reporting.RecordCount,
                0,
                ChatAnswerSource.Reporting,
                null);
        }

        var ragContext = await PrepareRagExecutionAsync(prepared.Request, ct);

        if (ChatResponseCacheKey.IsCacheable(prepared.Request.Query))
        {
                var cacheKey = ChatResponseCacheKey.Build(
                    prepared.Request.TenantId,
                    prepared.Request.MembershipId,
                    ragContext.AccessScope.Role.ToString(),
                    ragContext.EffectiveDepartmentId,
                    ragContext.OwnerFilter,
                    ragContext.AccessScope.AllowedChunkTypes,
                    prepared.Request.Query,
                    ResponsePresentationVersion);

            ChatResponseCacheEntry? cachedResponse = null;
            try
            {
                cachedResponse = await _cacheService.GetAsync<ChatResponseCacheEntry>(cacheKey, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Chat response cache read failed for key {CacheKey}; treating as miss.", cacheKey);
            }

            if (cachedResponse is not null)
            {
                _logger.LogInformation(
                    "Chat response cache HIT for tenant {TenantId} membership {MembershipId}",
                    prepared.Request.TenantId,
                    prepared.Request.MembershipId);

                var session = await ResolveSessionAsync(prepared.Request, ct);
                var cachedAssistantMessage = await PersistConversationAsync(
                    prepared.Request,
                    session,
                    ChatCitationParser.StripMarkers(cachedResponse.Answer),
                    ct);

                var cachedCitations = cachedResponse.Citations
                    .Select(c => new ChatCitation(c.ChunkNumber, c.ChunkId, c.DocumentId, c.ChunkType, c.Preview))
                    .ToList();

                await RecordUsageAsync(prepared.Request, prepared.QuotaDecision, cachedResponse.TokenUsage, ct);

                return new ChatResponse(
                    ChatCitationParser.StripMarkers(cachedResponse.Answer),
                    session.SessionId,
                    cachedAssistantMessage.Id,
                    cachedResponse.DocumentCount,
                    cachedResponse.TokenUsage,
                    ChatAnswerSource.Rag,
                    cachedCitations);
            }
        }

        string assistantResponse;
        string displayAssistantResponse;
        IReadOnlyList<ChatCitation> citations;
        int? totalTokens = null;
        string? promptVersion = null;
        var responseDocumentCount = ragContext.TopChunks.Count;
        if (ragContext.TopChunks.Count == 0)
        {
            _logger.LogWarning(
                "Chat retrieval returned no authorized context for session {SessionId} and membership {MembershipId}.",
                ragContext.Session.SessionId,
                prepared.Request.MembershipId);
            assistantResponse = AuthorizedNoContextMessage;
            displayAssistantResponse = assistantResponse;
            citations = [];
        }
        else
        {
            var formatted = ChatRagBusinessFormatter.TryFormat(prepared.Request.Query, ragContext.TopChunks);
            if (formatted is not null)
            {
                promptVersion = ChatRagBusinessFormatter.FormatVersion;
                responseDocumentCount = formatted.DocumentCount;
                citations = formatted.Citations;
                assistantResponse = formatted.Answer;

                if (_outputFilter is not null)
                {
                    var filtered = _outputFilter.Sanitize(assistantResponse);
                    if (filtered.RedactionCount > 0)
                    {
                        _logger.LogWarning(
                            "Chat output filter applied {RedactionCount} redactions for session {SessionId}, types: {RedactionTypes}",
                            filtered.RedactionCount,
                            ragContext.Session.SessionId,
                            string.Join(",", filtered.RedactionTypes));
                    }

                    assistantResponse = filtered.SanitizedResponse;
                }

                displayAssistantResponse = assistantResponse;
            }
            else
            {
                var prompt = await BuildRagPromptAsync(prepared.Request.Query, ragContext, ct);
                promptVersion = prompt.Version;
                (assistantResponse, totalTokens) = await CallOpenRouterAsync(prompt, ct);

                if (_outputFilter is not null)
                {
                    var filtered = _outputFilter.Sanitize(assistantResponse);
                    if (filtered.RedactionCount > 0)
                    {
                        _logger.LogWarning(
                            "Chat output filter applied {RedactionCount} redactions for session {SessionId}, types: {RedactionTypes}",
                            filtered.RedactionCount,
                            ragContext.Session.SessionId,
                            string.Join(",", filtered.RedactionTypes));
                    }

                    assistantResponse = filtered.SanitizedResponse;
                }

                displayAssistantResponse = ChatCitationParser.StripMarkers(assistantResponse);
                citations = ChatCitationParser.Parse(assistantResponse, ragContext.TopChunks);
            }
        }

        if (ragContext.TopChunks.Count == 0)
            displayAssistantResponse = assistantResponse;

        var assistantMessage = await PersistConversationAsync(
            prepared.Request,
            ragContext.Session,
            displayAssistantResponse,
            ct);

        if (_auditLogRepository is not null)
        {
            var queryHash = ComputeSha256Hash(prepared.Request.Query);
            var topChunkIds = string.Join(",", ragContext.TopChunks.Select(c => c.Id));
            var auditMetadata = JsonSerializer.Serialize(new
            {
                sessionId = ragContext.Session.SessionId,
                role = ragContext.AccessScope.Role.ToString(),
                queryHash,
                queryLength = prepared.Request.Query.Length,
                retrievedChunkCount = ragContext.SearchChunks.Count,
                topChunkCount = responseDocumentCount,
                topChunkIds,
                tokensUsed = totalTokens,
                promptVersion,
                deterministicFormatter = promptVersion == ChatRagBusinessFormatter.FormatVersion,
                citationCount = citations.Count,
                effectiveDepartmentId = ragContext.EffectiveDepartmentId,
                ownerFilter = ragContext.OwnerFilter
            }, SerializerOptions);

            var auditLog = AuditLog.Create(
                action: "chat.query",
                entityType: nameof(ChatSession),
                entityId: ragContext.Session.SessionId.ToString(),
                newValue: auditMetadata,
                idTenant: prepared.Request.TenantId);
            await _auditLogRepository.AddAsync(auditLog, ct);
        }

        await RecordUsageAsync(prepared.Request, prepared.QuotaDecision, totalTokens, ct);

        if (ChatResponseCacheKey.IsCacheable(prepared.Request.Query) && ragContext.TopChunks.Count > 0)
        {
            var cacheKey = ChatResponseCacheKey.Build(
                prepared.Request.TenantId,
                prepared.Request.MembershipId,
                ragContext.AccessScope.Role.ToString(),
                ragContext.EffectiveDepartmentId,
                ragContext.OwnerFilter,
                ragContext.AccessScope.AllowedChunkTypes,
                prepared.Request.Query,
                ResponsePresentationVersion);

            var cacheEntry = new ChatResponseCacheEntry(
                Answer: displayAssistantResponse,
                DocumentCount: responseDocumentCount,
                TokenUsage: totalTokens ?? 0,
                Citations: citations.Select(c => new CachedCitation(c.ChunkNumber, c.ChunkId, c.DocumentId, c.ChunkType, c.Preview)).ToList());

            await _cacheService.SetAsync(cacheKey, cacheEntry, TimeSpan.FromMinutes(5), ct);
        }

        return new ChatResponse(
            displayAssistantResponse,
            ragContext.Session.SessionId,
            assistantMessage.Id,
            responseDocumentCount,
            totalTokens ?? 0,
            ChatAnswerSource.Rag,
            citations);
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

    /// <summary>
    /// Creates a sanitized copy of a document chunk with injection-hardened content.
    /// </summary>
    private static DocumentChunk SanitizeChunk(DocumentChunk chunk)
    {
        var sanitizedContent = ChatPromptSanitizer.Sanitize(chunk.Content);
        return DocumentChunk.WithSanitizedContent(chunk, sanitizedContent);
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

    private static void EnsureTenantAccess(ChatRequest request, ChatAuthorizationProfile profile)
    {
        if (request.MembershipId == Guid.Empty)
            throw new InvalidOperationException("Chat access denied: membership is required.");

        if (request.TenantId == Guid.Empty)
            throw new InvalidOperationException("Chat access denied: tenant is required.");

        if (profile.TenantId != request.TenantId)
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

    public async IAsyncEnumerable<ChatStreamEvent> ChatStreamAsync(
        ChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var prepared = await PrepareExecutionAsync(request, streamed: true, ct);
        var session = await ResolveSessionAsync(prepared.Request, ct);
        var policyMessage = ResolvePolicyMessage(prepared.PolicyDecision);

        if (policyMessage is not null)
        {
            yield return new ChatStreamEvent(ChatStreamEventKind.Token, TokenChunk: policyMessage);

            var policyAssistantMessage = await PersistConversationAsync(
                prepared.Request,
                session,
                policyMessage,
                ct);
            await RecordUsageAsync(prepared.Request, prepared.QuotaDecision, null, ct);

            yield return new ChatStreamEvent(
                Kind: ChatStreamEventKind.Complete,
                SessionId: session.SessionId,
                MessageId: policyAssistantMessage.Id,
                DocumentCount: 0,
                TokenUsage: 0,
                CompleteAnswer: policyMessage,
                AnswerSource: ChatAnswerSource.Reporting);
            yield break;
        }

        if (prepared.Intent.Mode == ChatExecutionMode.Greeting)
        {
            yield return new ChatStreamEvent(ChatStreamEventKind.Token, TokenChunk: GreetingMessage);

            var greetingAssistantMessage = await PersistConversationAsync(
                prepared.Request,
                session,
                GreetingMessage,
                ct);
            await RecordUsageAsync(prepared.Request, prepared.QuotaDecision, null, ct);

            yield return new ChatStreamEvent(
                Kind: ChatStreamEventKind.Complete,
                SessionId: session.SessionId,
                MessageId: greetingAssistantMessage.Id,
                DocumentCount: 0,
                TokenUsage: 0,
                CompleteAnswer: GreetingMessage,
                AnswerSource: ChatAnswerSource.General);
            yield break;
        }

        if (prepared.Intent.Mode == ChatExecutionMode.General)
        {
            var general = await ExecuteGeneralAsync(prepared.Request.Query, prepared.Intent, session.SessionId, ct);
            yield return new ChatStreamEvent(ChatStreamEventKind.Token, TokenChunk: general.Answer);

            var generalAssistantMessage = await PersistConversationAsync(
                prepared.Request,
                session,
                general.Answer,
                ct);
            await RecordUsageAsync(prepared.Request, prepared.QuotaDecision, general.TokenUsage, ct);

            yield return new ChatStreamEvent(
                Kind: ChatStreamEventKind.Complete,
                SessionId: session.SessionId,
                MessageId: generalAssistantMessage.Id,
                DocumentCount: 0,
                TokenUsage: general.TokenUsage ?? 0,
                CompleteAnswer: general.Answer,
                AnswerSource: ChatAnswerSource.General);
            yield break;
        }

        if (prepared.Intent.Mode == ChatExecutionMode.Reporting)
        {
            var reporting = await ExecuteReportingAsync(prepared.AuthorizationProfile, prepared.Request.Query, prepared.Intent, ct);

            yield return new ChatStreamEvent(ChatStreamEventKind.Token, TokenChunk: reporting.Answer);

            var reportingAssistantMessage = await PersistConversationAsync(
                prepared.Request,
                session,
                reporting.Answer,
                ct);
            await RecordUsageAsync(prepared.Request, prepared.QuotaDecision, null, ct);

            yield return new ChatStreamEvent(
                Kind: ChatStreamEventKind.Complete,
                SessionId: session.SessionId,
                MessageId: reportingAssistantMessage.Id,
                DocumentCount: reporting.RecordCount,
                TokenUsage: 0,
                CompleteAnswer: reporting.Answer,
                AnswerSource: ChatAnswerSource.Reporting);
            yield break;
        }

        var ragContext = await PrepareRagExecutionAsync(prepared.Request, ct, session);
        var formatted = ragContext.TopChunks.Count > 0
            ? ChatRagBusinessFormatter.TryFormat(prepared.Request.Query, ragContext.TopChunks)
            : null;
        var fullResponseBuilder = new StringBuilder();
        var streamFilter = _outputFilter is not null ? new StreamingOutputFilter(_outputFilter) : null;
        int? totalTokens = null;
        string? promptVersion = formatted is null ? null : ChatRagBusinessFormatter.FormatVersion;
        var responseDocumentCount = formatted?.DocumentCount ?? ragContext.TopChunks.Count;

        if (ragContext.TopChunks.Count == 0)
        {
            yield return new ChatStreamEvent(
                ChatStreamEventKind.Token, TokenChunk: AuthorizedNoContextMessage);
            fullResponseBuilder.Append(AuthorizedNoContextMessage);
        }
        else if (formatted is not null)
        {
            var formattedAnswer = formatted.Answer;
            if (_outputFilter is not null)
            {
                var filtered = _outputFilter.Sanitize(formattedAnswer);
                if (filtered.RedactionCount > 0)
                {
                    _logger.LogWarning(
                        "Chat output filter applied {RedactionCount} redactions for session {SessionId}, types: {RedactionTypes}",
                        filtered.RedactionCount,
                        session.SessionId,
                        string.Join(",", filtered.RedactionTypes));
                }

                formattedAnswer = filtered.SanitizedResponse;
            }

            yield return new ChatStreamEvent(ChatStreamEventKind.Token, TokenChunk: formattedAnswer);
            fullResponseBuilder.Append(formattedAnswer);
        }
        else
        {
            var prompt = await BuildRagPromptAsync(prepared.Request.Query, ragContext, ct);
            promptVersion = prompt.Version;

            await foreach (var chunk in CallOpenRouterStreamAsync(prompt, ct))
            {
                if (chunk.TokenChunk is { Length: > 0 })
                {
                    fullResponseBuilder.Append(chunk.TokenChunk);

                    // Buffer token through output filter; emit only the redacted prefix
                    // so partial PII patterns at the buffer tail are not yet leaked.
                    var emit = streamFilter is not null
                        ? streamFilter.Append(chunk.TokenChunk)
                        : chunk.TokenChunk;
                    if (!string.IsNullOrEmpty(emit))
                        yield return new ChatStreamEvent(ChatStreamEventKind.Token, TokenChunk: emit);
                }
                if (chunk.TokenUsage.HasValue)
                {
                    totalTokens = chunk.TokenUsage.Value;
                }
            }

            // Flush any remaining buffered text through the filter at end of stream.
            if (streamFilter is not null)
            {
                var tail = streamFilter.Flush();
                if (!string.IsNullOrEmpty(tail))
                    yield return new ChatStreamEvent(ChatStreamEventKind.Token, TokenChunk: tail);

                if (streamFilter.TotalRedactionCount > 0)
                {
                    _logger.LogWarning(
                        "Chat stream output filter applied {RedactionCount} redactions for session {SessionId}, types: {RedactionTypes}",
                        streamFilter.TotalRedactionCount, session.SessionId, string.Join(",", streamFilter.RedactionTypes));
                }
            }
        }

        var assistantResponse = fullResponseBuilder.ToString();
        if (_outputFilter is not null && assistantResponse.Length > 0)
        {
            var filteredFinal = _outputFilter.Sanitize(assistantResponse);
            assistantResponse = filteredFinal.SanitizedResponse;
        }

        var assistantMessage = await PersistConversationAsync(
            prepared.Request,
            session,
            assistantResponse,
            ct);

        if (_auditLogRepository is not null)
        {
            var queryHash = ComputeSha256Hash(prepared.Request.Query);
            var auditMetadata = JsonSerializer.Serialize(new
            {
                sessionId = session.SessionId, role = ragContext.AccessScope.Role.ToString(),
                queryHash, queryLength = prepared.Request.Query.Length,
                retrievedChunkCount = ragContext.SearchChunks.Count,
                topChunkCount = responseDocumentCount,
                tokensUsed = totalTokens,
                promptVersion,
                deterministicFormatter = promptVersion == ChatRagBusinessFormatter.FormatVersion,
                streamed = true
            }, SerializerOptions);
            var auditLog = AuditLog.Create("chat.query.stream", nameof(ChatSession),
                session.SessionId.ToString(), newValue: auditMetadata, idTenant: prepared.Request.TenantId);
            await _auditLogRepository.AddAsync(auditLog, ct);
        }

        await RecordUsageAsync(prepared.Request, prepared.QuotaDecision, totalTokens, ct);

        yield return new ChatStreamEvent(
            Kind: ChatStreamEventKind.Complete,
            SessionId: session.SessionId,
            MessageId: assistantMessage.Id,
            DocumentCount: responseDocumentCount,
            TokenUsage: totalTokens ?? 0,
            CompleteAnswer: assistantResponse,
            AnswerSource: ChatAnswerSource.Rag);
    }

    private async IAsyncEnumerable<ChatStreamEvent> CallOpenRouterStreamAsync(
        Prompt prompt,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(prompt.System))
            messages.Add(new { role = "system", content = prompt.System });
        foreach (var msg in prompt.History)
        {
            var role = msg.Role == ChatMessageRole.User ? "user" : "assistant";
            messages.Add(new { role, content = msg.Content });
        }
        messages.Add(new { role = "user", content = prompt.User });

        var requestBody = new
        {
            model = _options.ChatModel,
            messages,
            stream = true,
            stream_options = new { include_usage = true }
        };

        var jsonContent = JsonSerializer.Serialize(requestBody, SerializerOptions);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _chatCompletionsUri);
        httpRequest.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Groq stream returned {response.StatusCode}: {error}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line) || !line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var payload = line.Substring(5).Trim();
            if (payload == "[DONE]") yield break;

            string? tokenText = null;
            int? usageTokens = null;
            try
            {
                using var doc = JsonDocument.Parse(payload);
                var root = doc.RootElement;

                if (root.TryGetProperty("choices", out var choices) &&
                    choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("content", out var contentEl) &&
                    contentEl.ValueKind == JsonValueKind.String)
                {
                    var token = contentEl.GetString();
                    if (!string.IsNullOrEmpty(token))
                        tokenText = token;
                }

                if (root.TryGetProperty("usage", out var usage) &&
                    usage.TryGetProperty("total_tokens", out var totalTokensEl) &&
                    totalTokensEl.ValueKind == JsonValueKind.Number)
                {
                    usageTokens = totalTokensEl.GetInt32();
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse Groq SSE payload: {Payload}", payload);
                continue;
            }

            // Fix #6: yield token AND usage if both present in the same SSE frame.
            if (tokenText is not null)
                yield return new ChatStreamEvent(ChatStreamEventKind.Token, TokenChunk: tokenText);
            if (usageTokens.HasValue)
                yield return new ChatStreamEvent(ChatStreamEventKind.Token, TokenUsage: usageTokens.Value);
        }
    }

    public async Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(Guid sessionId, Guid membershipId, CancellationToken ct = default)
    {
        var session = await _chatRepository.GetSessionByIdAndMembershipAsync(sessionId, membershipId, ct)
            ?? throw new InvalidOperationException("Chat access denied: session was not found for the current membership.");

        // FIX #6: Add tenant check to prevent cross-tenant session access
        if (session.IdTenant != _currentTenant.Id)
            throw new InvalidOperationException("Chat access denied: session was not found for the current membership.");

        return await _chatRepository.GetMessagesBySessionAsync(sessionId, ct);
    }

    public async Task<IReadOnlyList<FinFlow.Domain.Interfaces.ChatSessionSummary>> GetSessionsAsync(Guid membershipId, int limit = 20, CancellationToken ct = default)
    {
        return await _chatRepository.GetSessionsAsync(membershipId, limit, ct);
    }

    private async Task<(string Content, int? TotalTokens)> CallOpenRouterAsync(Prompt prompt, CancellationToken ct)
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

        var jsonContent = JsonSerializer.Serialize(requestBody, SerializerOptions);

        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(_chatCompletionsUri, content, ct);
            var responseText = await response.Content.ReadAsStringAsync(ct);
            _logger.LogInformation("Groq response status: {Status}, responseLength: {ResponseLength}", response.StatusCode, responseText.Length);

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Groq returned {response.StatusCode}: {responseText}");

            var json = JsonSerializer.Deserialize<OpenRouterChatResponse>(responseText, SerializerOptions);

            if (json?.Choices == null || json.Choices.Count == 0)
                throw new InvalidOperationException("No response from Groq");

            // Use provider-reported token count (accurate) instead of length-based heuristic.
            return (json.Choices[0].Message.Content, json.Usage?.TotalTokens);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to get response from Groq");
            throw;
        }
    }

    private static string TruncateForTitle(string query) =>
        query.Length <= 50 ? query : query[..47] + "...";

    private async Task<PreparedChatExecution> PrepareExecutionAsync(
        ChatRequest request,
        bool streamed,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            throw new InvalidOperationException("Query cannot be empty.");

        if (request.Query.Length > MaxQueryLength)
            throw new InvalidOperationException($"Query exceeds maximum length of {MaxQueryLength} characters.");

        var sanitizedQuery = ChatPromptSanitizer.Sanitize(request.Query);
        request = request with { Query = sanitizedQuery };

        if (_contentModerator is not null)
        {
            var moderationReason = _contentModerator.Moderate(sanitizedQuery);
            if (moderationReason is not null)
            {
                var template = streamed
                    ? "Chat content moderation rejected streamed query for membership {MembershipId}, reason: {Reason}"
                    : "Chat content moderation rejected query for membership {MembershipId}, reason: {Reason}";
                _logger.LogWarning(template, request.MembershipId, moderationReason);
                throw new InvalidOperationException("Your message was rejected by our content policy. Please rephrase.");
            }
        }

        var rateLimitKey = $"chat:ratelimit:{request.MembershipId}";
        var perUserCount = await _cacheService.IncrementWithExpiryAsync(rateLimitKey, TimeSpan.FromSeconds(RateLimitSeconds), ct);
        if (perUserCount > 1)
            throw new InvalidOperationException("Rate limit exceeded. Please wait a moment before sending another message.");

        var tenantRateKey = $"chat:ratelimit:tenant:{request.TenantId}";
        var tenantCount = await _cacheService.IncrementWithExpiryAsync(tenantRateKey, TimeSpan.FromSeconds(TenantRateLimitWindowSeconds), ct);
        if (tenantCount > TenantRateLimitMaxRequests)
            throw new InvalidOperationException("Tenant chat rate limit exceeded. Please slow down and try again in a minute.");

        var quotaDecisionResult = await _subscriptionQuotaGate.EnsureChatbotAllowedAsync(
            request.TenantId,
            request.MembershipId,
            1,
            ct);
        if (quotaDecisionResult.IsFailure)
            throw new InvalidOperationException(quotaDecisionResult.Error.Description);

        var tokenQuotaResult = await _subscriptionQuotaGate.EnsureChatbotTokensAvailableAsync(
            request.TenantId,
            request.MembershipId,
            ct);
        if (tokenQuotaResult.IsFailure)
            throw new InvalidOperationException(tokenQuotaResult.Error.Description);

        var authorizationProfile = await _chatAuthorizationService.GetAuthorizationProfileAsync(request.MembershipId, ct);
        EnsureTenantAccess(request, authorizationProfile);
        var intent = _intentRouter.Classify(request.Query);
        var policyDecision = _chatPolicyEngine.Decide(authorizationProfile, intent, request.Query);
        LogPolicyDecision(request, authorizationProfile, intent, policyDecision, streamed);

        return new PreparedChatExecution(request, quotaDecisionResult.Value, authorizationProfile, intent, policyDecision);
    }

    private void LogPolicyDecision(
        ChatRequest request,
        ChatAuthorizationProfile profile,
        ChatIntentClassification intent,
        ChatPolicyDecision policyDecision,
        bool streamed)
    {
        var template = policyDecision.Kind switch
        {
            ChatPolicyDecisionKind.Deny => "Chat policy denied {@PolicyDecisionAudit}",
            ChatPolicyDecisionKind.Clarify => "Chat policy requires clarification {@PolicyDecisionAudit}",
            _ => "Chat policy decision {@PolicyDecisionAudit}"
        };

        var audit = new
        {
            request.MembershipId,
            request.TenantId,
            Role = profile.Role.ToString(),
            IntentFamily = intent.Family.ToString(),
            IntentReason = intent.Reason,
            ScopeConfidence = intent.ScopeConfidence.ToString(),
            Decision = policyDecision.Kind.ToString(),
            Streamed = streamed,
            QueryHash = ComputeSha256Hash(request.Query),
            QueryLength = request.Query.Length
        };

        if (policyDecision.Kind == ChatPolicyDecisionKind.Deny)
        {
            _logger.LogWarning(template, audit);
            LogBoundaryTelemetry(intent, policyDecision, audit);
            return;
        }

        _logger.LogInformation(template, audit);
    }

    private void LogBoundaryTelemetry(
        ChatIntentClassification intent,
        ChatPolicyDecision policyDecision,
        object audit)
    {
        if (policyDecision.Kind != ChatPolicyDecisionKind.Deny)
            return;

        if (intent.Family is ChatIntentFamily.Programming or ChatIntentFamily.SensitiveAdvice)
        {
            _logger.LogWarning("Chat boundary blocked {@BoundaryTelemetry}", new
            {
                TelemetryVersion = 1,
                TelemetryKind = "BoundaryBlocked",
                AlertSeverity = "Medium",
                RecommendedAction = "Review recurring blocked intents and extend deny lexicon only if new bypass wording appears frequently.",
                BoundaryFamily = intent.Family.ToString(),
                intent.Reason,
                Audit = audit
            });
            return;
        }

        _logger.LogWarning("Chat boundary gap candidate {@BoundaryTelemetry}", new
        {
            TelemetryVersion = 1,
            TelemetryKind = "BoundaryGapCandidate",
            AlertSeverity = "High",
            RecommendedAction = "Review this deny event to decide whether a new explicit boundary family or lexicon rule is required.",
            BoundaryFamily = intent.Family.ToString(),
            intent.Reason,
            Audit = audit
        });
    }

    private static string? ResolvePolicyMessage(ChatPolicyDecision policyDecision) =>
        policyDecision.Kind switch
        {
            ChatPolicyDecisionKind.Clarify => policyDecision.Message ?? ScopeClarificationMessage,
            ChatPolicyDecisionKind.Deny => policyDecision.Message ?? ScopeDeniedMessage,
            _ => null
        };

    private async Task<ResolvedChatSession> ResolveSessionAsync(ChatRequest request, CancellationToken ct)
    {
        if (request.SessionId.HasValue)
        {
            var existingSession = await _chatRepository.GetOwnedSessionAsync(request.SessionId.Value, request.TenantId, request.MembershipId, ct)
                ?? throw new InvalidOperationException("Chat access denied: session was not found for the current membership.");

            return new ResolvedChatSession(existingSession.Id, existingSession, IsNewSession: false);
        }

        var newSession = ChatSession.Create(request.TenantId, request.MembershipId, $"Chat {DateTime.UtcNow:yyyy-MM-dd HH:mm}");
        return new ResolvedChatSession(newSession.Id, newSession, IsNewSession: true);
    }

    private async Task<ReportingExecutionResult> ExecuteReportingAsync(
        ChatAuthorizationProfile profile,
        string query,
        ChatIntentClassification intent,
        CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = new DateOnly(today.Year, today.Month, 1);
        var to = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

        if (string.Equals(intent.Reason, "approval-reporting", StringComparison.Ordinal))
        {
            var approvalAnswer = await _chatReportingService.BuildPendingApprovalSummaryAsync(profile, query, ct);
            return new ReportingExecutionResult(approvalAnswer.Answer, approvalAnswer.RecordCount);
        }

        if (string.Equals(intent.Reason, "trend-reporting", StringComparison.Ordinal))
        {
            var trendAnswer = await _chatReportingService.BuildMonthlyTrendSummaryAsync(profile, query, ct);
            return new ReportingExecutionResult(trendAnswer.Answer, trendAnswer.RecordCount);
        }

        if (string.Equals(intent.Reason, "vendor-reporting", StringComparison.Ordinal))
        {
            var vendorAnswer = await _chatReportingService.BuildTopVendorsSummaryAsync(profile, query, from, to, ct);
            return new ReportingExecutionResult(vendorAnswer.Answer, vendorAnswer.RecordCount);
        }

        if (string.Equals(intent.Reason, "budget-reporting", StringComparison.Ordinal))
        {
            var budgetAnswer = await _chatReportingService.BuildBudgetUtilizationSummaryAsync(profile, query, from, to, ct);
            return new ReportingExecutionResult(budgetAnswer.Answer, budgetAnswer.RecordCount);
        }

        if (intent.Family == ChatIntentFamily.Ranking)
        {
            var rankingAnswer = await _chatReportingService.BuildTopEmployeesSummaryAsync(profile, query, from, to, ct);
            return new ReportingExecutionResult(rankingAnswer.Answer, rankingAnswer.RecordCount);
        }

        if (string.Equals(intent.Reason, "comparison-reporting", StringComparison.Ordinal))
        {
            var comparisonAnswer = await _chatReportingService.BuildExpenseComparisonAsync(profile, query, from, to, ct);
            return new ReportingExecutionResult(comparisonAnswer.Answer, comparisonAnswer.RecordCount);
        }

        if (intent.Family == ChatIntentFamily.Aggregate)
        {
            var aggregateAnswer = await _chatReportingService.BuildScopedExpenseSummaryAsync(profile, query, from, to, ct);
            return new ReportingExecutionResult(aggregateAnswer.Answer, aggregateAnswer.RecordCount);
        }

        var reportingAnswer = await _chatReportingService.BuildOwnExpenseSummaryAsync(profile, from, to, ct);
        return new ReportingExecutionResult(reportingAnswer.Answer, reportingAnswer.RecordCount);
    }

    private async Task<RagExecutionContext> PrepareRagExecutionAsync(
        ChatRequest request,
        CancellationToken ct,
        ResolvedChatSession? resolvedSession = null)
    {
        var session = resolvedSession ?? await ResolveSessionAsync(request, ct);
        var accessScope = await _chatAuthorizationService.GetChatAccessScopeAsync(request.MembershipId, ct);
        EnsureTenantAccess(request, accessScope);
        var effectiveDepartmentId = ResolveDepartmentFilter(request.DepartmentId, accessScope);
        var ownerFilter = ResolveOwnerFilter(accessScope);

        var retrievalQuery = request.Query;
        if (_queryRewriter is not null)
        {
            var rewritten = await _queryRewriter.RewriteAsync(request.Query, ct);
            if (!string.IsNullOrWhiteSpace(rewritten) && rewritten != request.Query)
                retrievalQuery = ChatPromptSanitizer.Sanitize(rewritten);
        }

        var queryEmbedding = await _embeddingService.EmbedAsync(retrievalQuery, ct);
        if (queryEmbedding == null || queryEmbedding.Length == 0)
            throw new InvalidOperationException("Failed to generate embedding for query.");

        var vectorChunks = await _vectorStore.SearchAsync(
            queryEmbedding,
            request.TenantId,
            effectiveDepartmentId,
            ownerFilter,
            accessScope.AllowedChunkTypes,
            20,
            ct);

        var keywordChunks = await SafeKeywordSearchAsync(
            retrievalQuery,
            request.TenantId,
            effectiveDepartmentId,
            ownerFilter,
            accessScope.AllowedChunkTypes,
            20,
            ct);

        var searchChunks = ReciprocalRankFusion.Fuse(vectorChunks, keywordChunks, 20);

        // Sanitize document chunks to prevent RAG injection attacks before fusion/reranking
        var sanitizedChunks = searchChunks.Select(c => SanitizeChunk(c)).ToList();
        searchChunks = sanitizedChunks;

        EnsureChunksWithinScope(searchChunks, request, accessScope, effectiveDepartmentId, ownerFilter);

        var rerankedResults = await _rerankService.RerankAsync(request.Query, searchChunks, 5, ct);
        var topChunks = rerankedResults.Select(r => r.Chunk).ToList();
        EnsureChunksWithinScope(topChunks, request, accessScope, effectiveDepartmentId, ownerFilter);

        // Sanitize topChunks as well since they come from the same search results
        var sanitizedTopChunks = topChunks.Select(c => SanitizeChunk(c)).ToList();
        topChunks = sanitizedTopChunks;

        LogRetrievalAudit(
            session.SessionId,
            request,
            accessScope,
            effectiveDepartmentId,
            ownerFilter,
            searchChunks,
            topChunks);

        return new RagExecutionContext(
            session,
            accessScope,
            effectiveDepartmentId,
            ownerFilter,
            searchChunks,
            topChunks);
    }

    private async Task<Prompt> BuildRagPromptAsync(
        string query,
        RagExecutionContext context,
        CancellationToken ct)
    {
        var history = await _chatRepository.GetMessagesBySessionAsync(context.Session.SessionId, ct);
        var limitedHistory = history
            .TakeLast(MaxHistoryMessages)
            .Select(m => ChatMessage.Create(m.SessionId, m.SenderId, m.Role, ChatPromptSanitizer.Sanitize(m.Content)))
            .ToList();

        return _promptBuilder.BuildFullPrompt(query, context.TopChunks, context.AccessScope, limitedHistory);
    }

    private async Task<Prompt> BuildGeneralPromptAsync(
        string query,
        ChatIntentClassification classification,
        Guid sessionId,
        CancellationToken ct)
    {
        var history = await _chatRepository.GetMessagesBySessionAsync(sessionId, ct);
        var limitedHistory = history
            .TakeLast(MaxHistoryMessages)
            .Select(m => ChatMessage.Create(m.SessionId, m.SenderId, m.Role, ChatPromptSanitizer.Sanitize(m.Content)))
            .ToList();

        return _promptBuilder.BuildGeneralPrompt(query, classification, limitedHistory);
    }

    private async Task<GeneralExecutionResult> ExecuteGeneralAsync(
        string query,
        ChatIntentClassification classification,
        Guid sessionId,
        CancellationToken ct)
    {
        if (classification.Family == ChatIntentFamily.LowSignal)
            return new GeneralExecutionResult(LowSignalClarificationMessage, null);

        if (classification.Family == ChatIntentFamily.Greeting)
            return new GeneralExecutionResult(GreetingMessage, null);

        var prompt = await BuildGeneralPromptAsync(query, classification, sessionId, ct);
        var (answer, totalTokens) = await CallOpenRouterAsync(prompt, ct);

        if (_outputFilter is not null)
        {
            var filtered = _outputFilter.Sanitize(answer);
            answer = filtered.SanitizedResponse;
        }

        return new GeneralExecutionResult(answer, totalTokens);
    }

    private async Task<ChatMessage> PersistConversationAsync(
        ChatRequest request,
        ResolvedChatSession session,
        string assistantResponse,
        CancellationToken ct)
    {
        if (session.IsNewSession)
            await _chatRepository.AddSessionAsync(session.Session, ct);

        var userMessage = ChatMessage.Create(session.SessionId, request.MembershipId, ChatMessageRole.User, request.Query);
        var assistantMessage = ChatMessage.Create(session.SessionId, request.MembershipId, ChatMessageRole.Assistant, assistantResponse);

        await _chatRepository.AddMessageAsync(userMessage, ct);
        await _chatRepository.AddMessageAsync(assistantMessage, ct);

        session.Session.UpdateTitle(TruncateForTitle(request.Query));
        await _unitOfWork.SaveChangesAsync(ct);
        return assistantMessage;
    }

    private async Task RecordUsageAsync(
        ChatRequest request,
        SubscriptionQuotaDecision quotaDecision,
        int? totalTokens,
        CancellationToken ct)
    {
        await _subscriptionQuotaGate.RecordChatbotUsageAsync(quotaDecision, ct);
        if (totalTokens.HasValue && totalTokens.Value > 0)
        {
            await _subscriptionQuotaGate.RecordChatbotTokensAsync(
                request.TenantId,
                request.MembershipId,
                totalTokens.Value,
                quotaDecision.PeriodStart,
                quotaDecision.PeriodEnd,
                ct);
        }

        await _unitOfWork.SaveChangesAsync(ct);
    }

    private async Task<IReadOnlyList<DocumentChunk>> SafeKeywordSearchAsync(
        string query,
        Guid tenantId,
        Guid? departmentId,
        Guid? ownerId,
        IReadOnlyCollection<DocumentChunkType>? allowedTypes,
        int topK,
        CancellationToken ct)
    {
        try
        {
            return await _vectorStore.KeywordSearchAsync(query, tenantId, departmentId, ownerId, allowedTypes, topK, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Keyword search failed for tenant {TenantId}; falling back to vector-only retrieval.", tenantId);
            return Array.Empty<DocumentChunk>();
        }
    }

    private static string ComputeSha256Hash(string input)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input ?? string.Empty);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }

    private static Uri BuildChatCompletionsUri(string baseUrl)
    {
        var normalizedBaseUrl = string.IsNullOrWhiteSpace(baseUrl)
            ? throw new InvalidOperationException("Chat base URL is not configured.")
            : baseUrl.TrimEnd('/') + "/";

        return new Uri(new Uri(normalizedBaseUrl, UriKind.Absolute), "chat/completions");
    }

    private record OpenRouterChatResponse(
        List<OpenRouterChoice> Choices,
        OpenRouterUsage? Usage
    );

    private record OpenRouterChoice(
        OpenRouterMessage Message
    );

    private record OpenRouterMessage(
        string Content
    );

    private record OpenRouterUsage(
        int? PromptTokens,
        int? CompletionTokens,
        int? TotalTokens
    );

    private sealed class ChatRateLimitEntry
    {
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    }

    private sealed class TenantRateCounter
    {
        public int Count { get; set; }
    }

    private sealed record PreparedChatExecution(
        ChatRequest Request,
        SubscriptionQuotaDecision QuotaDecision,
        ChatAuthorizationProfile AuthorizationProfile,
        ChatIntentClassification Intent,
        ChatPolicyDecision PolicyDecision);

    private sealed record ResolvedChatSession(
        Guid SessionId,
        ChatSession Session,
        bool IsNewSession);

    private sealed record ReportingExecutionResult(
        string Answer,
        int RecordCount);

    private sealed record GeneralExecutionResult(
        string Answer,
        int? TokenUsage);

    private sealed record RagExecutionContext(
        ResolvedChatSession Session,
        ChatAccessScope AccessScope,
        Guid? EffectiveDepartmentId,
        Guid? OwnerFilter,
        IReadOnlyList<DocumentChunk> SearchChunks,
        IReadOnlyList<DocumentChunk> TopChunks);
}
