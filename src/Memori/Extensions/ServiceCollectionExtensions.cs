using Memori.Abstractions;
using Memori.Augmentation;
using Memori.Management;
using Memori.Models;
using Memori.Search;
using Memori.Storage;
using Memori.Summarization;
using Memori.Versioning;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;

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
            services.AddSingleton(sp =>
            {
                var options = new MemoriOptions();
                configureOptions(options);
                options.Validate();
                return options;
            });
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
        services.TryAddSingleton<IConversationStorage, InMemoryConversationStorage>();
        services.TryAddSingleton<IAugmentationClient, NullAugmentationClient>();
        services.TryAddSingleton<MemorySearchService>();
        services.TryAddSingleton(sp =>
        {
            var vectorStore = new InMemoryVectorStore();
            var collection = vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");
            return collection;
        });
        services.TryAddSingleton(sp => new MemoriEngine(
            sp.GetRequiredService<IConversationStorage>(),
            sp.GetRequiredService<VectorStoreCollection<string, MemoryFactRecord>>(),
            sp.GetRequiredService<MemoriOptions>(),
            augmentationClient: sp.GetRequiredService<IAugmentationClient>(),
            embeddingGenerator: sp.GetService<IEmbeddingGenerator<string, Embedding<float>>>(),
            memoryManagement: sp.GetService<IMemoryManagementService>(),
            logger: sp.GetService<ILogger<MemoriEngine>>()));

        services.TryAddSingleton<VersioningService>();
        services.TryAddSingleton<IMemoryManagementService, MemoryManagementService>();

        if (services.Any(d => d.ServiceType == typeof(IChatClient)))
            services.TryAddSingleton<IThreadSummarizer>(sp =>
            {
                var chatClient = sp.GetRequiredService<IChatClient>();
                return new ChatClientThreadSummarizer(chatClient);
            });

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
    /// Registers Memori services with a custom conversation storage factory.
    /// </summary>
    public static IServiceCollection AddMemori(
        this IServiceCollection services,
        Func<IServiceProvider, IConversationStorage> conversationStorageFactory,
        Action<MemoriOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(conversationStorageFactory);

        addOptions(services, configureOptions);
        services.AddSingleton(sp => conversationStorageFactory(sp));
        return addCoreServices(services);
    }

    /// <summary>
    /// Registers Memori services with explicit conversation storage and VectorStore implementations.
    /// </summary>
    public static IServiceCollection AddMemori(
        this IServiceCollection services,
        IConversationStorage conversationStorage,
        VectorStoreCollection<string, MemoryFactRecord> factCollection,
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null,
        IAugmentationClient? augmentationClient = null,
        Action<MemoriOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(conversationStorage);
        ArgumentNullException.ThrowIfNull(factCollection);

        addOptions(services, configureOptions);
        services.AddSingleton(conversationStorage);
        services.AddSingleton(factCollection);

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
        Func<IServiceProvider, IConversationStorage> conversationStorageFactory,
        Func<IServiceProvider, VectorStoreCollection<string, MemoryFactRecord>>? factCollectionFactory,
        Func<IServiceProvider, IEmbeddingGenerator<string, Embedding<float>>>? embeddingGeneratorFactory,
        Func<IServiceProvider, IAugmentationClient>? augmentationClientFactory,
        Action<MemoriOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(conversationStorageFactory);

        addOptions(services, configureOptions);
        services.AddSingleton(sp => conversationStorageFactory(sp));

        if (factCollectionFactory is not null)
            services.AddSingleton(sp => factCollectionFactory(sp));

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
    /// Registers Memori services with custom factories for Tier 3 services.
    /// </summary>
    public static IServiceCollection AddMemori(
        this IServiceCollection services,
        Func<IServiceProvider, IMemoryManagementService>? memoryManagementFactory,
        Func<IServiceProvider, IThreadSummarizer>? threadSummarizerFactory = null,
        Func<IServiceProvider, VersioningService>? versioningServiceFactory = null,
        Action<MemoriOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        addOptions(services, configureOptions);

        if (memoryManagementFactory is not null)
            services.AddSingleton(sp => memoryManagementFactory(sp));

        if (threadSummarizerFactory is not null)
            services.AddSingleton(sp => threadSummarizerFactory(sp));

        if (versioningServiceFactory is not null)
            services.AddSingleton(sp => versioningServiceFactory(sp));

        return addCoreServices(services);
    }

    /// <summary>
    /// Registers Memori services with a composite collection factory.
    /// </summary>
    public static IServiceCollection AddMemori(
        this IServiceCollection services,
        Func<IServiceProvider, CompositeMemoryCollection> compositeCollectionFactory,
        Action<MemoriOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(compositeCollectionFactory);

        addOptions(services, configureOptions);
        services.AddSingleton<VectorStoreCollection<string, MemoryFactRecord>>(sp => compositeCollectionFactory(sp));
        return addCoreServices(services);
    }

    /// <summary>
    /// Resolves a configured Memori facade from a service provider.
    /// </summary>
    public static MemoriEngine CreateMemori(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.GetRequiredService<MemoriEngine>();
    }
}
