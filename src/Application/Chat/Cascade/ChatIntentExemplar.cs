using FinFlow.Application.Chat.Interfaces;
using FinFlow.Domain.Abstractions;

namespace FinFlow.Application.Chat.Cascade;

public class ChatIntentExemplar : Entity
{
    public string ExemplarText { get; private set; } = string.Empty;
    public string Language { get; private set; } = "vi";
    public ChatExecutionMode IntentMode { get; private set; }
    public ChatIntentFamily IntentFamily { get; private set; }
    public ChatReportingTask IntentTask { get; private set; }
    public double Weight { get; private set; } = 1.0;
    public float[] Embedding { get; private set; } = Array.Empty<float>();
    public string EmbeddingModel { get; private set; } = string.Empty;
    public Guid? IdTenant { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private ChatIntentExemplar() { }

    public static ChatIntentExemplar Create(
        string exemplarText,
        string language,
        ChatExecutionMode intentMode,
        ChatIntentFamily intentFamily,
        ChatReportingTask intentTask,
        double weight,
        float[] embedding,
        string embeddingModel,
        Guid? idTenant = null)
    {
        if (string.IsNullOrWhiteSpace(exemplarText))
            throw new ArgumentException("Exemplar text required.", nameof(exemplarText));
        if (embedding is null || embedding.Length == 0)
            throw new ArgumentException("Embedding required.", nameof(embedding));

        return new ChatIntentExemplar
        {
            Id = Guid.NewGuid(),
            ExemplarText = exemplarText,
            Language = string.IsNullOrWhiteSpace(language) ? "vi" : language,
            IntentMode = intentMode,
            IntentFamily = intentFamily,
            IntentTask = intentTask,
            Weight = weight <= 0 ? 1.0 : weight,
            Embedding = embedding,
            EmbeddingModel = embeddingModel,
            IdTenant = idTenant,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Reembed(float[] embedding, string embeddingModel)
    {
        if (embedding is null || embedding.Length == 0)
            throw new ArgumentException("Embedding required.", nameof(embedding));
        Embedding = embedding;
        EmbeddingModel = embeddingModel;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
