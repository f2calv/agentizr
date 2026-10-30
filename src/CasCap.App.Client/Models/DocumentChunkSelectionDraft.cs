namespace CasCap.Client.Models;

/// <summary>Tracks local selection edits without changing candidate content or granting approval.</summary>
public sealed class DocumentChunkSelectionDraft(IEnumerable<int> ordinals, DocumentChunkReviewResponse? saved)
{
    private readonly HashSet<int> _ordinals = ordinals.ToHashSet();
    private readonly Dictionary<int, string?> _baseline = saved?.Exclusions.ToDictionary(item => item.Ordinal, item => item.Reason) ?? [];
    private readonly Dictionary<int, string?> _exclusions = saved?.Exclusions.ToDictionary(item => item.Ordinal, item => item.Reason) ?? [];

    /// <summary>Gets the number of selected candidates.</summary>
    public int IncludedCount => _ordinals.Count - _exclusions.Count;

    /// <summary>Gets the number of excluded candidates.</summary>
    public int ExcludedCount => _exclusions.Count;

    /// <summary>Gets whether local decisions differ from the loaded review.</summary>
    public bool IsDirty => _exclusions.Count != _baseline.Count
        || _exclusions.Any(item => !_baseline.TryGetValue(item.Key, out var reason) || item.Value != reason);

    /// <summary>Checks whether a candidate is excluded.</summary>
    public bool IsExcluded(int ordinal) => _exclusions.ContainsKey(ordinal);

    /// <summary>Gets the separate optional exclusion explanation.</summary>
    public string? Reason(int ordinal) => _exclusions.GetValueOrDefault(ordinal);

    /// <summary>Changes candidate inclusion without granting search eligibility.</summary>
    public void SetIncluded(int ordinal, bool included)
    {
        if (!_ordinals.Contains(ordinal))
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        if (included)
            _exclusions.Remove(ordinal);
        else
            _exclusions.TryAdd(ordinal, null);
    }

    /// <summary>Changes the explanation for an excluded candidate.</summary>
    public void SetReason(int ordinal, string? reason)
    {
        if (!_exclusions.ContainsKey(ordinal))
            throw new InvalidOperationException("Only excluded candidates can carry an exclusion reason.");
        _exclusions[ordinal] = reason;
    }

    /// <summary>Returns canonically ordered local exclusion decisions.</summary>
    public IReadOnlyList<DocumentChunkExclusionRequest> Exclusions() => _exclusions.OrderBy(item => item.Key)
        .Select(item => new DocumentChunkExclusionRequest { Ordinal = item.Key, Reason = item.Value }).ToArray();
}
