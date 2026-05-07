using Microsoft.Extensions.VectorData;
using Memori.Abstractions;
using Memori.Models;
using Memori.Storage;
using Microsoft.Extensions.AI;

namespace Memori.Tests;

/// <summary>
/// Factory for creating Memori instances with the new split for tests.
/// </summary>
public static class TestMemoriFactory
{
    /// <summary>
    /// Creates a Memori instance using in-memory implementations for both conversation storage and vector store.
    /// </summary>
    public static Memori Create(
        IAugmentationClient? augmentationClient = null,
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null,
        MemoriOptions? options = null)
    {
        var conversationStorage = new InMemoryConversationStorage();
        var vectorStore = new InMemoryVectorStore();
        var factCollection = vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");
        return new Memori(conversationStorage, factCollection, options, augmentationClient: augmentationClient, embeddingGenerator: embeddingGenerator);
    }

    /// <summary>
    /// Creates a Memori instance with a custom conversation storage.
    /// </summary>
    public static Memori Create(
        IConversationStorage conversationStorage,
        IAugmentationClient? augmentationClient = null,
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null,
        MemoriOptions? options = null)
    {
        var vectorStore = new InMemoryVectorStore();
        var factCollection = vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");
        return new Memori(conversationStorage, factCollection, options, augmentationClient: augmentationClient, embeddingGenerator: embeddingGenerator);
    }

    /// <summary>
    /// Creates a Memori instance with a custom vector store collection.
    /// </summary>
    public static Memori Create(
        VectorStoreCollection<string, MemoryFactRecord> factCollection,
        IAugmentationClient? augmentationClient = null,
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null,
        MemoriOptions? options = null)
    {
        var conversationStorage = new InMemoryConversationStorage();
        return new Memori(conversationStorage, factCollection, options, augmentationClient: augmentationClient, embeddingGenerator: embeddingGenerator);
    }
}
