namespace FinFlow.Domain.Chat;

public enum EntityType
{
    PERSON,
    ORGANIZATION,
    MONEY,
    DATE,
    LOCATION,
    DOCUMENT,
    CONCEPT,
    ACTION,
    DEPARTMENT,
    VENDOR,
    EXPENSE,
    BUDGET,
    UNKNOWN,
    Person = PERSON,
    Organization = ORGANIZATION,
    Money = MONEY,
    Date = DATE,
    Location = LOCATION,
    Document = DOCUMENT,
    Concept = CONCEPT,
    Action = ACTION,
    Department = DEPARTMENT,
    Vendor = VENDOR,
    Expense = EXPENSE,
    Budget = BUDGET,
    Unknown = UNKNOWN
}

public enum PendingClarificationKind
{
    Scope
}

public sealed class PendingClarification
{
    public PendingClarificationKind Kind { get; private set; }
    public string OriginalQuery { get; private set; } = string.Empty;
    public string Prompt { get; private set; } = string.Empty;
    public string? IntentReason { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private PendingClarification() { }

    public static PendingClarification Scope(string originalQuery, string prompt, string? intentReason)
    {
        return new PendingClarification
        {
            Kind = PendingClarificationKind.Scope,
            OriginalQuery = originalQuery,
            Prompt = prompt,
            IntentReason = intentReason,
            CreatedAt = DateTime.UtcNow
        };
    }
}

public class TrackedEntity
{
    public Guid Id { get; private set; }
    public string CanonicalName { get; private set; }
    public List<string> Aliases { get; private set; } = [];
    public EntityType Type { get; private set; }
    public Dictionary<string, object> Attributes { get; private set; } = [];
    public int FirstMentionTurn { get; private set; }
    public int LastReferenceTurn { get; private set; }
    public int MentionCount { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime LastAccessedAt { get; private set; }
    public int TtlSeconds { get; private set; }

    private TrackedEntity() { }

    public static TrackedEntity Create(
        string canonicalName,
        EntityType type,
        int turnNumber,
        int ttlSeconds = 86400)
    {
        return new TrackedEntity
        {
            Id = Guid.NewGuid(),
            CanonicalName = canonicalName,
            Type = type,
            FirstMentionTurn = turnNumber,
            LastReferenceTurn = turnNumber,
            MentionCount = 1,
            CreatedAt = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow,
            TtlSeconds = ttlSeconds
        };
    }

    public void AddAlias(string alias)
    {
        if (!Aliases.Contains(alias))
            Aliases.Add(alias);
    }

    public void RecordReference(int turnNumber)
    {
        LastReferenceTurn = turnNumber;
        LastAccessedAt = DateTime.UtcNow;
        MentionCount++;
    }

    public void SetAttribute(string key, object value)
    {
        Attributes[key] = value;
    }

    public void RefreshTtl(int ttlSeconds)
    {
        LastAccessedAt = DateTime.UtcNow;
        TtlSeconds = ttlSeconds;
    }

    public bool IsExpired()
    {
        return DateTime.UtcNow > LastAccessedAt.AddSeconds(TtlSeconds);
    }

    public static TrackedEntity ExtractEntity(
        string canonicalName,
        EntityType type,
        int turnNumber,
        List<string>? aliases = null,
        Dictionary<string, object>? attributes = null,
        int ttlSeconds = 86400)
    {
        var entity = Create(canonicalName, type, turnNumber, ttlSeconds);
        if (aliases != null)
        {
            foreach (var alias in aliases)
            {
                entity.AddAlias(alias);
            }
        }
        if (attributes != null)
        {
            foreach (var attr in attributes)
            {
                entity.SetAttribute(attr.Key, attr.Value);
            }
        }
        return entity;
    }
}

public class ConversationContext
{
    public Guid Id { get; private set; }
    public Guid SessionId { get; private set; }
    public List<TrackedEntity> Entities { get; private set; } = [];
    public IntentStack IntentStack { get; private set; }
    public PendingClarification? PendingClarification { get; private set; }
    public ConversationTurnState? LastTurn { get; private set; }
    public int TurnCount { get; private set; }
    public string? CompressedSummary { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime LastAccessedAt { get; private set; }

    private ConversationContext()
    {
        IntentStack = new IntentStack();
    }

    public static ConversationContext Create(Guid sessionId)
    {
        return new ConversationContext
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            IntentStack = new IntentStack(),
            TurnCount = 0,
            CreatedAt = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow
        };
    }

    public void IncrementTurn()
    {
        TurnCount++;
        LastAccessedAt = DateTime.UtcNow;
    }

    public void AddEntity(TrackedEntity entity, int? turnNumber = null)
    {
        if (turnNumber.HasValue)
        {
            entity.RecordReference(turnNumber.Value);
        }
        Entities.Add(entity);
    }

