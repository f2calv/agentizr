namespace CasCap.Constants;

/// <summary>Defines the source roles eligible for explicit whole-block exclusion from vector input.</summary>
internal static class DocumentMarkdownSourceRoles
{
    /// <summary>Identifies reviewed running page furniture above the body.</summary>
    public const string RunningHeader = "running-header";

    /// <summary>Identifies reviewed running page furniture below the body.</summary>
    public const string RunningFooter = "running-footer";

    /// <summary>Identifies contents/navigation retained as metadata rather than embedding text.</summary>
    public const string Navigation = "navigation";
}
