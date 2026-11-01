namespace CasCap.Client.Constants;

/// <summary>Supported document classification and CEFR values for reviewed readiness assessments.</summary>
public static class DocumentApprovalValues
{
    /// <summary>Gets the device-manual classification; its visual gate is not yet implemented.</summary>
    public const string DeviceManual = nameof(DeviceManual);

    /// <summary>Gets the vocabulary-list classification, requiring a reviewed CEFR level.</summary>
    public const string Vocabulary = nameof(Vocabulary);

    /// <summary>Gets the exam-paper classification, requiring a reviewed CEFR level.</summary>
    public const string ExamPaper = nameof(ExamPaper);

    /// <summary>Gets the supported document classifications.</summary>
    public static IReadOnlyList<string> DocumentTypes { get; } = [DeviceManual, Vocabulary, ExamPaper];

    /// <summary>Gets the cumulative-level vocabulary used by future restricted retrieval.</summary>
    public static IReadOnlyList<string> CefrLevels { get; } = ["A1", "A2", "B1", "B2", "C1", "C2"];
}
