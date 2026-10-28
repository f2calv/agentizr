namespace CasCap.Models;

/// <summary>A transcription on its original one-based physical page; contains no usage or review annotations.</summary>
internal sealed record DocumentMarkdownPage(int PageNumber, string Markdown);
