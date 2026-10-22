using CasCap.Constants;
using CasCap.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace CasCap.Services;

/// <summary>Appends debugging activity directly to PostgreSQL, independently of session reset and compaction.</summary>
public sealed class AgentRunHistoryService(
    IOptions<AgentRuntimeConfig> runtimeConfig,
    TimeProvider timeProvider,
    IDbContextFactory<AgentRuntimeDbContext> dbContextFactory,
    ITenantContext tenantContext,
    AgentRuntimeMetrics metrics)
{
    private static readonly JsonSerializerOptions _historyJsonOptions = new(JsonSerializerOptions.Web)
    {
        Converters = { new OmittedBinaryConverter() },
    };
    private readonly HashSet<string> _secrets = new(StringComparer.Ordinal);
    private readonly Lock _secretsLock = new();

    /// <summary>Adds an exact credential value to the redaction set without storing it.</summary>
    public void ExcludeSecret(string? secret)
    {
        if (!string.IsNullOrEmpty(secret))
            lock (_secretsLock)
                _secrets.Add(secret);
    }

    /// <summary>Commits the turn start before model or tool work begins.</summary>
    public async ValueTask<Guid?> StartAsync(
        AgentExecutionRequest request, AgentDefinition definition, string? modelName, CancellationToken cancellationToken)
    {
        if (!runtimeConfig.Value.ConversationHistoryEnabled)
            return null;
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        // The coordinator already owns the session lock, so no earlier Started turn can still own it.
        var interrupted = await db.AgentRuns.Where(run => run.TenantId == tenantContext.TenantId
                && run.AgentName == request.AgentName && run.SessionId == request.SessionId
                && run.Status == AgentRunStatuses.Started)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(run => run.Status, AgentRunStatuses.Interrupted)
                .SetProperty(run => run.CompletedAtUtc, timeProvider.GetUtcNow()), cancellationToken);
        if (interrupted > 0)
            metrics.RecordHa(tenantContext.TenantId, "history.recover", "interrupted", count: interrupted);
        var id = Guid.NewGuid();
        db.AgentRuns.Add(new AgentRunEntity
        {
            Id = id,
            TenantId = tenantContext.TenantId,
            AgentName = request.AgentName,
            SessionId = request.SessionId,
            DefinitionVersion = definition.Version,
            ModelName = modelName,
            Status = AgentRunStatuses.Started,
            StartedAtUtc = timeProvider.GetUtcNow(),
        });
        db.AgentRunActivities.Add(CreateActivity(id, new AgentRunActivity
        {
            Kind = "user-message",
            AgentName = request.AgentName,
            Data = new { Text = request.Input, BinaryInputOmitted = request.BinaryContent is not null },
        }));
        await SaveChangesAsync(db, "history.start", "started", cancellationToken);
        return id;
    }

    /// <summary>Commits one activity before allowing execution to proceed.</summary>
    public async ValueTask AppendAsync(Guid runId, AgentRunActivity activity, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.AgentRunActivities.Add(CreateActivity(runId, activity));
        await SaveChangesAsync(db, "history.activity", "appended", cancellationToken);
    }

    /// <summary>Records a terminal outcome even when the caller's request has been cancelled.</summary>
    public async ValueTask FinishAsync(Guid runId, AgentExecutionResult? result, Exception? error)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var db = await dbContextFactory.CreateDbContextAsync(timeout.Token);
        var run = await db.AgentRuns.SingleAsync(
            run => run.Id == runId && run.TenantId == tenantContext.TenantId, timeout.Token);
        run.Status = error switch
        {
            OperationCanceledException => AgentRunStatuses.Cancelled,
            not null => AgentRunStatuses.Failed,
            _ => AgentRunStatuses.Completed,
        };
        run.CompletedAtUtc = timeProvider.GetUtcNow();
        run.ErrorType = error?.GetType().FullName;
        if (result is not null)
            run.SummaryJson = Sanitize(new
            {
                result.OutputText,
                result.ModelName,
                result.DefinitionVersion,
                result.Diagnostics?.Elapsed,
                result.Diagnostics?.FinishReason,
                InputTokens = result.Diagnostics?.Usage?.InputTokenCount,
                OutputTokens = result.Diagnostics?.Usage?.OutputTokenCount,
                result.Diagnostics?.ToolCallCount,
                Events = result.Events.Select(activity => new
                {
                    activity.Type,
                    activity.AgentName,
                    activity.Depth,
                    activity.ModelName,
                    activity.Elapsed,
                    activity.InputMessageCount,
                    activity.OutputMessageCount,
                    activity.ToolMessagesDropped,
                    activity.WindowMessagesTrimmed,
                }),
            });
        await SaveChangesAsync(db, "history.finish", run.Status.ToLowerInvariant(), timeout.Token);
    }

    private async Task SaveChangesAsync(AgentRuntimeDbContext db, string operation, string outcome, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            metrics.RecordHa(tenantContext.TenantId, operation, outcome);
        }
        catch
        {
            metrics.RecordHa(tenantContext.TenantId, operation, "error");
            throw;
        }
    }

    private AgentRunActivityEntity CreateActivity(Guid id, AgentRunActivity activity) => new()
    {
        RunId = id,
        RecordedAtUtc = timeProvider.GetUtcNow(),
        ActivityJson = Sanitize(activity),
    };

    private string Sanitize(object value)
    {
        var node = JsonSerializer.SerializeToNode(value, _historyJsonOptions);
        Redact(node);
        return node?.ToJsonString() ?? "null";
    }

    private void Redact(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            if (obj["type"] is JsonValue type && type.TryGetValue<string>(out var contentType)
                && contentType is "image" or "audio" or "resource")
            {
                obj.Clear();
                obj["omitted"] = "binary or embedded resource";
                return;
            }
            foreach (var property in obj.ToArray())
            {
                var name = property.Key.Replace("_", string.Empty, StringComparison.Ordinal)
                    .Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
                if (name.Contains("password", StringComparison.Ordinal)
                    || name.Contains("secret", StringComparison.Ordinal)
                    || name.Contains("token", StringComparison.Ordinal) && !name.EndsWith("tokens", StringComparison.Ordinal)
                    || name.Contains("authorization", StringComparison.Ordinal)
                    || name.Contains("apikey", StringComparison.Ordinal)
                    || name.Contains("connectionstring", StringComparison.Ordinal)
                    || name is "headers" or "bytes" or "base64content" or "datauri")
                    obj[property.Key] = "[omitted]";
                else if (property.Value is JsonValue scalar && scalar.TryGetValue<string>(out var text))
                    obj[property.Key] = RedactText(text);
                else
                    Redact(property.Value);
            }
        }
        else if (node is JsonArray array)
            for (var index = 0; index < array.Count; index++)
                if (array[index] is JsonValue scalar && scalar.TryGetValue<string>(out var text))
                    array[index] = RedactText(text);
                else
                    Redact(array[index]);
    }

    private string RedactText(string text)
    {
        if (text.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return "[binary omitted]";
        lock (_secretsLock)
            foreach (var secret in _secrets)
                text = text.Replace(secret, "[credential omitted]", StringComparison.Ordinal);
        return text;
    }

    private sealed class OmittedBinaryConverter : JsonConverter<byte[]>
    {
        public override byte[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException("History projections are write-only.");

        public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options) =>
            writer.WriteStringValue("[binary omitted]");
    }
}
