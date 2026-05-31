using FinFlow.Application.Chat.Cascade;
using FinFlow.Application.Chat.Interfaces;

namespace FinFlow.UnitTests.Application.Chat.Cascade;

public sealed class IntentEvalMetrics
{
    public sealed class IntentBucket
    {
        public string Key { get; }
        public int Support { get; private set; }
        public int TruePositive { get; private set; }
        public int FalsePositive { get; private set; }
        public int FalseNegative { get; private set; }

        public IntentBucket(string key) => Key = key;

        public void RecordExpected() => Support++;
        public void RecordTruePositive() => TruePositive++;
        public void RecordFalsePositive() => FalsePositive++;
        public void RecordFalseNegative() => FalseNegative++;

        public double Precision => TruePositive + FalsePositive == 0 ? 1.0 : (double)TruePositive / (TruePositive + FalsePositive);
        public double Recall => TruePositive + FalseNegative == 0 ? 1.0 : (double)TruePositive / (TruePositive + FalseNegative);
        public double F1 => Precision + Recall == 0 ? 0 : 2 * Precision * Recall / (Precision + Recall);
    }

    private readonly Dictionary<string, IntentBucket> _byMode = new();
    private readonly List<int> _latenciesMs = new();
    public int Total { get; private set; }
    public int Correct { get; private set; }
    public int ScopeWidened { get; private set; }
    public int RagDefaultAbstain { get; private set; }

    public void Record(
        ChatExecutionMode expectedMode,
        ChatExecutionMode actualMode,
        ChatScopeConfidence expectedScope,
        ChatScopeConfidence actualScope,
        int latencyMs,
        string classifierStage)
    {
        Total++;
        _latenciesMs.Add(latencyMs);

        var expectedBucket = GetBucket(expectedMode.ToString());
        expectedBucket.RecordExpected();

        if (expectedMode == actualMode)
        {
            Correct++;
            expectedBucket.RecordTruePositive();
        }
        else
        {
            expectedBucket.RecordFalseNegative();
            GetBucket(actualMode.ToString()).RecordFalsePositive();
        }

        if (IsScopeWidened(expectedScope, actualScope))
            ScopeWidened++;

        if (classifierStage == ClassifierStages.DefaultRag)
            RagDefaultAbstain++;
    }

    public double WeightedF1
    {
        get
        {
            if (Total == 0) return 0;
            double sum = 0;
            foreach (var bucket in _byMode.Values)
                sum += bucket.F1 * bucket.Support;
            return sum / Total;
        }
    }

    public double Accuracy => Total == 0 ? 0 : (double)Correct / Total;
    public double ScopeWidenedRate => Total == 0 ? 0 : (double)ScopeWidened / Total;
    public double RagDefaultRate => Total == 0 ? 0 : (double)RagDefaultAbstain / Total;

    public int P95LatencyMs
    {
        get
        {
            if (_latenciesMs.Count == 0) return 0;
            var sorted = _latenciesMs.OrderBy(x => x).ToArray();
            var idx = Math.Min(sorted.Length - 1, (int)Math.Ceiling(0.95 * sorted.Length) - 1);
            return sorted[Math.Max(0, idx)];
        }
    }

    public IReadOnlyDictionary<string, IntentBucket> Buckets => _byMode;

    private IntentBucket GetBucket(string key)
    {
        if (!_byMode.TryGetValue(key, out var bucket))
        {
            bucket = new IntentBucket(key);
            _byMode[key] = bucket;
        }
        return bucket;
    }

    private static bool IsScopeWidened(ChatScopeConfidence expected, ChatScopeConfidence actual)
    {
        // Widening = moving toward less restrictive: Forbidden -> Ambiguous -> SafeInferred -> Explicit
        return Rank(actual) > Rank(expected);

        static int Rank(ChatScopeConfidence c) => c switch
        {
            ChatScopeConfidence.Forbidden => 0,
            ChatScopeConfidence.Ambiguous => 1,
            ChatScopeConfidence.SafeInferred => 2,
            ChatScopeConfidence.Explicit => 3,
            _ => 0
        };
    }
}
