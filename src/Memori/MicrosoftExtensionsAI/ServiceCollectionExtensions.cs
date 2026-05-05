using Memori.Abstractions;
using Memori.Augmentation;
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
        Action<global::Memori.Models.MemoriOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configureOptions is not null)
        {
            services.AddSingleton(sp =>
            {
                var options = new global::Memori.Models.MemoriOptions();
                configureOptions(options);
                options.Validate();
                return options;
            });
        }
        else
        {
            services.AddSingleton(new global::Memori.Models.MemoriOptions());
        }

        services.AddSingleton<IStorage, InMemoryStorage>();
        services.AddSingleton<IAugmentationClient, NullAugmentationClient>();
        services.AddSingleton<global::Memori.Memori>();
        services.AddSingleton<MemorySearchService>();

        return services;
    }
}
