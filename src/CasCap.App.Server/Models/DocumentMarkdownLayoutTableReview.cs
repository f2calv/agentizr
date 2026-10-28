namespace CasCap.Models;

/// <summary>Projects an exact headerless layout table as independent entries in an explicit reading order.</summary>
internal sealed record DocumentMarkdownLayoutTableReview(int PageNumber, int StartOffset, int EndOffset, string ReadingOrder, string Reason);