    public TrackedEntity? FindEntity(string name)
    {
        var normalized = name.ToLowerInvariant().Trim();

        // Exact canonical match
        var exact = Entities.FirstOrDefault(e =>
            e.CanonicalName.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact;

        // Alias match
        var alias = Entities.FirstOrDefault(e =>
            e.Aliases.Any(a => a.Equals(normalized, StringComparison.OrdinalIgnoreCase)));
        if (alias != null) return alias;

        // Contains match (partial)
        var partial = Entities.FirstOrDefault(e =>
            e.CanonicalName.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
            e.Aliases.Any(a => a.Contains(normalized, StringComparison.OrdinalIgnoreCase)));
        return partial;
    }

    public T? FindEntity<T>(Func<T, bool> predicate) where T : TrackedEntity
    {
        return Entities.OfType<T>().FirstOrDefault(predicate);
    }

    public TrackedEntity? FindEntityByType(EntityType type)
    {
        return Entities
            .Where(e => e.Type == type)
            .OrderByDescending(e => e.LastReferenceTurn)
            .FirstOrDefault();
    }

    public void AddAliasToEntity(Guid entityId, string alias)
    {
        var entity = Entities.FirstOrDefault(e => e.Id == entityId);
        entity?.AddAlias(alias);
    }

    public void SetCompressedSummary(string summary)
    {
        CompressedSummary = summary;
    }

    public void SetPendingClarification(PendingClarification clarification)
    {
        PendingClarification = clarification;
        LastAccessedAt = DateTime.UtcNow;
    }

    public void ClearPendingClarification()
    {
        PendingClarification = null;
        LastAccessedAt = DateTime.UtcNow;
    }

    public void SetLastTurn(ConversationTurnState turn)
    {
        LastTurn = turn;
        LastAccessedAt = DateTime.UtcNow;
    }

    public List<TrackedEntity> GetActiveEntities()
    {
        return Entities.Where(e => !e.IsExpired()).ToList();
    }

    public void CleanupExpiredEntities()
    {
        Entities = GetActiveEntities();
    }
}

public sealed class ConversationTurnState
{
    public string OriginalQuery { get; private set; } = string.Empty;
    public string EffectiveQuery { get; private set; } = string.Empty;
    public string ExecutionMode { get; private set; } = string.Empty;
    public string IntentFamily { get; private set; } = string.Empty;
    public string ReportingTask { get; private set; } = string.Empty;
    public string IntentReason { get; private set; } = string.Empty;
    public string ScopeConfidence { get; private set; } = string.Empty;
    public string AnswerSource { get; private set; } = string.Empty;
    public DateOnly? PeriodFrom { get; private set; }
    public DateOnly? PeriodTo { get; private set; }
    public DateTime CreatedAt { get; private set; }

    private ConversationTurnState() { }

    public static ConversationTurnState Create(
        string originalQuery,
        string effectiveQuery,
        string executionMode,
        string intentFamily,
        string reportingTask,
        string intentReason,
        string scopeConfidence,
        string answerSource,
        DateOnly? periodFrom,
        DateOnly? periodTo)
    {
        return new ConversationTurnState
        {
            OriginalQuery = originalQuery,
            EffectiveQuery = effectiveQuery,
            ExecutionMode = executionMode,
            IntentFamily = intentFamily,
            ReportingTask = reportingTask,
            IntentReason = intentReason,
            ScopeConfidence = scopeConfidence,
            AnswerSource = answerSource,
            PeriodFrom = periodFrom,
            PeriodTo = periodTo,
            CreatedAt = DateTime.UtcNow
        };
    }
}

public enum LlmIntentType
{
    Unknown,
    QueryExpense,
    QueryBudget,
    QueryReport,
    CreateExpense,
    ApproveExpense,
    CancelExpense,
    UpdateExpense,
    QueryDepartment,
    QueryVendor,
    QueryTrend,
    Greeting,
    Clarification,
    Pivot,
    Continuation
}

public static class LlmIntentTypeExtensions
{
    public static LlmIntentType ParseIntentType(string intentType)
    {
        if (string.IsNullOrWhiteSpace(intentType))
            return LlmIntentType.Unknown;

        var normalized = intentType.Trim().ToLowerInvariant().Replace("_", "").Replace("-", "");

        return normalized switch
        {
            "query_expense" or "queryexpense" => LlmIntentType.QueryExpense,
            "query_budget" or "querybudget" => LlmIntentType.QueryBudget,
            "query_report" or "queryreport" => LlmIntentType.QueryReport,
            "create_expense" or "createexpense" => LlmIntentType.CreateExpense,
            "approve_expense" or "approveexpense" => LlmIntentType.ApproveExpense,
            "cancel_expense" or "cancelexpense" => LlmIntentType.CancelExpense,
            "update_expense" or "updateexpense" => LlmIntentType.UpdateExpense,
            "query_department" or "querydepartment" => LlmIntentType.QueryDepartment,
            "query_vendor" or "queryvendor" => LlmIntentType.QueryVendor,
            "query_trend" or "querytrend" => LlmIntentType.QueryTrend,
            "greeting" => LlmIntentType.Greeting,
            "clarification" => LlmIntentType.Clarification,
            "pivot" => LlmIntentType.Pivot,
            "continuation" => LlmIntentType.Continuation,
            _ => LlmIntentType.Unknown
        };
    }

