using Memori.Abstractions;
using Memori.Models;
using Memori.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.VectorData;

namespace Memori;

/// <summary>
/// Chat pipeline extensions for Memori.
/// </summary>
public static class ChatClientBuilderExtensions
{
    /// <summary>
    /// Adds Memori middleware to a chat client pipeline.
    /// </summary>
    public static ChatClientBuilder UseMemori(this ChatClientBuilder builder, MemoriEngine memori)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(memori);

        return builder.Use(inner => new MemoriChatClient(inner, memori));
    }

    /// <summary>
    /// Adds Memori middleware using a service-provider-based factory.
    /// </summary>
    public static ChatClientBuilder UseMemori(
        this ChatClientBuilder builder,
        Func<IServiceProvider, MemoriEngine> memoriFactory)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(memoriFactory);

        return builder.Use((inner, services) => new MemoriChatClient(inner, memoriFactory(services)));
    }

    /// <summary>
    /// Adds Memori middleware that builds its facade from the active service provider.
    /// </summary>
    public static ChatClientBuilder UseMemori(this ChatClientBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Use(
            (inner, services)
            => new MemoriChatClient(inner, services.GetRequiredService<MemoriEngine>()));
    }

    /// <summary>
    /// Adds Memori middleware and constructs the facade during pipeline registration.
    /// </summary>
    public static ChatClientBuilder UseMemori(
        this ChatClientBuilder builder,
        Func<IServiceProvider, IConversationStorage> conversationStorageFactory,
        Func<IServiceProvider, VectorStoreCollection<string, MemoryFactRecord>>? factCollectionFactory = null,
        Func<IServiceProvider, IEmbeddingGenerator<string, Embedding<float>>>? embeddingGeneratorFactory = null,
        Func<IServiceProvider, IAugmentationClient>? augmentationClientFactory = null,
        Action<MemoriOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(conversationStorageFactory);

        return builder.Use((inner, services) =>
        {
            var options = new MemoriOptions();
            configureOptions?.Invoke(options);
            options.Validate();

            var conversationStorage = conversationStorageFactory(services);
            var factCollection = factCollectionFactory?.Invoke(services) ?? CreateDefaultFactCollection(services);
            var augmentationClient = augmentationClientFactory?.Invoke(services);
            var embeddingGenerator = embeddingGeneratorFactory?.Invoke(services);

            var memori = new MemoriEngine(
                conversationStorage,
                factCollection,
                options,
                augmentationClient: augmentationClient,
                embeddingGenerator: embeddingGenerator);

            return new MemoriChatClient(inner, memori);
        });
    }

    static VectorStoreCollection<string, MemoryFactRecord> CreateDefaultFactCollection(IServiceProvider services)
    {
        var vectorStore = new InMemoryVectorStore();
        return vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");
    }
}
