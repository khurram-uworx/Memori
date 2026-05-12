using Memori.Embeddings;
using Microsoft.Extensions.AI;
using NUnit.Framework;
using System.Numerics.Tensors;

namespace Memori.Tests;

public class NgramEmbeddingGeneratorTests
{
    static readonly NgramEmbeddingGenerator Sut = new();

    [Test]
    public async Task GenerateAsync_Returns1536Dimensions()
    {
        var result = await Sut.GenerateAsync(["hello world"]);
        var embedding = result[0].Vector;

        Assert.That(embedding.Length, Is.EqualTo(1536));
    }

    [Test]
    public async Task GenerateAsync_ProducesL2NormalizedVector()
    {
        var result = await Sut.GenerateAsync(["hello world"]);
        var embedding = result[0].Vector;

        var norm = MathF.Sqrt(TensorPrimitives.SumOfSquares(embedding.Span));
        Assert.That(norm, Is.EqualTo(1f).Within(1e-6f));
    }

    [Test]
    public async Task GenerateAsync_IdenticalInputs_HaveCosineSimilarityApproximatelyOne()
    {
        var result = await Sut.GenerateAsync(["the quick brown fox", "the quick brown fox"]);
        var v0 = result[0].Vector;
        var v1 = result[1].Vector;

        var dot = TensorPrimitives.Dot(v0.Span, v1.Span);
        Assert.That(dot, Is.EqualTo(1f).Within(1e-6f));
    }

    [Test]
    public async Task GenerateAsync_VeryDifferentInputs_HaveLowerSimilarity()
    {
        var result = await Sut.GenerateAsync(["abcdefghij", "1234567890"]);
        var v0 = result[0].Vector;
        var v1 = result[1].Vector;

        var dot = TensorPrimitives.Dot(v0.Span, v1.Span);
        Assert.That(dot, Is.LessThan(0.9f));
    }

    [Test]
    public async Task GenerateAsync_EmptyString_ReturnsZeroVector()
    {
        var result = await Sut.GenerateAsync([""]);
        var embedding = result[0].Vector;

        Assert.That(embedding.ToArray(), Is.All.EqualTo(0f));
        Assert.That(embedding.Length, Is.EqualTo(1536));
    }

    [Test]
    public async Task GenerateAsync_SingleCharacterString_ReturnsZeroVector()
    {
        var result = await Sut.GenerateAsync(["x"]);
        var embedding = result[0].Vector;

        Assert.That(embedding.ToArray(), Is.All.EqualTo(0f));
        Assert.That(embedding.Length, Is.EqualTo(1536));
    }

    [Test]
    public void GenerateAsync_CancellationToken_IsRespected()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.That(
            () => Sut.GenerateAsync(["hello world"], cancellationToken: cts.Token),
            Throws.TypeOf<OperationCanceledException>());
    }

    [Test]
    public void GenerateAsync_CancellationToken_InInnerLoop_IsRespected()
    {
        using var cts = new CancellationTokenSource();
        using var barrier = new Barrier(2);
        var longInput = new string('a', 50000);

        var task = Task.Run(() =>
        {
            barrier.SignalAndWait();
            return Sut.GenerateAsync([longInput], cancellationToken: cts.Token);
        });

        barrier.SignalAndWait();
        cts.Cancel();

        Assert.That(
            () => task.GetAwaiter().GetResult(),
            Throws.TypeOf<OperationCanceledException>());
    }

    [Test]
    public async Task GenerateAsync_ReturnsGeneratedEmbeddingsWrapper()
    {
        var result = await Sut.GenerateAsync(["first", "second", "third"]);

        Assert.That(result, Is.InstanceOf<GeneratedEmbeddings<Embedding<float>>>());
        Assert.That(result.Count, Is.EqualTo(3));
        Assert.That(result[0].Vector.Length, Is.EqualTo(1536));
    }

    [Test]
    public void GetService_ReturnsEmbeddingGeneratorMetadata()
    {
        var metadata = Sut.GetService(typeof(EmbeddingGeneratorMetadata));

        Assert.That(metadata, Is.Not.Null);
        Assert.That(metadata, Is.InstanceOf<EmbeddingGeneratorMetadata>());
    }

    [Test]
    public void GetService_ReturnsNonNull_ForOwnType()
    {
        var service = Sut.GetService(typeof(NgramEmbeddingGenerator));

        Assert.That(service, Is.Not.Null);
        Assert.That(service, Is.SameAs(Sut));
    }

    [Test]
    public void GetService_ReturnsNull_ForUnknownType()
    {
        var service = Sut.GetService(typeof(string));

        Assert.That(service, Is.Null);
    }
}