    public static string ToDisplayString(this LlmIntentType intentType)
    {
        return intentType switch
        {
            LlmIntentType.QueryExpense => "Query Expense",
            LlmIntentType.QueryBudget => "Query Budget",
            LlmIntentType.QueryReport => "Query Report",
            LlmIntentType.CreateExpense => "Create Expense",
            LlmIntentType.ApproveExpense => "Approve Expense",
            LlmIntentType.CancelExpense => "Cancel Expense",
            LlmIntentType.UpdateExpense => "Update Expense",
            LlmIntentType.QueryDepartment => "Query Department",
            LlmIntentType.QueryVendor => "Query Vendor",
            LlmIntentType.QueryTrend => "Query Trend",
            LlmIntentType.Greeting => "Greeting",
            LlmIntentType.Clarification => "Clarification",
            LlmIntentType.Pivot => "Pivot",
            LlmIntentType.Continuation => "Continuation",
            _ => "Unknown"
        };
    }
}

public enum IntentState
{
    Initiated,
    InProgress,
    AwaitingConfirmation,
    Suspended,
    Completed,
    Cancelled
}

public class IntentFrame
{
    public Guid Id { get; private set; }
    public LlmIntentType IntentType { get; private set; }
    public string RawIntentType { get; private set; }
    public Dictionary<string, object?> Slots { get; private set; } = [];
    public IntentState State { get; private set; }
    public string? PivotTrigger { get; private set; }
    public bool IsPivot { get; private set; }
    public bool IsClarification { get; private set; }
    public bool IsContinuation { get; private set; }
    public string? Confidence { get; private set; }
    public string? Reasoning { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? SuspendedAt { get; private set; }

    private IntentFrame() { }

    public static IntentFrame Create(string intentType)
    {
        var parsed = LlmIntentTypeExtensions.ParseIntentType(intentType);
        return new IntentFrame
        {
            Id = Guid.NewGuid(),
            IntentType = parsed,
            RawIntentType = intentType,
            State = IntentState.Initiated,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static IntentFrame CreateFromLlm(string intentType, bool isPivot, bool isClarification, bool isContinuation, string? confidence = null, string? reasoning = null)
    {
        var parsed = LlmIntentTypeExtensions.ParseIntentType(intentType);
        return new IntentFrame
        {
            Id = Guid.NewGuid(),
            IntentType = parsed,
            RawIntentType = intentType,
            State = IntentState.Initiated,
            IsPivot = isPivot,
            IsClarification = isClarification,
            IsContinuation = isContinuation,
            Confidence = confidence,
            Reasoning = reasoning,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void SetState(IntentState state)
    {
        State = state;
        if (state == IntentState.Suspended)
            SuspendedAt = DateTime.UtcNow;
    }

    public void SetSlot(string key, object? value)
    {
        Slots[key] = value;
    }

    public object? GetSlot(string key)
    {
        return Slots.TryGetValue(key, out var value) ? value : null;
    }

    public void Suspend(string trigger)
    {
        State = IntentState.Suspended;
        PivotTrigger = trigger;
        SuspendedAt = DateTime.UtcNow;
    }

    public void Resume()
    {
        State = IntentState.InProgress;
    }

    public void Complete()
    {
        State = IntentState.Completed;
    }
}

public class IntentStack
{
    private readonly List<IntentFrame> _stack = [];
    private bool _isLocked;
    private string? _lockReason;

    public bool IsLocked => _isLocked;
    public string? LockReason => _lockReason;

    public int Count => _stack.Count;
    public IReadOnlyList<IntentFrame> Frames => _stack.AsReadOnly();

    public IntentFrame? GetActive()
    {
        return _stack.LastOrDefault();
    }

    public void Push(IntentFrame frame)
    {
        _stack.Add(frame);
    }

    public IntentFrame? Pop()
    {
        if (_stack.Count > 0)
        {
            var frame = _stack[^1];
            _stack.RemoveAt(_stack.Count - 1);
            return frame;
        }
        return null;
    }

    public IntentFrame? Peek()
    {
        return _stack.LastOrDefault();
    }

    public void Lock(string reason)
    {
        _isLocked = true;
        _lockReason = reason;
    }

    public void Unlock()
    {
        _isLocked = false;
        _lockReason = null;
    }

    public void SuspendCurrent(string trigger)
    {
        var current = Peek();
        current?.Suspend(trigger);
    }

    public IntentFrame? Resume()
    {
        for (int i = _stack.Count - 1; i >= 0; i--)
        {
            if (_stack[i].State == IntentState.Suspended)
            {
                _stack[i].Resume();
                return _stack[i];
            }
        }
        return null;
    }

    public List<IntentFrame> GetSuspended()
    {
        return _stack.Where(f => f.State == IntentState.Suspended).ToList();
    }

    public List<string> GetSuspendedSummary()
    {
        return _stack
            .Where(f => f.State == IntentState.Suspended)
            .Select(f => $"- {f.RawIntentType} (triggered by: {f.PivotTrigger})")
            .ToList();
    }

    public void Clear()
    {
        _stack.Clear();
        _isLocked = false;
        _lockReason = null;
    }
}
