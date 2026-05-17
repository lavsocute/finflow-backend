using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Common.Abstractions;
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
    private const string AuthorizedNoContextMessage = "There is not enough authorized context to answer that question.";
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
        ICurrentTenant currentTenant,
        IUnitOfWork unitOfWork,
        ICacheService cacheService,
        HttpClient httpClient,
        IOptions<GroqChatOptions> options,
        ILogger<ChatService> logger,
        IAuditLogRepository? auditLogRepository = null,
        IChatOutputFilter? outputFilter = null,
        IContentModerator? contentModerator = null,
        IQueryRewriter? queryRewriter = null)
    {
        _chatRepository = chatRepository;
        _chatAuthorizationService = chatAuthorizationService;
        _subscriptionQuotaGate = subscriptionQuotaGate;
        _embeddingService = embeddingService;
        _vectorStore = vectorStore;
        _rerankService = rerankService;
        _promptBuilder = promptBuilder;
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
        if (string.IsNullOrWhiteSpace(request.Query))
            throw new InvalidOperationException("Query cannot be empty.");

        if (request.Query.Length > MaxQueryLength)
            throw new InvalidOperationException($"Query exceeds maximum length of {MaxQueryLength} characters.");

        // Sanitize user query before any processing (Unicode tricks, label injection).
        var sanitizedQuery = ChatPromptSanitizer.Sanitize(request.Query);
        request = request with { Query = sanitizedQuery };

        // Content moderation: reject query before LLM call (saves cost + compliance).
        if (_contentModerator is not null)
        {
            var moderationReason = _contentModerator.Moderate(sanitizedQuery);
            if (moderationReason is not null)
            {
                _logger.LogWarning(
                    "Chat content moderation rejected query for membership {MembershipId}, reason: {Reason}",
                    request.MembershipId,
                    moderationReason);
                throw new InvalidOperationException("Your message was rejected by our content policy. Please rephrase.");
            }
        }

        // Per-user rate limit: Redis SETNX-style via atomic INCR with TTL set only on creation.
        var rateLimitKey = $"chat:ratelimit:{request.MembershipId}";
        var perUserCount = await _cacheService.IncrementWithExpiryAsync(rateLimitKey, TimeSpan.FromSeconds(RateLimitSeconds), ct);
        if (perUserCount > 1)
            throw new InvalidOperationException("Rate limit exceeded. Please wait a moment before sending another message.");

        // Tenant-level rate limit: atomic counter with fixed TTL (NOT renewed per request).
        // Fix #11: previous implementation kept extending TTL, locking out tenants permanently.
        var tenantRateKey = $"chat:ratelimit:tenant:{request.TenantId}";
        var tenantCount = await _cacheService.IncrementWithExpiryAsync(tenantRateKey, TimeSpan.FromSeconds(TenantRateLimitWindowSeconds), ct);
        if (tenantCount > TenantRateLimitMaxRequests)
            throw new InvalidOperationException("Tenant chat rate limit exceeded. Please slow down and try again in a minute.");

        var quotaDecisionResult = await _subscriptionQuotaGate.EnsureChatbotAllowedAsync(
            request.TenantId,
            request.MembershipId,
            2,
            ct);
        if (quotaDecisionResult.IsFailure)
            throw new InvalidOperationException(quotaDecisionResult.Error.Description);

        var tokenQuotaResult = await _subscriptionQuotaGate.EnsureChatbotTokensAvailableAsync(
            request.TenantId,
            request.MembershipId,
            ct);
        if (tokenQuotaResult.IsFailure)
            throw new InvalidOperationException(tokenQuotaResult.Error.Description);

        var accessScope = await _chatAuthorizationService.GetChatAccessScopeAsync(request.MembershipId, ct);
        EnsureTenantAccess(request, accessScope);
        var effectiveDepartmentId = ResolveDepartmentFilter(request.DepartmentId, accessScope);
        var ownerFilter = ResolveOwnerFilter(accessScope);

        // Response cache lookup BEFORE expensive RAG/LLM work.
        // Skip for time-relative queries (answer changes over time).
        if (ChatResponseCacheKey.IsCacheable(request.Query))
        {
            var cacheKey = ChatResponseCacheKey.Build(
                request.TenantId,
                accessScope.Role.ToString(),
                effectiveDepartmentId,
                ownerFilter,
                accessScope.AllowedChunkTypes,
                request.Query);

            ChatResponseCacheEntry? cachedResponse = null;
            try
            {
                cachedResponse = await _cacheService.GetAsync<ChatResponseCacheEntry>(cacheKey, ct);
            }
            catch (Exception ex)
            {
                // Fix #14: cache schema drift treated as miss instead of failing the request.
                _logger.LogWarning(ex, "Chat response cache read failed for key {CacheKey}; treating as miss.", cacheKey);
            }
            if (cachedResponse is not null)
            {
                _logger.LogInformation(
                    "Chat response cache HIT for tenant {TenantId} membership {MembershipId}",
                    request.TenantId, request.MembershipId);

                Guid cachedSessionId;
                ChatSession cachedSession;
                if (request.SessionId.HasValue)
                {
                    cachedSession = await _chatRepository.GetOwnedSessionAsync(request.SessionId.Value, request.TenantId, request.MembershipId, ct)
                        ?? throw new InvalidOperationException("Chat access denied: session was not found for the current membership.");
                    cachedSessionId = cachedSession.Id;
                }
                else
                {
                    cachedSession = ChatSession.Create(request.TenantId, request.MembershipId, $"Chat {DateTime.UtcNow:yyyy-MM-dd HH:mm}");
                    cachedSessionId = cachedSession.Id;
                    await _chatRepository.AddSessionAsync(cachedSession, ct);
                }

                var cachedUserMsg = ChatMessage.Create(cachedSessionId, request.MembershipId, ChatMessageRole.User, request.Query);
                var cachedAsstMsg = ChatMessage.Create(cachedSessionId, request.MembershipId, ChatMessageRole.Assistant, cachedResponse.Answer);
                await _chatRepository.AddMessageAsync(cachedUserMsg, ct);
                await _chatRepository.AddMessageAsync(cachedAsstMsg, ct);
                cachedSession.UpdateTitle(TruncateForTitle(request.Query));
                await _unitOfWork.SaveChangesAsync(ct);

                var cachedCitations = cachedResponse.Citations
                    .Select(c => new ChatCitation(c.ChunkNumber, c.ChunkId, c.DocumentId, c.ChunkType, c.Preview))
                    .ToList();

                // Critical fix #3: still record quota on cache hit so users cannot
                // bypass the message/token cap by polling cached queries.
                await _subscriptionQuotaGate.RecordChatbotUsageAsync(quotaDecisionResult.Value, ct);
                if (cachedResponse.TokenUsage > 0)
                {
                    await _subscriptionQuotaGate.RecordChatbotTokensAsync(
                        request.TenantId,
                        request.MembershipId,
                        cachedResponse.TokenUsage,
                        quotaDecisionResult.Value.PeriodStart,
                        quotaDecisionResult.Value.PeriodEnd,
                        ct);
                }

                return new ChatResponse(
                    cachedResponse.Answer,
                    cachedSessionId,
                    cachedAsstMsg.Id,
                    cachedResponse.DocumentCount,
                    cachedResponse.TokenUsage,
                    cachedCitations);
            }
        }

        Guid actualSessionId;
        ChatSession activeSession;
        if (request.SessionId.HasValue)
        {
            // Single-query 3-key filter eliminates TOCTOU race between session lookup
            // and tenant ownership check.
            activeSession = await _chatRepository.GetOwnedSessionAsync(request.SessionId.Value, request.TenantId, request.MembershipId, ct)
                ?? throw new InvalidOperationException("Chat access denied: session was not found for the current membership.");

            actualSessionId = activeSession.Id;
        }
        else
        {
            activeSession = ChatSession.Create(request.TenantId, request.MembershipId, $"Chat {DateTime.UtcNow:yyyy-MM-dd HH:mm}");
            actualSessionId = activeSession.Id;
        }

        // Optional query rewriting: rewrite for retrieval only; original query
        // is preserved for storage/audit/prompt user-side.
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

        // Hybrid retrieval: vector + keyword in parallel, fused via RRF.
        var vectorTask = _vectorStore.SearchAsync(
            queryEmbedding,
            request.TenantId,
            effectiveDepartmentId,
            ownerFilter,
            accessScope.AllowedChunkTypes,
            20,
            ct);

        var keywordTask = SafeKeywordSearchAsync(
            retrievalQuery, request.TenantId, effectiveDepartmentId, ownerFilter, accessScope.AllowedChunkTypes, 20, ct);

        await Task.WhenAll(vectorTask, keywordTask);
        var vectorChunks = vectorTask.Result;
        var keywordChunks = keywordTask.Result;
        var searchChunks = ReciprocalRankFusion.Fuse(vectorChunks, keywordChunks, 20);

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
        int? totalTokens = null;
        string? promptVersion = null;
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
            // Re-sanitize history (defense-in-depth: even if a bad message landed in DB,
            // it gets neutralized when read back into a prompt).
            var limitedHistory = history
                .TakeLast(MaxHistoryMessages)
                .Select(m => ChatMessage.Create(m.SessionId, m.SenderId, m.Role, ChatPromptSanitizer.Sanitize(m.Content)))
                .ToList();
            var prompt = _promptBuilder.BuildFullPrompt(request.Query, topChunks, accessScope, limitedHistory);
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
                        actualSessionId,
                        string.Join(",", filtered.RedactionTypes));
                }
                assistantResponse = filtered.SanitizedResponse;
            }
        }

        if (!request.SessionId.HasValue)
            await _chatRepository.AddSessionAsync(activeSession, ct);

        var userMessage = ChatMessage.Create(actualSessionId, request.MembershipId, ChatMessageRole.User, request.Query);
        var assistantMessage = ChatMessage.Create(actualSessionId, request.MembershipId, ChatMessageRole.Assistant, assistantResponse);

        await _chatRepository.AddMessageAsync(userMessage, ct);
        await _chatRepository.AddMessageAsync(assistantMessage, ct);

        // Parse citations from the response
        var citations = ChatCitationParser.Parse(assistantResponse, topChunks);

        if (_auditLogRepository is not null)
        {
            var queryHash = ComputeSha256Hash(request.Query);
            var topChunkIds = string.Join(",", topChunks.Select(c => c.Id));
            var auditMetadata = JsonSerializer.Serialize(new
            {
                sessionId = actualSessionId,
                role = accessScope.Role.ToString(),
                queryHash,
                queryLength = request.Query.Length,
                retrievedChunkCount = searchChunks.Count,
                topChunkCount = topChunks.Count,
                topChunkIds,
                tokensUsed = totalTokens,
                promptVersion,
                citationCount = citations.Count,
                effectiveDepartmentId,
                ownerFilter
            }, SerializerOptions);

            var auditLog = AuditLog.Create(
                action: "chat.query",
                entityType: nameof(ChatSession),
                entityId: actualSessionId.ToString(),
                newValue: auditMetadata,
                idTenant: request.TenantId);
            await _auditLogRepository.AddAsync(auditLog, ct);
        }

        activeSession.UpdateTitle(TruncateForTitle(request.Query));
        await _subscriptionQuotaGate.RecordChatbotUsageAsync(quotaDecisionResult.Value, ct);
        if (totalTokens.HasValue && totalTokens.Value > 0)
        {
            await _subscriptionQuotaGate.RecordChatbotTokensAsync(
                request.TenantId,
                request.MembershipId,
                totalTokens.Value,
                quotaDecisionResult.Value.PeriodStart,
                quotaDecisionResult.Value.PeriodEnd,
                ct);
        }
        await _unitOfWork.SaveChangesAsync(ct);

        // Cache the response for future identical queries within the same scope.
        // Skipped for time-relative queries.
        if (ChatResponseCacheKey.IsCacheable(request.Query) && topChunks.Count > 0)
        {
            var cacheKey = ChatResponseCacheKey.Build(
                request.TenantId,
                accessScope.Role.ToString(),
                effectiveDepartmentId,
                ownerFilter,
                accessScope.AllowedChunkTypes,
                request.Query);

            var cacheEntry = new ChatResponseCacheEntry(
                Answer: assistantResponse,
                DocumentCount: topChunks.Count,
                TokenUsage: totalTokens ?? 0,
                Citations: citations.Select(c => new CachedCitation(c.ChunkNumber, c.ChunkId, c.DocumentId, c.ChunkType, c.Preview)).ToList());

            await _cacheService.SetAsync(cacheKey, cacheEntry, TimeSpan.FromMinutes(5), ct);
        }

        return new ChatResponse(
            assistantResponse,
            actualSessionId,
            assistantMessage.Id,
            topChunks.Count,
            totalTokens ?? 0,
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

    public async IAsyncEnumerable<ChatStreamEvent> ChatStreamAsync(
        ChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        // Reuse all auth/quota/RAG logic from ChatAsync by calling it but capture
        // intermediate state. To minimize duplication we run full pipeline,
        // then split the LLM call into a streaming variant.
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
                _logger.LogWarning(
                    "Chat content moderation rejected streamed query for membership {MembershipId}, reason: {Reason}",
                    request.MembershipId, moderationReason);
                throw new InvalidOperationException("Your message was rejected by our content policy. Please rephrase.");
            }
        }

        var rateLimitKey = $"chat:ratelimit:{request.MembershipId}";
        var perUserCountStream = await _cacheService.IncrementWithExpiryAsync(rateLimitKey, TimeSpan.FromSeconds(RateLimitSeconds), ct);
        if (perUserCountStream > 1)
            throw new InvalidOperationException("Rate limit exceeded. Please wait a moment before sending another message.");

        var tenantRateKey = $"chat:ratelimit:tenant:{request.TenantId}";
        var tenantCountStream = await _cacheService.IncrementWithExpiryAsync(tenantRateKey, TimeSpan.FromSeconds(TenantRateLimitWindowSeconds), ct);
        if (tenantCountStream > TenantRateLimitMaxRequests)
            throw new InvalidOperationException("Tenant chat rate limit exceeded. Please slow down and try again in a minute.");

        var quotaDecisionResult = await _subscriptionQuotaGate.EnsureChatbotAllowedAsync(
            request.TenantId, request.MembershipId, 2, ct);
        if (quotaDecisionResult.IsFailure)
            throw new InvalidOperationException(quotaDecisionResult.Error.Description);

        var tokenQuotaResult = await _subscriptionQuotaGate.EnsureChatbotTokensAvailableAsync(
            request.TenantId, request.MembershipId, ct);
        if (tokenQuotaResult.IsFailure)
            throw new InvalidOperationException(tokenQuotaResult.Error.Description);

        var accessScope = await _chatAuthorizationService.GetChatAccessScopeAsync(request.MembershipId, ct);
        EnsureTenantAccess(request, accessScope);
        var effectiveDepartmentId = ResolveDepartmentFilter(request.DepartmentId, accessScope);
        var ownerFilter = ResolveOwnerFilter(accessScope);

        Guid actualSessionId;
        ChatSession activeSession;
        if (request.SessionId.HasValue)
        {
            activeSession = await _chatRepository.GetOwnedSessionAsync(request.SessionId.Value, request.TenantId, request.MembershipId, ct)
                ?? throw new InvalidOperationException("Chat access denied: session was not found for the current membership.");
            actualSessionId = activeSession.Id;
        }
        else
        {
            activeSession = ChatSession.Create(request.TenantId, request.MembershipId, $"Chat {DateTime.UtcNow:yyyy-MM-dd HH:mm}");
            actualSessionId = activeSession.Id;
        }

        var retrievalQueryStream = request.Query;
        if (_queryRewriter is not null)
        {
            var rewritten = await _queryRewriter.RewriteAsync(request.Query, ct);
            if (!string.IsNullOrWhiteSpace(rewritten) && rewritten != request.Query)
                retrievalQueryStream = ChatPromptSanitizer.Sanitize(rewritten);
        }

        var queryEmbedding = await _embeddingService.EmbedAsync(retrievalQueryStream, ct);
        if (queryEmbedding == null || queryEmbedding.Length == 0)
            throw new InvalidOperationException("Failed to generate embedding for query.");

        var vectorTask = _vectorStore.SearchAsync(
            queryEmbedding, request.TenantId, effectiveDepartmentId, ownerFilter,
            accessScope.AllowedChunkTypes, 20, ct);
        var keywordTask = SafeKeywordSearchAsync(
            retrievalQueryStream, request.TenantId, effectiveDepartmentId, ownerFilter, accessScope.AllowedChunkTypes, 20, ct);
        await Task.WhenAll(vectorTask, keywordTask);
        var searchChunks = ReciprocalRankFusion.Fuse(vectorTask.Result, keywordTask.Result, 20);
        EnsureChunksWithinScope(searchChunks, request, accessScope, effectiveDepartmentId, ownerFilter);

        var rerankedResults = await _rerankService.RerankAsync(request.Query, searchChunks, 5, ct);
        var topChunks = rerankedResults.Select(r => r.Chunk).ToList();
        EnsureChunksWithinScope(topChunks, request, accessScope, effectiveDepartmentId, ownerFilter);

        LogRetrievalAudit(actualSessionId, request, accessScope, effectiveDepartmentId, ownerFilter, searchChunks, topChunks);

        // Stream tokens from LLM with sliding-window output filter to redact PII
        // before bytes leave the server (Critical fix #1).
        var fullResponseBuilder = new StringBuilder();
        var streamFilter = _outputFilter is not null ? new StreamingOutputFilter(_outputFilter) : null;
        int? totalTokens = null;
        string? promptVersion = null;

        if (topChunks.Count == 0)
        {
            // Same fallback as non-streaming path
            yield return new ChatStreamEvent(
                ChatStreamEventKind.Token, TokenChunk: AuthorizedNoContextMessage);
            fullResponseBuilder.Append(AuthorizedNoContextMessage);
        }
        else
        {
            var history = await _chatRepository.GetMessagesBySessionAsync(actualSessionId, ct);
            var limitedHistory = history.TakeLast(MaxHistoryMessages)
                .Select(m => ChatMessage.Create(m.SessionId, m.SenderId, m.Role, ChatPromptSanitizer.Sanitize(m.Content)))
                .ToList();
            var prompt = _promptBuilder.BuildFullPrompt(request.Query, topChunks, accessScope, limitedHistory);
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
                        streamFilter.TotalRedactionCount, actualSessionId, string.Join(",", streamFilter.RedactionTypes));
                }
            }
        }

        // Re-run filter on the assembled full response so the persisted assistant message
        // matches what the user saw (and stays redacted in history/audit).
        var assistantResponse = fullResponseBuilder.ToString();
        if (_outputFilter is not null && assistantResponse.Length > 0)
        {
            var filteredFinal = _outputFilter.Sanitize(assistantResponse);
            assistantResponse = filteredFinal.SanitizedResponse;
        }

        // Persist + audit
        if (!request.SessionId.HasValue)
            await _chatRepository.AddSessionAsync(activeSession, ct);

        var userMessage = ChatMessage.Create(actualSessionId, request.MembershipId, ChatMessageRole.User, request.Query);
        var assistantMessage = ChatMessage.Create(actualSessionId, request.MembershipId, ChatMessageRole.Assistant, assistantResponse);
        await _chatRepository.AddMessageAsync(userMessage, ct);
        await _chatRepository.AddMessageAsync(assistantMessage, ct);

        if (_auditLogRepository is not null)
        {
            var queryHash = ComputeSha256Hash(request.Query);
            var auditMetadata = JsonSerializer.Serialize(new
            {
                sessionId = actualSessionId, role = accessScope.Role.ToString(),
                queryHash, queryLength = request.Query.Length,
                retrievedChunkCount = searchChunks.Count, topChunkCount = topChunks.Count,
                tokensUsed = totalTokens, promptVersion, streamed = true
            }, SerializerOptions);
            var auditLog = AuditLog.Create("chat.query.stream", nameof(ChatSession),
                actualSessionId.ToString(), newValue: auditMetadata, idTenant: request.TenantId);
            await _auditLogRepository.AddAsync(auditLog, ct);
        }

        activeSession.UpdateTitle(TruncateForTitle(request.Query));
        await _subscriptionQuotaGate.RecordChatbotUsageAsync(quotaDecisionResult.Value, ct);
        if (totalTokens.HasValue && totalTokens.Value > 0)
        {
            await _subscriptionQuotaGate.RecordChatbotTokensAsync(
                request.TenantId, request.MembershipId, totalTokens.Value,
                quotaDecisionResult.Value.PeriodStart, quotaDecisionResult.Value.PeriodEnd, ct);
        }
        await _unitOfWork.SaveChangesAsync(ct);

        yield return new ChatStreamEvent(
            Kind: ChatStreamEventKind.Complete,
            SessionId: actualSessionId,
            MessageId: assistantMessage.Id,
            DocumentCount: topChunks.Count,
            TokenUsage: totalTokens ?? 0,
            CompleteAnswer: assistantResponse);
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
}
