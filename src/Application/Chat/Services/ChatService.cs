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
using Microsoft.Extensions.Logging.Abstractions;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace FinFlow.Application.Chat.Services;

public class GroqChatOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1";
    public string ChatModel { get; set; } = "llama-3.3-70b-versatile";
    public string IntentPlannerModel { get; set; } = "openai/gpt-oss-20b";
}

/// <summary>
/// Main chat service that handles user queries via RAG, reporting, and general execution modes.
/// </summary>
public sealed class ChatService : IChatService
{
    private const string ResponsePresentationVersion = PromptBuilder.PromptVersion + "|" + ChatRagBusinessFormatter.FormatVersion;
    private const string AuthorizedNoContextMessage = "Tôi chưa tìm thấy đủ thông tin phù hợp trong các tài liệu bạn được phép truy cập để trả lời câu hỏi này. (not enough authorized context)";
    private const string GreetingMessage = "Xin chào! Tôi là FinFlow. Tôi có thể hỗ trợ bạn về chi phí, ngân sách, chứng từ, báo cáo và phân tích chi tiêu. Bạn có thể hỏi như: \"Tháng này tôi đã tiêu bao nhiêu?\", \"Cho tôi xem chứng từ gần đây\", hoặc \"Phòng ban tôi đã chi bao nhiêu?\"";
    private const string LowSignalClarificationMessage = "Bạn có thể hỏi cụ thể hơn không? Ví dụ: \"Tháng này tôi đã tiêu bao nhiêu?\", \"Cho tôi xem chứng từ gần đây\", hoặc \"Viết lại câu này cho lịch sự hơn\".";
    private const string AmbiguityClarificationMessage = "Bạn có thể nói rõ hơn được không? Ví dụ: \"Những ai đã duyệt chứng từ tháng này?\", \"Cho tôi xem chi phí phòng ban tuần này\", hoặc \"Ai đã duyệt phiếu chi #12345\".";
    private const string ScopeClarificationMessage = "Bạn muốn xem trong phạm vi nào: của bạn, phòng ban của bạn, hay toàn công ty?";
    private const string ScopeDeniedMessage = "Tôi không thể hỗ trợ yêu cầu này vì quyền hiện tại chỉ cho phép xem dữ liệu trong phạm vi được phép của bạn.";
    private const string ForbiddenScopeMessage = "Chat access denied: the requested chat scope is outside your scope and allowed department or ownership boundary.";
    private const string MissingDepartmentBoundaryMessage = "Chat access denied: your membership is missing a required department boundary for this chat scope.";
    private const string OutOfScopeChunkMessage = "Chat retrieval returned out-of-scope document chunks.";
    private const int MaxQueryLength = 4000;
    private const int MaxHistoryMessages = 20;

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
    private readonly IHybridResolutionRouter _hybridResolutionRouter;
    private readonly IContextSummarizationService _contextSummarizationService;
    private readonly IConversationStateManager _conversationStateManager;
    private readonly ILlmEntityExtractor _llmEntityExtractor;
    private readonly IMultiIntentDetector? _multiIntentDetector;
    private readonly HttpClient _httpClient;
    private readonly GroqChatOptions _options;
    private readonly ILogger<ChatService> _logger;
    private readonly Uri _chatCompletionsUri;
    private readonly IRateLimitService _rateLimitService;
    private readonly IChatResponseCacheKeyBuilder _cacheKeyBuilder;
    private readonly IContextualChatPlanner _contextualChatPlanner;
    private readonly IChatIntentPlanner _intentPlanner;

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
        IContextualChatPlanner? contextualChatPlanner = null,
        IChatIntentPlanner? intentPlanner = null,
        IAuditLogRepository? auditLogRepository = null,
        IChatOutputFilter? outputFilter = null,
        IContentModerator? contentModerator = null,
        IQueryRewriter? queryRewriter = null,
        IChatPolicyEngine? chatPolicyEngine = null,
        IHybridResolutionRouter? hybridResolutionRouter = null,
        IContextSummarizationService? contextSummarizationService = null,
        IConversationStateManager? conversationStateManager = null,
        IRateLimitService? rateLimitService = null,
        IChatResponseCacheKeyBuilder? cacheKeyBuilder = null,
        ILoggerFactory? loggerFactory = null,
        ILlmEntityExtractor? llmEntityExtractor = null,
        IMultiIntentDetector? multiIntentDetector = null)
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
        _rateLimitService = rateLimitService ?? new RateLimitService(cacheService, loggerFactory?.CreateLogger<RateLimitService>() ?? NullLogger<RateLimitService>.Instance);
        _cacheKeyBuilder = cacheKeyBuilder ?? new ChatResponseCacheKeyBuilder();
        _contextualChatPlanner = contextualChatPlanner ?? new NoOpContextualChatPlanner();
        _intentPlanner = intentPlanner ?? new RouterBackedChatIntentPlanner(intentRouter);
        _multiIntentDetector = multiIntentDetector;
        loggerFactory ??= NullLoggerFactory.Instance;
        var contextResolverLogger = loggerFactory.CreateLogger<ContextResolver>();
        var summarizationLogger = loggerFactory.CreateLogger<ContextSummarizationService>();
        var stateManagerLogger = loggerFactory.CreateLogger<ConversationStateManager>();
        var textNormalizerLogger = loggerFactory.CreateLogger<TextNormalizer>();
        var textNormalizer = new TextNormalizer();
        _llmEntityExtractor = llmEntityExtractor ?? NullLlmEntityExtractor.Instance;
        _hybridResolutionRouter = hybridResolutionRouter ?? new HybridResolutionRouter(new ContextResolver(new ConfidenceScorer(), _llmEntityExtractor, contextResolverLogger, textNormalizer), new ConfidenceScorer(), _cacheService, textNormalizer);
        _contextSummarizationService = contextSummarizationService ?? new ContextSummarizationService(summarizationLogger, _httpClient, options);
        _conversationStateManager = conversationStateManager ?? new ConversationStateManager(_cacheService, stateManagerLogger);
    }

    public async Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken ct = default)
    {
        var prepared = await PrepareExecutionAsync(request, streamed: false, ct);

        // Handle multi-intent queries by splitting and processing each intent separately
        if (_multiIntentDetector is not null &&
            !prepared.ContextualPlanApplied &&
            ShouldRunMultiIntentDetection(prepared.EffectiveQuery))
        {
            var subQueries = await _multiIntentDetector.DetectAndSplitAsync(prepared.EffectiveQuery, ct);
            if (subQueries.Count > 1)
            {
                return await HandleMultiIntentQueryAsync(prepared, subQueries, ct);
            }
        }

        var policyMessage = ResolvePolicyMessage(prepared.PolicyDecision);

        if (policyMessage is not null)
        {
            var session = await ResolveSessionAsync(prepared.Request, ct);
            var policyAssistantMessage = await PersistConversationAsync(prepared.Request, session, policyMessage, ct);
            await RecordPendingClarificationAsync(session.SessionId, prepared, policyMessage, ct);
            await RecordUsageAsync(prepared.Request, prepared.QuotaDecision, null, ct);

            return new ChatResponse(
                policyMessage,
                session.SessionId,
                policyAssistantMessage.Id,
                0,
                0,
                ChatAnswerSource.General,
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
            var general = await ExecuteGeneralAsync(prepared.EffectiveQuery, prepared.Intent, session.SessionId, ct, prepared.AuthorizationProfile);
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
            var reporting = await ExecuteReportingAsync(
                prepared.AuthorizationProfile,
                prepared.EffectiveQuery,
                prepared.Intent,
                prepared.ReportingFrom,
                prepared.ReportingTo,
                ct);
            var reportingAssistantMessage = await PersistConversationAsync(
                prepared.Request,
                session,
                reporting.Answer,
                ct,
                BuildTurnState(prepared, ChatAnswerSource.Reporting));
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

        if (_cacheKeyBuilder.IsCacheable(prepared.Request.Query))
        {
                var cacheKey = _cacheKeyBuilder.Build(
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
                    ct,
                    BuildTurnState(prepared, ChatAnswerSource.Rag));

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
            var formatted = ChatRagBusinessFormatter.TryFormat(
                prepared.EffectiveQuery,
                ragContext.TopChunks,
                prepared.Intent.ReportingTask);
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
                var prompt = await BuildRagPromptAsync(prepared.EffectiveQuery, ragContext, ct);
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
            ct,
            BuildTurnState(prepared, ChatAnswerSource.Rag));

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

        if (_cacheKeyBuilder.IsCacheable(prepared.Request.Query) && ragContext.TopChunks.Count > 0)
        {
            var cacheKey = _cacheKeyBuilder.Build(
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

    private static bool ShouldRunMultiIntentDetection(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return false;

        var questionMarkCount = query.Count(c => c == '?');
        if (questionMarkCount > 1)
            return true;

        return query.Contains('\n') ||
            query.Contains(';') ||
            query.Contains("；", StringComparison.Ordinal);
    }

    private static void EnsureRequestedDepartmentWithinProfile(ChatRequest request, ChatAuthorizationProfile profile)
    {
        if (!request.DepartmentId.HasValue || profile.CanAccessAllTenantData)
            return;

        if (profile.DepartmentId == request.DepartmentId.Value ||
            profile.AllowedDepartmentIds.Contains(request.DepartmentId.Value))
            return;

        throw new InvalidOperationException(ForbiddenScopeMessage);
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
            await RecordPendingClarificationAsync(session.SessionId, prepared, policyMessage, ct);
            await RecordUsageAsync(prepared.Request, prepared.QuotaDecision, null, ct);

            yield return new ChatStreamEvent(
                Kind: ChatStreamEventKind.Complete,
                SessionId: session.SessionId,
                MessageId: policyAssistantMessage.Id,
                DocumentCount: 0,
                TokenUsage: 0,
                CompleteAnswer: policyMessage,
                AnswerSource: ChatAnswerSource.General);
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
            var general = await ExecuteGeneralAsync(prepared.EffectiveQuery, prepared.Intent, session.SessionId, ct, prepared.AuthorizationProfile);
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
            var reporting = await ExecuteReportingAsync(
                prepared.AuthorizationProfile,
                prepared.EffectiveQuery,
                prepared.Intent,
                prepared.ReportingFrom,
                prepared.ReportingTo,
                ct);

            yield return new ChatStreamEvent(ChatStreamEventKind.Token, TokenChunk: reporting.Answer);

            var reportingAssistantMessage = await PersistConversationAsync(
                prepared.Request,
                session,
                reporting.Answer,
                ct,
                BuildTurnState(prepared, ChatAnswerSource.Reporting));
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
            ? ChatRagBusinessFormatter.TryFormat(
                prepared.EffectiveQuery,
                ragContext.TopChunks,
                prepared.Intent.ReportingTask)
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
            var prompt = await BuildRagPromptAsync(prepared.EffectiveQuery, ragContext, ct);
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
            ct,
            BuildTurnState(prepared, ChatAnswerSource.Rag));

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

        // Streaming path: inline retry for transient 5xx/timeout (best-effort).
        HttpResponseMessage? response = null;
        var transientAttempts = 0;
        while (true)
        {
            // HttpRequestMessage cannot be re-sent, so build a fresh one each attempt.
            using var perAttemptRequest = new HttpRequestMessage(HttpMethod.Post, _chatCompletionsUri)
            {
                Content = new StringContent(jsonContent, Encoding.UTF8, "application/json")
            };
            response = await _httpClient.SendAsync(perAttemptRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.IsSuccessStatusCode) break;

            var status = (int)response.StatusCode;
            var transient = status >= 500 || response.StatusCode == HttpStatusCode.RequestTimeout || response.StatusCode == HttpStatusCode.TooManyRequests;
            if (transient && transientAttempts < 2)
            {
                transientAttempts++;
                _logger.LogWarning("Groq stream returned transient {StatusCode}; retry attempt {Attempt}/2.", response.StatusCode, transientAttempts);
                response.Dispose();
                await Task.Delay(TimeSpan.FromMilliseconds(500 * transientAttempts), ct);
                continue;
            }

            _logger.LogWarning("Groq API returned error status {StatusCode}", response.StatusCode);
            throw new InvalidOperationException("Dịch vụ tạm thời gián đoạn. Vui lòng thử lại sau.");
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

            // Yield both token and usage when both are present in the same SSE frame.
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

        // Add tenant check to prevent cross-tenant session access.
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

        try
        {
            const int maxAttempts = 3;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                using var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                using var response = await _httpClient.PostAsync(_chatCompletionsUri, content, ct);
                var responseText = await response.Content.ReadAsStringAsync(ct);
                _logger.LogInformation("Groq response status: {Status}, responseLength: {ResponseLength}", response.StatusCode, responseText.Length);

                if (!response.IsSuccessStatusCode)
                {
                    var status = (int)response.StatusCode;
                    var transient = response.StatusCode == HttpStatusCode.TooManyRequests
                                    || response.StatusCode == HttpStatusCode.RequestTimeout
                                    || (status >= 500 && status <= 599);
                    if (transient && attempt < maxAttempts)
                    {
                        var delay = response.StatusCode == HttpStatusCode.TooManyRequests
                            ? ResolveRetryDelay(response, responseText, attempt)
                            : TimeSpan.FromMilliseconds(400 * attempt);
                        _logger.LogWarning(
                            "Groq API returned transient {StatusCode}; retrying attempt {Attempt}/{MaxAttempts} after {DelayMs} ms.",
                            response.StatusCode,
                            attempt + 1,
                            maxAttempts,
                            delay.TotalMilliseconds);
                        await Task.Delay(delay, ct);
                        continue;
                    }

                    _logger.LogWarning("Groq API returned error status {StatusCode}", response.StatusCode);
                    throw new InvalidOperationException("Dịch vụ tạm thời gián đoạn. Vui lòng thử lại sau.");
                }

                var json = JsonSerializer.Deserialize<OpenRouterChatResponse>(responseText, SerializerOptions);

                if (json?.Choices == null || json.Choices.Count == 0)
                    throw new InvalidOperationException("No response from Groq");

                // Use provider-reported token count (accurate) instead of length-based heuristic.
                return (json.Choices[0].Message.Content, json.Usage?.TotalTokens);
            }

            throw new InvalidOperationException("Dịch vụ tạm thời gián đoạn. Vui lòng thử lại sau.");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "Failed to get response from Groq");
            throw new InvalidOperationException("Dịch vụ tạm thời gián đoạn. Vui lòng thử lại sau.", ex);
        }
    }

    private static HttpRequestMessage CloneRequestForRetry(HttpRequestMessage original, string jsonContent)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri);
        clone.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
        foreach (var header in original.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return clone;
    }

    private static TimeSpan ResolveRetryDelay(HttpResponseMessage response, string responseText, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is { } retryAfterDelta)
            return ClampRetryDelay(retryAfterDelta);

        var marker = "try again in ";
        var index = responseText.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            var start = index + marker.Length;
            var end = responseText.IndexOf('s', start);
            if (end > start &&
                double.TryParse(
                    responseText[start..end],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var seconds))
            {
                return ClampRetryDelay(TimeSpan.FromSeconds(seconds));
            }
        }

        return TimeSpan.FromMilliseconds(250 * attempt);
    }

    private static TimeSpan ClampRetryDelay(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero)
            return TimeSpan.Zero;

        return delay > TimeSpan.FromSeconds(2)
            ? TimeSpan.FromSeconds(2)
            : delay;
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

        if (!await _rateLimitService.CheckUserRateLimitAsync(request.MembershipId, ct))
            throw new InvalidOperationException("Rate limit exceeded. Please wait a moment before sending another message.");

        if (!await _rateLimitService.CheckTenantRateLimitAsync(request.TenantId, ct))
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
        EnsureRequestedDepartmentWithinProfile(request, authorizationProfile);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var defaultReportingFrom = new DateOnly(today.Year, today.Month, 1);
        var defaultReportingTo = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        var effectiveQuery = await ResolveEffectiveQueryAsync(request, ct);
        var intent = await _intentPlanner.ClassifyAsync(new ChatIntentPlanningRequest(effectiveQuery, today), ct);
        var reportingFrom = defaultReportingFrom;
        var reportingTo = defaultReportingTo;
        var contextualPlanApplied = false;

        var contextualPlan = await ResolveContextualPlanAsync(
            request,
            effectiveQuery,
            intent,
            today,
            ct);
        if (contextualPlan is not null)
        {
            effectiveQuery = contextualPlan.EffectiveQuery;
            intent = contextualPlan.Intent;
            reportingFrom = contextualPlan.ReportingFrom ?? reportingFrom;
            reportingTo = contextualPlan.ReportingTo ?? reportingTo;
            contextualPlanApplied = true;
        }

        if (intent.ReportingTask == ChatReportingTask.EntityStatusLookup)
        {
            intent = new ChatIntentClassification(
                ChatExecutionMode.Rag,
                intent.Reason,
                ChatIntentFamily.DocumentLookup,
                intent.ScopeConfidence,
                intent.ReportingTask);
        }

        var policyDecision = _chatPolicyEngine.Decide(authorizationProfile, intent, effectiveQuery);
        LogPolicyDecision(request, authorizationProfile, intent, policyDecision, streamed);

        return new PreparedChatExecution(
            request,
            effectiveQuery,
            quotaDecisionResult.Value,
            authorizationProfile,
            intent,
            policyDecision,
            reportingFrom,
            reportingTo,
            contextualPlanApplied);
    }

    private async Task<ContextualChatPlan?> ResolveContextualPlanAsync(
        ChatRequest request,
        string effectiveQuery,
        ChatIntentClassification initialIntent,
        DateOnly today,
        CancellationToken ct)
    {
        if (!request.SessionId.HasValue ||
            initialIntent.Mode != ChatExecutionMode.Rag ||
            initialIntent.Family != ChatIntentFamily.Unknown ||
            initialIntent.ScopeConfidence != ChatScopeConfidence.Ambiguous)
        {
            return null;
        }

        var session = await _chatRepository.GetOwnedSessionAsync(
            request.SessionId.Value,
            request.TenantId,
            request.MembershipId,
            ct);
        if (session is null)
            return null;

        var history = await _chatRepository.GetMessagesBySessionAsync(session.Id, ct);
        if (history.Count == 0)
            return null;

        var context = await _conversationStateManager.GetContextAsync(session.Id, ct);
        var lastTurn = context?.LastTurn ?? await ReconstructLastTurnFromHistoryAsync(history, today, ct);
        if (lastTurn is null)
            return null;

        var deterministicContextualPlan = TryBuildDeterministicContextualPlan(
            request.Query,
            effectiveQuery,
            lastTurn,
            today);
        if (deterministicContextualPlan is not null)
            return deterministicContextualPlan;

        var contextualPlan = await _contextualChatPlanner.PlanAsync(
            new ContextualChatPlanRequest(
                request.Query,
                effectiveQuery,
                initialIntent,
                history,
                lastTurn,
                today),
            ct);

        return contextualPlan;
    }

    private static ContextualChatPlan? TryBuildDeterministicContextualPlan(
        string originalQuery,
        string effectiveQuery,
        ConversationTurnState lastTurn,
        DateOnly today)
    {
        var reportingPlan = TryBuildDeterministicReportingPeriodPlan(originalQuery, effectiveQuery, lastTurn, today);
        if (reportingPlan is not null)
            return reportingPlan;

        if (!string.Equals(lastTurn.AnswerSource, ChatAnswerSource.Rag.ToString(), StringComparison.Ordinal) ||
            !LooksLikeEntityStatusLookup(originalQuery) ||
            LooksLikeStateChangingApprovalRequest(originalQuery))
        {
            return null;
        }

        return new ContextualChatPlan(
            effectiveQuery,
            new ChatIntentClassification(
                ChatExecutionMode.Rag,
                "deterministic-context-status-lookup",
                ChatIntentFamily.DocumentLookup,
                ChatScopeConfidence.SafeInferred,
                ChatReportingTask.EntityStatusLookup),
            null,
            null);
    }

    private static ContextualChatPlan? TryBuildDeterministicReportingPeriodPlan(
        string originalQuery,
        string effectiveQuery,
        ConversationTurnState lastTurn,
        DateOnly today)
    {
        if (!string.Equals(lastTurn.AnswerSource, ChatAnswerSource.Reporting.ToString(), StringComparison.Ordinal) ||
            !TryResolveRelativeReportingPeriod(originalQuery, lastTurn, today, out var from, out var to))
        {
            return null;
        }

        var family = ParseEnum(lastTurn.IntentFamily, ChatIntentFamily.Aggregate);
        var task = ParseEnum(lastTurn.ReportingTask, ChatReportingTask.Summary);

        if (task == ChatReportingTask.Unknown)
            task = family == ChatIntentFamily.Ranking ? ChatReportingTask.VendorRanking : ChatReportingTask.Summary;

        return new ContextualChatPlan(
            effectiveQuery,
            new ChatIntentClassification(
                ChatExecutionMode.Reporting,
                "deterministic-context-period-carry",
                family,
                ChatScopeConfidence.SafeInferred,
                task),
            from,
            to);
    }

    private static bool TryResolveRelativeReportingPeriod(
        string query,
        ConversationTurnState lastTurn,
        DateOnly today,
        out DateOnly from,
        out DateOnly to)
    {
        var normalized = IntentTextNormalizer.Normalize(query);
        var anchor = lastTurn.PeriodFrom ?? new DateOnly(today.Year, today.Month, 1);

        if (ContainsSemanticPhrase(normalized, "thang truoc") ||
            ContainsSemanticPhrase(normalized, "previous month") ||
            ContainsSemanticPhrase(normalized, "prev month") ||
            ContainsSemanticPhrase(normalized, "last month"))
        {
            from = new DateOnly(anchor.Year, anchor.Month, 1).AddMonths(-1);
            to = new DateOnly(from.Year, from.Month, DateTime.DaysInMonth(from.Year, from.Month));
            return true;
        }

        from = default;
        to = default;
        return false;
    }

    private static TEnum ParseEnum<TEnum>(string value, TEnum fallback)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            ? parsed
            : fallback;

    private static bool LooksLikeEntityStatusLookup(string query)
    {
        var normalized = IntentTextNormalizer.Normalize(query);
        if (string.IsNullOrWhiteSpace(normalized))
            return false;

        var asksStatus =
            ContainsSemanticPhrase(normalized, "trang thai") ||
            ContainsSemanticPhrase(normalized, "tinh trang") ||
            ContainsSemanticPhrase(normalized, "status") ||
            ContainsSemanticPhrase(normalized, "approved") ||
            ContainsSemanticPhrase(normalized, "approval") ||
            ContainsSemanticPhrase(normalized, "rejected") ||
            ContainsSemanticPhrase(normalized, "duyet") ||
            ContainsSemanticPhrase(normalized, "phe duyet") ||
            ContainsSemanticPhrase(normalized, "cho duyet") ||
            ContainsSemanticPhrase(normalized, "tu choi");

        if (!asksStatus)
            return false;

        return query.Contains('?', StringComparison.Ordinal) ||
            ContainsSemanticPhrase(normalized, "chua") ||
            ContainsSemanticPhrase(normalized, "da") ||
            ContainsSemanticPhrase(normalized, "whether") ||
            ContainsSemanticPhrase(normalized, "is") ||
            ContainsSemanticPhrase(normalized, "was");
    }

    private static bool LooksLikeStateChangingApprovalRequest(string query)
    {
        var normalized = IntentTextNormalizer.Normalize(query);
        return ContainsSemanticPhrase(normalized, "duyet giup") ||
            ContainsSemanticPhrase(normalized, "approve giup") ||
            ContainsSemanticPhrase(normalized, "phe duyet giup") ||
            ContainsSemanticPhrase(normalized, "duyet luon") ||
            ContainsSemanticPhrase(normalized, "approve it") ||
            ContainsSemanticPhrase(normalized, "approve now");
    }

    private static bool ContainsSemanticPhrase(string normalizedQuery, string phrase)
    {
        var paddedQuery = $" {normalizedQuery} ";
        var paddedPhrase = $" {phrase} ";
        return paddedQuery.Contains(paddedPhrase, StringComparison.Ordinal);
    }

    private async Task<ConversationTurnState?> ReconstructLastTurnFromHistoryAsync(
        IReadOnlyList<ChatMessage> history,
        DateOnly today,
        CancellationToken ct)
    {
        var lastUserMessage = history
            .Where(message => message.Role == ChatMessageRole.User)
            .LastOrDefault();
        if (lastUserMessage is null)
            return null;

        var intent = await _intentPlanner.ClassifyAsync(new ChatIntentPlanningRequest(lastUserMessage.Content, today), ct);
        if (intent.Mode != ChatExecutionMode.Reporting)
            return null;

        var reportingFrom = new DateOnly(today.Year, today.Month, 1);
        var reportingTo = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

        return ConversationTurnState.Create(
            lastUserMessage.Content,
            lastUserMessage.Content,
            intent.Mode.ToString(),
            intent.Family.ToString(),
            intent.ReportingTask.ToString(),
            intent.Reason,
            intent.ScopeConfidence.ToString(),
            ChatAnswerSource.Reporting.ToString(),
            reportingFrom,
            reportingTo);
    }

    private async Task<string> ResolveEffectiveQueryAsync(ChatRequest request, CancellationToken ct)
    {
        // Always resolve entity references when we have a session with history,
        // not just for scope clarification answers (e.g., "tháng đó" should resolve to "tháng 5")
        if (request.SessionId.HasValue)
        {
            var session = await _chatRepository.GetOwnedSessionAsync(request.SessionId.Value, request.TenantId, request.MembershipId, ct);
            if (session is not null)
            {
                var messages = await _chatRepository.GetMessagesBySessionAsync(session.Id, ct);
                var context = await _conversationStateManager.GetContextAsync(session.Id, ct);

                // Check if this might be a scope clarification answer
                if (IsScopeClarificationAnswer(request.Query))
                {
                    var pendingScopeQuery = context?.PendingClarification?.Kind == PendingClarificationKind.Scope
                        ? context.PendingClarification.OriginalQuery
                        : null;

                    var previousScopeQuestion = !string.IsNullOrWhiteSpace(pendingScopeQuery)
                        ? pendingScopeQuery
                        : FindPreviousQuestionForScopeClarification(messages);

                    if (!string.IsNullOrWhiteSpace(previousScopeQuestion))
                        return await ResolveScopeClarificationAnswerAsync(
                            session.Id,
                            previousScopeQuestion,
                            request.Query,
                            messages,
                            context,
                            ct);
                }
                // For non-scope queries with history, still resolve entity references like "tháng đó"
                else if (messages.Count > 0)
                {
                    var resolvedResult = await _hybridResolutionRouter.RouteAsync(
                        request.Query,
                        messages,
                        context,
                        ct);

                    if (resolvedResult.RequiresClarification && !string.IsNullOrEmpty(resolvedResult.ClarificationPrompt))
                    {
                        _logger.LogInformation(
                            "HybridResolutionRouter requested clarification: {Prompt}",
                            resolvedResult.ClarificationPrompt);
                    }

                    return resolvedResult.ResolvedQuery;
                }
            }
        }

        return request.Query;
    }

    private async Task<string> ResolveScopeClarificationAnswerAsync(
        Guid sessionId,
        string previousUserQuestion,
        string clarificationAnswer,
        IReadOnlyList<ChatMessage> messages,
        ConversationContext? context,
        CancellationToken ct)
    {
        var resolvedResult = await _hybridResolutionRouter.RouteAsync(
            $"{previousUserQuestion.Trim()} {clarificationAnswer.Trim()}",
            messages,
            context,
            ct);

        if (resolvedResult.RequiresClarification && !string.IsNullOrEmpty(resolvedResult.ClarificationPrompt))
        {
            _logger.LogInformation(
                "HybridResolutionRouter requested clarification: {Prompt}",
                resolvedResult.ClarificationPrompt);
        }

        if (context?.PendingClarification is not null)
        {
            context.ClearPendingClarification();
            await _conversationStateManager.SaveContextAsync(sessionId, context, ct);
        }

        return resolvedResult.ResolvedQuery;
    }

    private static string? FindPreviousQuestionForScopeClarification(IReadOnlyList<ChatMessage> messages)
    {
        var clarificationIndex = messages
            .Select((message, index) => new { Message = message, Index = index })
            .Where(x => x.Message.Role == ChatMessageRole.Assistant && IsScopeClarificationPrompt(x.Message.Content))
            .Select(x => x.Index)
            .LastOrDefault(-1);

        if (clarificationIndex <= 0)
            return null;

        return messages
            .Take(clarificationIndex)
            .LastOrDefault(message => message.Role == ChatMessageRole.User)
            ?.Content;
    }

    private static bool IsScopeClarificationAnswer(string query)
    {
        var normalized = IntentTextNormalizer.Normalize(query);
        return normalized is "toan cong ty" or "cong ty" or "company" or "all company" or
            "phong ban cua toi" or "phong ban toi" or "bo phan cua toi" or "bo phan toi" or "team toi" or "my team" or
            "cua toi" or "cua em" or "cua minh" or "toi" or "minh" or "my";
    }

    private static bool IsScopeClarificationPrompt(string content)
    {
        var normalized = IntentTextNormalizer.Normalize(content);
        return normalized.Contains("ban muon xem trong pham vi", StringComparison.Ordinal) &&
               (normalized.Contains("toan cong ty", StringComparison.Ordinal) ||
                normalized.Contains("phong ban", StringComparison.Ordinal));
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
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        var reportingTask = ResolveReportingTask(intent);

        if (reportingTask == ChatReportingTask.ApprovalQueue)
        {
            var approvalAnswer = await _chatReportingService.BuildPendingApprovalSummaryAsync(profile, query, ct);
            return new ReportingExecutionResult(approvalAnswer.Answer, approvalAnswer.RecordCount);
        }

        if (reportingTask == ChatReportingTask.Trend)
        {
            var trendAnswer = await _chatReportingService.BuildMonthlyTrendSummaryAsync(profile, query, ct);
            return new ReportingExecutionResult(trendAnswer.Answer, trendAnswer.RecordCount);
        }

        if (reportingTask == ChatReportingTask.VendorRanking)
        {
            var vendorAnswer = await _chatReportingService.BuildTopVendorsSummaryAsync(profile, query, from, to, ct);
            return new ReportingExecutionResult(vendorAnswer.Answer, vendorAnswer.RecordCount);
        }

        if (reportingTask == ChatReportingTask.BudgetUtilization)
        {
            var budgetAnswer = await _chatReportingService.BuildBudgetUtilizationSummaryAsync(profile, query, from, to, ct);
            return new ReportingExecutionResult(budgetAnswer.Answer, budgetAnswer.RecordCount);
        }

        if (reportingTask == ChatReportingTask.EmployeeRanking)
        {
            var rankingAnswer = await _chatReportingService.BuildTopEmployeesSummaryAsync(profile, query, from, to, ct);
            return new ReportingExecutionResult(rankingAnswer.Answer, rankingAnswer.RecordCount);
        }

        if (reportingTask == ChatReportingTask.Comparison)
        {
            var comparisonAnswer = await _chatReportingService.BuildExpenseComparisonAsync(profile, query, from, to, ct);
            return new ReportingExecutionResult(comparisonAnswer.Answer, comparisonAnswer.RecordCount);
        }

        // OwnSummary must be checked BEFORE the Summary/Aggregate branch: an own-spending
        // query ("tháng này tôi tiêu bao nhiêu") classifies as Family=OwnSummary with
        // Task=Summary, and would otherwise fall into the tenant-wide scoped summary below.
        if (intent.Family == ChatIntentFamily.OwnSummary)
        {
            var ownAnswer = await _chatReportingService.BuildOwnExpenseSummaryAsync(profile, from, to, ct);
            return new ReportingExecutionResult(ownAnswer.Answer, ownAnswer.RecordCount);
        }

        if (reportingTask == ChatReportingTask.Summary || intent.Family == ChatIntentFamily.Aggregate)
        {
            var aggregateAnswer = await _chatReportingService.BuildScopedExpenseSummaryAsync(profile, query, from, to, ct);
            return new ReportingExecutionResult(aggregateAnswer.Answer, aggregateAnswer.RecordCount);
        }

        var reportingAnswer = await _chatReportingService.BuildOwnExpenseSummaryAsync(profile, from, to, ct);
        return new ReportingExecutionResult(reportingAnswer.Answer, reportingAnswer.RecordCount);
    }

    private static ChatReportingTask ResolveReportingTask(ChatIntentClassification intent)
    {
        if (intent.ReportingTask != ChatReportingTask.Unknown)
            return intent.ReportingTask;

        if (string.Equals(intent.Reason, "approval-reporting", StringComparison.Ordinal))
            return ChatReportingTask.ApprovalQueue;
        if (string.Equals(intent.Reason, "trend-reporting", StringComparison.Ordinal))
            return ChatReportingTask.Trend;
        if (string.Equals(intent.Reason, "vendor-reporting", StringComparison.Ordinal))
            return ChatReportingTask.VendorRanking;
        if (string.Equals(intent.Reason, "budget-reporting", StringComparison.Ordinal))
            return ChatReportingTask.BudgetUtilization;
        if (intent.Family == ChatIntentFamily.Ranking)
            return ChatReportingTask.EmployeeRanking;
        if (intent.Family == ChatIntentFamily.ApprovalQueue)
            return ChatReportingTask.ApprovalQueue;
        if (intent.Family == ChatIntentFamily.Comparison)
            return ChatReportingTask.Comparison;
        if (string.Equals(intent.Reason, "comparison-reporting", StringComparison.Ordinal))
            return ChatReportingTask.Comparison;
        if (intent.Family == ChatIntentFamily.Aggregate)
            return ChatReportingTask.Summary;

        return ChatReportingTask.Unknown;
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

        // Check if we should use compressed summary
        string? compressedSummary = null;
        if (await _contextSummarizationService.ShouldSummarizeAsync(history, ct))
        {
            var contextData = await _conversationStateManager.GetContextAsync(context.Session.SessionId, ct);
            compressedSummary = contextData?.CompressedSummary;

            if (string.IsNullOrEmpty(compressedSummary))
            {
                compressedSummary = await _contextSummarizationService.SummarizeAsync(history, ct);
                if (contextData != null && !string.IsNullOrEmpty(compressedSummary))
                {
                    contextData.SetCompressedSummary(compressedSummary);
                }
            }
        }

        return _promptBuilder.BuildFullPrompt(query, context.TopChunks, context.AccessScope, limitedHistory, compressedSummary);
    }

    private async Task<Prompt> BuildGeneralPromptAsync(
        string query,
        ChatIntentClassification classification,
        Guid sessionId,
        CancellationToken ct,
        ChatAuthorizationProfile? actor = null)
    {
        var history = await _chatRepository.GetMessagesBySessionAsync(sessionId, ct);
        var limitedHistory = history
            .TakeLast(MaxHistoryMessages)
            .Select(m => ChatMessage.Create(m.SessionId, m.SenderId, m.Role, ChatPromptSanitizer.Sanitize(m.Content)))
            .ToList();

        return _promptBuilder.BuildGeneralPrompt(query, classification, limitedHistory, actor);
    }

    private async Task<GeneralExecutionResult> ExecuteGeneralAsync(
        string query,
        ChatIntentClassification classification,
        Guid sessionId,
        CancellationToken ct,
        ChatAuthorizationProfile? actor = null)
    {
        if (classification.Family == ChatIntentFamily.LowSignal)
            return new GeneralExecutionResult(LowSignalClarificationMessage, null);

        if (classification.Family == ChatIntentFamily.Greeting)
            return new GeneralExecutionResult(GreetingMessage, null);

        var prompt = await BuildGeneralPromptAsync(query, classification, sessionId, ct, actor);
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
        CancellationToken ct,
        ChatTurnStateInput? turnState = null)
    {
        if (session.IsNewSession)
            await _chatRepository.AddSessionAsync(session.Session, ct);

        var turnCreatedAt = DateTime.UtcNow;
        var userMessage = ChatMessage.Create(
            session.SessionId,
            request.MembershipId,
            ChatMessageRole.User,
            request.Query,
            createdAtUtc: turnCreatedAt);
        var assistantMessage = ChatMessage.Create(
            session.SessionId,
            request.MembershipId,
            ChatMessageRole.Assistant,
            assistantResponse,
            createdAtUtc: turnCreatedAt.AddMilliseconds(1));

        // Persist tracked entities to user message so they survive to next turn
        var context = await _conversationStateManager.GetContextAsync(session.SessionId, ct);
        if (context != null)
        {
            var activeEntities = context.GetActiveEntities();
            if (activeEntities.Count > 0)
            {
                var trackedFactsJson = JsonSerializer.Serialize(
                    activeEntities.Select(e => new { e.CanonicalName, Type = e.Type.ToString(), e.Aliases }),
                    SerializerOptions);
                userMessage.SetScopeContext(trackedFactsJson);
            }
        }

        await _chatRepository.AddMessageAsync(userMessage, ct);
        await _chatRepository.AddMessageAsync(assistantMessage, ct);

        session.Session.UpdateTitle(TruncateForTitle(request.Query));

        // Track conversation state: entities and intents
        await TrackConversationStateAsync(session.SessionId, request, turnState, ct);

        await _unitOfWork.SaveChangesAsync(ct);
        return assistantMessage;
    }

    private static ChatTurnStateInput BuildTurnState(
        PreparedChatExecution prepared,
        ChatAnswerSource answerSource) =>
        new(
            prepared.EffectiveQuery,
            prepared.Intent.Mode.ToString(),
            prepared.Intent.Family.ToString(),
            prepared.Intent.ReportingTask.ToString(),
            prepared.Intent.Reason,
            prepared.Intent.ScopeConfidence.ToString(),
            answerSource.ToString(),
            prepared.Intent.Mode == ChatExecutionMode.Reporting ? prepared.ReportingFrom : null,
            prepared.Intent.Mode == ChatExecutionMode.Reporting ? prepared.ReportingTo : null);

    private async Task RecordPendingClarificationAsync(
        Guid sessionId,
        PreparedChatExecution prepared,
        string prompt,
        CancellationToken ct)
    {
        if (prepared.PolicyDecision.Kind != ChatPolicyDecisionKind.Clarify ||
            !IsScopeClarificationIntent(prepared.Intent))
        {
            return;
        }

        try
        {
            var context = await _conversationStateManager.GetOrCreateContextAsync(sessionId, ct);
            context.SetPendingClarification(PendingClarification.Scope(
                prepared.EffectiveQuery,
                prompt,
                prepared.Intent.Reason));
            await _conversationStateManager.SaveContextAsync(sessionId, context, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record pending scope clarification for session {SessionId}", sessionId);
        }
    }

    private static bool IsScopeClarificationIntent(ChatIntentClassification intent)
    {
        if (intent.ScopeConfidence != ChatScopeConfidence.Ambiguous)
            return false;

        return intent.Family is ChatIntentFamily.Aggregate
            or ChatIntentFamily.Comparison
            or ChatIntentFamily.Ranking;
    }

    private async Task TrackConversationStateAsync(
        Guid sessionId,
        ChatRequest request,
        ChatTurnStateInput? turnState,
        CancellationToken ct)
    {
        try
        {
            var context = await _conversationStateManager.GetOrCreateContextAsync(sessionId, ct);

            // Increment turn count
            context.IncrementTurn();

            if (ShouldExtractEntitiesForTurn(turnState))
            {
                // Get conversation history for LLM-based entity extraction
                var history = await _chatRepository.GetMessagesBySessionAsync(sessionId, ct);

                // Extract and track entities from the user query using LLM-based extraction
                var extractedEntities = await _llmEntityExtractor.ExtractEntitiesAsync(request.Query, history, ct);
                foreach (var entity in extractedEntities)
                {
                    if (entity.Confidence > 0.5f)
                    {
                        var trackedEntity = TrackedEntity.ExtractEntity(
                            entity.NormalizedForm ?? entity.Text,
                            entity.Type,
                            context.TurnCount);
                        context.AddEntity(trackedEntity);
                    }
                }
            }

            if (turnState is not null)
            {
                context.SetLastTurn(ConversationTurnState.Create(
                    request.Query,
                    turnState.EffectiveQuery,
                    turnState.ExecutionMode,
                    turnState.IntentFamily,
                    turnState.ReportingTask,
                    turnState.IntentReason,
                    turnState.ScopeConfidence,
                    turnState.AnswerSource,
                    turnState.ReportingFrom,
                    turnState.ReportingTo));

                if (!context.IntentStack.IsLocked)
                {
                    var frame = IntentFrame.Create(turnState.IntentFamily);
                    frame.SetSlot("executionMode", turnState.ExecutionMode);
                    frame.SetSlot("reportingTask", turnState.ReportingTask);
                    frame.SetSlot("intentReason", turnState.IntentReason);
                    frame.SetSlot("scopeConfidence", turnState.ScopeConfidence);
                    frame.SetSlot("answerSource", turnState.AnswerSource);
                    context.IntentStack.Push(frame);
                }
            }

            // Cleanup expired entities
            context.CleanupExpiredEntities();

            // Save all changes in a single cache write
            await _conversationStateManager.SaveContextAsync(sessionId, context, ct);
        }
        catch (Exception ex)
        {
            // Log but don't fail the conversation persistence
            _logger.LogWarning(ex, "Failed to track conversation state for session {SessionId}", sessionId);
        }
    }

    private static bool ShouldExtractEntitiesForTurn(ChatTurnStateInput? turnState)
    {
        return !string.Equals(
            turnState?.ExecutionMode,
            ChatExecutionMode.Reporting.ToString(),
            StringComparison.Ordinal);
    }

    private static List<TrackedEntity> ExtractEntitiesFromQuery(string query)
    {
        var entities = new List<TrackedEntity>();
        var normalized = IntentTextNormalizer.Normalize(query);

        // Simple keyword-based entity extraction
        // Department patterns
        if (normalized.Contains("phong ban") || normalized.Contains("bo phan"))
        {
            var deptMatch = ExtractEntityValue(query, new[] { "phòng ban", "bộ phận" });
            if (!string.IsNullOrEmpty(deptMatch))
            {
                entities.Add(TrackedEntity.Create(deptMatch, EntityType.DEPARTMENT, turnNumber: 0));
            }
        }

        // Money patterns
        var moneyPatterns = new[] { "vnd", "đồng", "triệu", "nghìn" };
        foreach (var pattern in moneyPatterns)
        {
            if (normalized.Contains(pattern))
            {
                var amountMatch = ExtractEntityValue(query, moneyPatterns);
                if (!string.IsNullOrEmpty(amountMatch))
                {
                    entities.Add(TrackedEntity.Create(amountMatch, EntityType.MONEY, turnNumber: 0));
                    break;
                }
            }
        }

        // Date patterns
        var datePatterns = new[] { "tháng", "năm", "ngày", "tuần" };
        foreach (var pattern in datePatterns)
        {
            if (normalized.Contains(pattern))
            {
                var dateMatch = ExtractEntityValue(query, datePatterns);
                if (!string.IsNullOrEmpty(dateMatch))
                {
                    entities.Add(TrackedEntity.Create(dateMatch, EntityType.DATE, turnNumber: 0));
                    break;
                }
            }
        }

        return entities;
    }

    private static string? ExtractEntityValue(string query, string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            var index = query.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                // Extract surrounding context
                var start = Math.Max(0, index - 20);
                var length = Math.Min(40, query.Length - start);
                var value = query.Substring(start, length).Trim();
                return value.Length > 35 ? value[..32] + "..." : value;
            }
        }
        return null;
    }

    private static string DetermineIntentType(string query)
    {
        var normalized = IntentTextNormalizer.Normalize(query);

        if (normalized.Contains("bao nhieu") || normalized.Contains("chi bao nhieu") || normalized.Contains("tong"))
            return "expense-query";
        if (normalized.Contains("chung tu") || normalized.Contains("hoa don") || normalized.Contains("receipt"))
            return "document-query";
        if (normalized.Contains("duyet") || normalized.Contains("approve") || normalized.Contains("phe duyet"))
            return "approval-query";
        if (normalized.Contains("ngan sach") || normalized.Contains("budget"))
            return "budget-query";
        if (normalized.Contains("so sanh") || normalized.Contains("comparison"))
            return "comparison-query";

        return "general-query";
    }

    private static bool IsSemanticallyVague(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 10)
            return true;

        var normalized = IntentTextNormalizer.Normalize(query);

        // Patterns that are too incomplete/vague without additional context
        var vaguePatterns = new[]
        {
            "cho toi xem",
            "xem gi",
            "xem nao",
            "xem di",
            "nhung ai",
            "ai da",
            "ai duyet",
            "ai phe duyet",
            "gi the",
            "gi vay",
            "the nao"
        };

        foreach (var pattern in vaguePatterns)
        {
            if (normalized.Contains(pattern))
                return true;
        }

        return false;
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

    private async Task<ChatResponse> HandleMultiIntentQueryAsync(
        PreparedChatExecution prepared,
        IReadOnlyList<string> subQueries,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Processing multi-intent query with {Count} sub-queries for membership {MembershipId}",
            subQueries.Count,
            prepared.Request.MembershipId);

        var answers = new List<string>();

        foreach (var subQuery in subQueries)
        {
            var subRequest = prepared.Request with { Query = subQuery };
            var subPrepared = await PrepareSingleIntentExecutionAsync(subRequest, prepared.QuotaDecision, ct);

            string answer;
            if (subPrepared.Intent.Mode == ChatExecutionMode.Reporting)
            {
                var reporting = await ExecuteReportingAsync(
                    subPrepared.AuthorizationProfile,
                    subQuery,
                    subPrepared.Intent,
                    subPrepared.ReportingFrom,
                    subPrepared.ReportingTo,
                    ct);
                answer = reporting.Answer;
            }
            else if (subPrepared.Intent.Mode == ChatExecutionMode.General)
            {
                var general = await ExecuteGeneralAsync(subQuery, subPrepared.Intent, prepared.Request.SessionId ?? Guid.NewGuid(), ct, subPrepared.AuthorizationProfile);
                answer = general.Answer;
            }
            else
            {
                // RAG mode - use the sub-query for retrieval
                var subRagContext = await PrepareRagExecutionAsync(subRequest, ct);

                if (subRagContext.TopChunks.Count == 0)
                {
                    answer = "Tôi không tìm thấy thông tin phù hợp để trả lời câu hỏi này.";
                }
                else
                {
                    var formatted = ChatRagBusinessFormatter.TryFormat(
                        subQuery,
                        subRagContext.TopChunks,
                        subPrepared.Intent.ReportingTask);
                    if (formatted is not null)
                    {
                        answer = ChatCitationParser.StripMarkers(formatted.Answer);
                    }
                    else
                    {
                        var prompt = await BuildRagPromptAsync(subQuery, subRagContext, ct);
                        var (ragAnswer, _) = await CallOpenRouterAsync(prompt, ct);
                        answer = ChatCitationParser.StripMarkers(ragAnswer);
                    }
                }
            }

            answers.Add($"**Câu hỏi:** {subQuery}\n**Trả lời:** {answer}");
        }

        var combinedAnswer = string.Join("\n\n", answers);
        var session = await ResolveSessionAsync(prepared.Request, ct);
        var assistantMessage = await PersistConversationAsync(prepared.Request, session, combinedAnswer, ct);
        await RecordUsageAsync(prepared.Request, prepared.QuotaDecision, null, ct);

        return new ChatResponse(
            combinedAnswer,
            session.SessionId,
            assistantMessage.Id,
            0,
            0,
            ChatAnswerSource.Rag,
            []);
    }

    private async Task<PreparedChatExecution> PrepareSingleIntentExecutionAsync(
        ChatRequest request,
        SubscriptionQuotaDecision quotaDecision,
        CancellationToken ct)
    {
        var sanitizedQuery = ChatPromptSanitizer.Sanitize(request.Query);
        request = request with { Query = sanitizedQuery };

        var authorizationProfile = await _chatAuthorizationService.GetAuthorizationProfileAsync(request.MembershipId, ct);
        EnsureTenantAccess(request, authorizationProfile);
        EnsureRequestedDepartmentWithinProfile(request, authorizationProfile);

        var effectiveQuery = await ResolveEffectiveQueryAsync(request, ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var intent = await _intentPlanner.ClassifyAsync(new ChatIntentPlanningRequest(effectiveQuery, today), ct);
        var policyDecision = _chatPolicyEngine.Decide(authorizationProfile, intent, effectiveQuery);
        var reportingFrom = new DateOnly(today.Year, today.Month, 1);
        var reportingTo = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

        return new PreparedChatExecution(request, effectiveQuery, quotaDecision, authorizationProfile, intent, policyDecision, reportingFrom, reportingTo, ContextualPlanApplied: false);
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
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("Chat base URL is not configured.");

        return ChatCompletionsEndpointBuilder.Build(baseUrl);
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
        [property: System.Text.Json.Serialization.JsonPropertyName("prompt_tokens")] int? PromptTokens,
        [property: System.Text.Json.Serialization.JsonPropertyName("completion_tokens")] int? CompletionTokens,
        [property: System.Text.Json.Serialization.JsonPropertyName("total_tokens")] int? TotalTokens
    );

    private sealed record PreparedChatExecution(
        ChatRequest Request,
        string EffectiveQuery,
        SubscriptionQuotaDecision QuotaDecision,
        ChatAuthorizationProfile AuthorizationProfile,
        ChatIntentClassification Intent,
        ChatPolicyDecision PolicyDecision,
        DateOnly ReportingFrom,
        DateOnly ReportingTo,
        bool ContextualPlanApplied);

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

    private sealed record ChatTurnStateInput(
        string EffectiveQuery,
        string ExecutionMode,
        string IntentFamily,
        string ReportingTask,
        string IntentReason,
        string ScopeConfidence,
        string AnswerSource,
        DateOnly? ReportingFrom,
        DateOnly? ReportingTo);

    private sealed record RagExecutionContext(
        ResolvedChatSession Session,
        ChatAccessScope AccessScope,
        Guid? EffectiveDepartmentId,
        Guid? OwnerFilter,
        IReadOnlyList<DocumentChunk> SearchChunks,
        IReadOnlyList<DocumentChunk> TopChunks);
}
