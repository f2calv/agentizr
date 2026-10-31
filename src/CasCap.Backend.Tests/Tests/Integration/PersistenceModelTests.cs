using Microsoft.EntityFrameworkCore;

namespace CasCap.Tests.Integration;

/// <summary>Verifies backend migration discovery and snapshot consistency without database access.</summary>
[Trait("Category", "Integration")]
public sealed class PersistenceModelTests
{
    [Fact]
    public void BackendAssembly_PreservesMigrationsAndModel()
    {
        using var context = new AgentRuntimeDbContextDesignTimeFactory().CreateDbContext([]);
        string[] expected =
        [
            "20261006043028_InitialAgentDefinitions",
            "20261007033209_AddDefinitionActivationHistory",
            "20261013163205_AddDefinitionDeactivation",
            "20261022011050_DurableRuntimeState",
            "20261024022317_AddDataProtectionKeys",
            "20261025123301_DocumentIngestionAdmission",
            "20261025164206_DocumentIngestionProcessing",
            "20261026041415_DocumentPreparedGenerations",
            "20261027125842_DocumentHybridPreparationOperations",
            "20261028120307_DocumentMarkdownReviewGenerations",
            "20261029104131_AddDocumentSynchronization",
            "20261030054010_DocumentChunkReviews",
            "20261030094946_DocumentApprovals",
            "20261030121900_DocumentVectorIndexing",
        ];

        Assert.Equal("CasCap.Backend", typeof(AgentRuntimeDbContext).Assembly.GetName().Name);
        Assert.Equal(expected, context.Database.GetMigrations());
        Assert.False(context.Database.HasPendingModelChanges());
    }
}
