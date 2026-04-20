namespace FinFlow.Infrastructure.Ocr;

public sealed class OcrOptions
{
    public const string SectionName = "Ocr";

    public string ActiveProvider { get; init; } = "Groq";
}
