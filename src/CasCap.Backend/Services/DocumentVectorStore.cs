#if DEBUG
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CasCap.Services;

/// <summary>Owns parameterized native pgvector DML; the vector table is explicitly SQL-owned by the indexing migration.</summary>
internal static class DocumentVectorStore
{
    /// <summary>Stages one complete batch in the caller's receipt transaction; nothing here publishes it.</summary>
    public static async Task StageAsync(
        AgentRuntimeDbContext db, DocumentEmbeddingBatch batch, DocumentEmbeddingResult result, CancellationToken token)
    {
        var payload = JsonSerializer.Serialize(batch.Inputs.Select((input, index) => new
        {
            ordinal = input.Chunk.Ordinal,
            input_sha256 = input.InputSha256,
            vector_text = JsonSerializer.Serialize(result.Vectors[index]),
        }));
        var rows = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO agent_runtime.document_index_vectors (tenant_id, job_id, ordinal, input_sha256, embedding)
            SELECT {batch.Claim.Manifest.TenantId}, {batch.Claim.JobId}, item.ordinal, item.input_sha256, CAST(item.vector_text AS vector)
            FROM jsonb_to_recordset(CAST({payload} AS jsonb)) AS item(ordinal integer, input_sha256 text, vector_text text)
            """, token);
        if (rows != batch.Inputs.Count)
            throw new InvalidDataException("The vector batch was not completely staged.");
    }

    /// <summary>Checks exact included ordinals, input identities and native dimensions without reading vector values into diagnostics.</summary>
    public static async Task ValidateAsync(AgentRuntimeDbContext db, DocumentIndexingClaim claim, CancellationToken token)
    {
        var rows = await db.Database.SqlQuery<DocumentVectorReceipt>($"""
            SELECT ordinal AS "Ordinal", input_sha256 AS "InputSha256", vector_dims(embedding) AS "Dimensions"
            FROM agent_runtime.document_index_vectors
            WHERE tenant_id = {claim.Manifest.TenantId} AND job_id = {claim.JobId}
            ORDER BY ordinal LIMIT {claim.Manifest.Inputs.Count + 1}
            """).ToArrayAsync(token);
        if (rows.Length != claim.Manifest.Inputs.Count || !rows.Select(row => (row.Ordinal, row.InputSha256, row.Dimensions))
            .SequenceEqual(claim.Manifest.Inputs.Select(input =>
                (input.Chunk.Ordinal, input.InputSha256, claim.Manifest.Profile.Dimensions))))
            throw new InvalidDataException("The staged vector generation does not match its exact approved inputs.");
    }
}
#endif
