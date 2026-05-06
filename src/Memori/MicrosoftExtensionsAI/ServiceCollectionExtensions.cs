using Memori.Abstractions;
using Memori.Augmentation;
using Memori.Models;
using Memori.Search;
using Memori.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Memori.MicrosoftExtensionsAI;

/// <summary>
/// Dependency injection helpers for Memori.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Memori services.
    /// </summary>
    public static IServiceCollection AddMemori(
        this IServiceCollection services,
        Action<MemoriOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

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

        services.AddSingleton<IStorage, InMemoryStorage>();
        services.AddSingleton<IAugmentationClient, NullAugmentationClient>();
        services.AddSingleton<Memori>();
        services.AddSingleton<MemorySearchService>();

        return services;
    }
}
