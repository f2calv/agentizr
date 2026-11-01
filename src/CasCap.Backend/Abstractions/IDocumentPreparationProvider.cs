namespace CasCap.Abstractions;

/// <summary>Paid-provider boundary; the caller must durably reserve every submission and persist its receipts.</summary>
internal interface IDocumentPreparationProvider
{
    /// <summary>Verifies the deployment's endpoint, public model and immutable revision before paid operations.</summary>
    public Task VerifyModelAsync(DocumentPreparationPlan plan, CancellationToken cancellationToken);

    /// <summary>Submits layout analysis once; exceptions can represent unknown paid outcomes.</summary>
    public Task<Uri> SubmitLayoutAsync(
        DocumentPreparationPlan plan, byte[] pdf, int[] pages, CancellationToken cancellationToken);

    /// <summary>Reads an existing operation once, without submitting another analysis.</summary>
    public Task<DocumentLayoutPollResult> PollLayoutAsync(
        DocumentPreparationPlan plan, Uri operation, int[] pages, CancellationToken cancellationToken);

    /// <summary>Executes the actual stateless framework agent and observes its response before validation.</summary>
    public Task ExecutePageAsync(
        DocumentPreparationPageRequest request,
        Func<AgentStructuredResponse, CancellationToken, ValueTask> observer,
        CancellationToken cancellationToken);
}
