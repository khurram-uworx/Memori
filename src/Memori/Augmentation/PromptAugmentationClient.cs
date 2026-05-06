using Memori.Abstractions;
using Memori.Models;
using Microsoft.Extensions.AI;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memori.Augmentation;

/// <summary>
/// Built-in prompt-based augmentation client for extracting durable memories from conversation turns.
/// </summary>
public sealed class PromptAugmentationClient : IAugmentationClient
{
    readonly IChatClient chatClient;
    readonly JsonSerializerOptions jsonOptions;
    readonly string prompt;

    /// <summary>
    /// Creates a new prompt-based augmentation client.
    /// </summary>
    public PromptAugmentationClient(IChatClient chatClient, string? prompt = null)
    {
        this.chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        this.prompt = string.IsNullOrWhiteSpace(prompt)
            ? DefaultPrompt
            : prompt;
        jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        jsonOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    /// <inheritdoc />
    public async ValueTask<AugmentationResult?> AugmentAsync(
        AugmentationInput context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var messages = new List<ChatMessage>(context.Messages.Count + 2)
        {
            new(ChatRole.System, prompt),
            new(ChatRole.User, serializeInput(context)),
        };

        var response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var json = response.Text;

        if (string.IsNullOrWhiteSpace(json))
            return null;

        return parse(json);
    }

    static string serializeInput(AugmentationInput context)
        => JsonSerializer.Serialize(new PromptAugmentationInput(
            context.EntityId,
            context.ProcessId,
            context.ConversationId,
            context.ConversationSummary,
            context.Messages.Select(message => new PromptAugmentationMessage(
                message.Role,
                message.Content,
                message.Type,
                message.CreatedAt)).ToArray()), JsonSerializerOptions.Web);

    AugmentationResult? parse(string json)
    {
        PromptAugmentationOutput? output;

        try
        {
            output = JsonSerializer.Deserialize<PromptAugmentationOutput>(json, jsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }

        if (output is null)
            return null;

        var factsArray = output.Facts is { Length: > 0 }
            ? output.Facts
                .Where(fact => !string.IsNullOrWhiteSpace(fact.Content))
                .Select(fact => new NewMemoryFact(
                    fact.Content!,
                    memoryType: fact.MemoryType ?? "general",
                    createdAt: fact.CreatedAt))
                .ToArray()
            : Array.Empty<NewMemoryFact>();
        IReadOnlyList<NewMemoryFact>? facts = factsArray.Length > 0 ? factsArray : null;

        var triplesArray = output.SemanticTriples is { Length: > 0 }
            ? output.SemanticTriples
                .Where(triple => !string.IsNullOrWhiteSpace(triple.SubjectName) &&
                                 !string.IsNullOrWhiteSpace(triple.SubjectType) &&
                                 !string.IsNullOrWhiteSpace(triple.Predicate) &&
                                 !string.IsNullOrWhiteSpace(triple.ObjectName) &&
                                 !string.IsNullOrWhiteSpace(triple.ObjectType))
                .Select(triple => new SemanticTriple(
                    triple.SubjectName!,
                    triple.SubjectType!,
                    triple.Predicate!,
                    triple.ObjectName!,
                    triple.ObjectType!))
                .ToArray()
            : Array.Empty<SemanticTriple>();
        IReadOnlyList<SemanticTriple>? triples = triplesArray.Length > 0 ? triplesArray : null;

        var attributesArray = output.ProcessAttributes is { Length: > 0 }
            ? output.ProcessAttributes
                .Where(attribute => !string.IsNullOrWhiteSpace(attribute))
                .Select(attribute => attribute!)
                .ToArray()
            : Array.Empty<string>();
        IReadOnlyList<string>? processAttributes = attributesArray.Length > 0 ? attributesArray : null;

        var summary = string.IsNullOrWhiteSpace(output.ConversationSummary)
            ? null
            : output.ConversationSummary;

        if (facts is null && triples is null && processAttributes is null && summary is null)
            return null;

        return new AugmentationResult(facts, triples, processAttributes, summary);
    }

    const string DefaultPrompt =
        """
        Extract durable memories from the conversation. Return JSON with these optional arrays:
        facts, semanticTriples, processAttributes. Include conversationSummary when useful.
        Use only valid JSON and no markdown.
        """;

    sealed record PromptAugmentationInput(
        string EntityId,
        string? ProcessId,
        string ConversationId,
        string? ConversationSummary,
        PromptAugmentationMessage[] Messages);

    sealed record PromptAugmentationMessage(
        string Role,
        string Content,
        string Type,
        DateTimeOffset CreatedAt);

    sealed record PromptAugmentationOutput(
        [property: JsonPropertyName("facts")] PromptFact[]? Facts,
        [property: JsonPropertyName("semanticTriples")] PromptSemanticTriple[]? SemanticTriples,
        [property: JsonPropertyName("processAttributes")] string[]? ProcessAttributes,
        [property: JsonPropertyName("conversationSummary")] string? ConversationSummary);

    sealed record PromptFact(
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("createdAt")] DateTimeOffset? CreatedAt,
        [property: JsonPropertyName("memoryType")] string? MemoryType);

    sealed record PromptSemanticTriple(
        [property: JsonPropertyName("subjectName")] string? SubjectName,
        [property: JsonPropertyName("subjectType")] string? SubjectType,
        [property: JsonPropertyName("predicate")] string? Predicate,
        [property: JsonPropertyName("objectName")] string? ObjectName,
        [property: JsonPropertyName("objectType")] string? ObjectType);
}
