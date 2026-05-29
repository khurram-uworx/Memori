using Memori.Abstractions;
using Memori.Models;
using Memori.Storage;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;

namespace Memori.Tests;

/// <summary>
/// Factory for creating Memori instances with the new split for tests.
/// </summary>
public static class TestMemoriFactory
{
    /// <summary>
    /// Creates a Memori instance using in-memory implementations for both conversation storage and vector store.
    /// </summary>
    public static MemoriEngine Create(
        IAugmentationClient? augmentationClient = null,
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null,
        MemoriOptions? options = null)
    {
        var conversationStorage = new InMemoriConversationStorage();
        var vectorStore = new InMemoriVectorStore();
        var factCollection = vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");
        return new MemoriEngine(conversationStorage, factCollection, options, augmentationClient: augmentationClient, embeddingGenerator: embeddingGenerator);
    }

    /// <summary>
    /// Creates a Memori instance with a custom conversation storage.
    /// </summary>
    public static MemoriEngine Create(
        IConversationStorage conversationStorage,
        IAugmentationClient? augmentationClient = null,
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null,
        MemoriOptions? options = null)
    {
        var vectorStore = new InMemoriVectorStore();
        var factCollection = vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");
        return new MemoriEngine(conversationStorage, factCollection, options, augmentationClient: augmentationClient, embeddingGenerator: embeddingGenerator);
    }

    /// <summary>
    /// Creates a Memori instance with a custom vector store collection.
    /// </summary>
    public static MemoriEngine Create(
        VectorStoreCollection<string, MemoryFactRecord> factCollection,
        IAugmentationClient? augmentationClient = null,
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null,
        MemoriOptions? options = null)
    {
        var conversationStorage = new InMemoriConversationStorage();
        return new MemoriEngine(conversationStorage, factCollection, options, augmentationClient: augmentationClient, embeddingGenerator: embeddingGenerator);
    }
}
