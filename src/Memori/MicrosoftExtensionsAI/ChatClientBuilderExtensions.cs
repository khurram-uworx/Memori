using Microsoft.Extensions.AI;

namespace Memori.MicrosoftExtensionsAI;

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
}
