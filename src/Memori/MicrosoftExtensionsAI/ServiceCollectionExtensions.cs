using Memori.Abstractions;
using Memori.Augmentation;
using Memori.Models;
using Memori.Search;
using Memori.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Memori;

/// <summary>
/// Dependency injection helpers for Memori.
/// </summary>
public static class ServiceCollectionExtensions
{
    static IServiceCollection addOptions(
        IServiceCollection services,
        Action<MemoriOptions>? configureOptions)
    {
        if (configureOptions is not null)
        {
            services.AddSingleton(sp =>
            {
                var options = new MemoriOptions();
                configureOptions(options);
                options.Validate();
                return options;
            });
        }
        else
            services.AddSingleton(new MemoriOptions());

        return services;
    }

    /// <summary>
    /// Registers Memori services.
    /// </summary>
    public static IServiceCollection AddMemori(
        this IServiceCollection services,
        Action<MemoriOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        addOptions(services, configureOptions);

        services.AddSingleton<IStorage, InMemoryStorage>();
        services.AddSingleton<IAugmentationClient, NullAugmentationClient>();
        services.AddSingleton<Memori>();
        services.AddSingleton<MemorySearchService>();

        return services;
    }

    /// <summary>
    /// Registers Memori services with a custom storage factory.
    /// </summary>
    public static IServiceCollection AddMemori(
        this IServiceCollection services,
        Func<IServiceProvider, IStorage> storageFactory,
        Action<MemoriOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(storageFactory);

        addOptions(services, configureOptions);
        services.AddSingleton(sp => storageFactory(sp));
        services.AddSingleton<IAugmentationClient, NullAugmentationClient>();
        services.AddSingleton<Memori>();
        services.AddSingleton<MemorySearchService>();

        return services;
    }

    /// <summary>
    /// Registers Memori services with explicit storage and embedding implementations.
    /// </summary>
    public static IServiceCollection AddMemori(
        this IServiceCollection services,
        IStorage storage,
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null,
        IAugmentationClient? augmentationClient = null,
        Action<MemoriOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(storage);

        addOptions(services, configureOptions);
        services.AddSingleton(storage);

        if (embeddingGenerator is not null)
            services.AddSingleton(embeddingGenerator);

        if (augmentationClient is not null)
            services.AddSingleton(augmentationClient);
        else
            services.AddSingleton<IAugmentationClient, NullAugmentationClient>();

        services.AddSingleton(sp => new Memori(
            sp.GetRequiredService<IStorage>(),
            sp.GetRequiredService<MemoriOptions>(),
            augmentationClient: sp.GetRequiredService<IAugmentationClient>(),
            embeddingGenerator: sp.GetService<IEmbeddingGenerator<string, Embedding<float>>>()));
        services.AddSingleton<MemorySearchService>();

        return services;
    }

    /// <summary>
    /// Registers Memori services with a custom embedding generator factory.
    /// </summary>
    public static IServiceCollection AddMemori(
        this IServiceCollection services,
        Func<IServiceProvider, IEmbeddingGenerator<string, Embedding<float>>> embeddingGeneratorFactory,
        Action<MemoriOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(embeddingGeneratorFactory);

        addOptions(services, configureOptions);
        services.AddSingleton<IStorage, InMemoryStorage>();
        services.AddSingleton(sp => embeddingGeneratorFactory(sp));
        services.AddSingleton<IAugmentationClient, NullAugmentationClient>();
        services.AddSingleton<Memori>();
        services.AddSingleton<MemorySearchService>();

        return services;
    }

    /// <summary>
    /// Registers Memori services with a custom augmentation client factory.
    /// </summary>
    public static IServiceCollection AddMemori(
        this IServiceCollection services,
        Func<IServiceProvider, IAugmentationClient> augmentationClientFactory,
        Action<MemoriOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(augmentationClientFactory);

        addOptions(services, configureOptions);
        services.AddSingleton<IStorage, InMemoryStorage>();
        services.AddSingleton(sp => augmentationClientFactory(sp));
        services.AddSingleton<Memori>();
        services.AddSingleton<MemorySearchService>();

        return services;
    }

}
