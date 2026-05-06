using Memori.Abstractions;
using Memori.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Memori;

/// <summary>
/// Chat pipeline extensions for Memori.
/// </summary>
public static class ChatClientBuilderExtensions
{
    /// <summary>
    /// Adds Memori middleware to a chat client pipeline.
    /// </summary>
    public static ChatClientBuilder UseMemori(this ChatClientBuilder builder, Memori memori)
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
        Func<IServiceProvider, Memori> memoriFactory)
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
            => new MemoriChatClient(inner, services.GetRequiredService<Memori>()));
    }

    /// <summary>
    /// Adds Memori middleware and constructs the facade during pipeline registration.
    /// </summary>
    public static ChatClientBuilder UseMemori(
        this ChatClientBuilder builder,
        Func<IServiceProvider, IStorage> storageFactory,
        Func<IServiceProvider, IEmbeddingGenerator<string, Embedding<float>>>? embeddingGeneratorFactory = null,
        Func<IServiceProvider, IAugmentationClient>? augmentationClientFactory = null,
        Action<MemoriOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(storageFactory);

        return builder.Use((inner, services) =>
        {
            var options = new MemoriOptions();
            configureOptions?.Invoke(options);
            options.Validate();

            var memori = new Memori(
                storageFactory(services),
                options,
                augmentationClient: augmentationClientFactory?.Invoke(services),
                embeddingGenerator: embeddingGeneratorFactory?.Invoke(services));

            return new MemoriChatClient(inner, memori);
        });
    }
}
