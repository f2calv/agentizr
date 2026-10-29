namespace CasCap.Client.Constants;

/// <summary>Same-origin, app-authorized document catalogue routes.</summary>
public static class DocumentCatalogueRoutes
{
    /// <summary>Gets the paged document and current-job catalogue route.</summary>
    public const string Documents = AdministrationRoutes.App + "/documents";

    /// <summary>Gets the application-scoped durable source synchronization route.</summary>
    public const string Synchronization = Documents + "/synchronization";
}
