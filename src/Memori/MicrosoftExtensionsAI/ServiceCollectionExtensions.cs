using Memori.Abstractions;
using Memori.Augmentation;
using Memori.Models;
using Memori.Search;
using Memori.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

    static IServiceCollection addOptions(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton(sp =>
        {
            var options = configuration.Get<MemoriOptions>() ?? new MemoriOptions();
            options.Validate();
            return options;
        });

        return services;
    }

    static IServiceCollection addCoreServices(IServiceCollection services)
    {
        services.TryAddSingleton<IStorage, InMemoryStorage>();
        services.TryAddSingleton<IAugmentationClient, NullAugmentationClient>();
        services.TryAddSingleton<MemorySearchService>();
        services.TryAddSingleton(sp => new Memori(
            sp.GetRequiredService<IStorage>(),
            sp.GetRequiredService<MemoriOptions>(),
            augmentationClient: sp.GetRequiredService<IAugmentationClient>(),
            embeddingGenerator: sp.GetService<IEmbeddingGenerator<string, Embedding<float>>>()));

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
        return addCoreServices(services);
    }

    /// <summary>
    /// Registers Memori services and binds <see cref="MemoriOptions"/> from configuration.
    /// </summary>
    public static IServiceCollection AddMemori(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        addOptions(services, configuration);
        return addCoreServices(services);
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
        return addCoreServices(services);
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

        return addCoreServices(services);
    }

    /// <summary>
    /// Registers Memori services with custom factories for the common composition points.
    /// </summary>
    public static IServiceCollection AddMemori(
        this IServiceCollection services,
        Func<IServiceProvider, IStorage> storageFactory,
        Func<IServiceProvider, IEmbeddingGenerator<string, Embedding<float>>>? embeddingGeneratorFactory,
        Func<IServiceProvider, IAugmentationClient>? augmentationClientFactory,
        Action<MemoriOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(storageFactory);

        addOptions(services, configureOptions);
        services.AddSingleton(sp => storageFactory(sp));

        if (embeddingGeneratorFactory is not null)
            services.AddSingleton(sp => embeddingGeneratorFactory(sp));

        if (augmentationClientFactory is not null)
            services.AddSingleton(sp => augmentationClientFactory(sp));

        return addCoreServices(services);
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
        services.AddSingleton(sp => embeddingGeneratorFactory(sp));
        return addCoreServices(services);
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
        services.AddSingleton(sp => augmentationClientFactory(sp));
        return addCoreServices(services);
    }

    /// <summary>
    /// Resolves a configured Memori facade from a service provider.
    /// </summary>
    public static Memori CreateMemori(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.GetRequiredService<Memori>();
    }
}
