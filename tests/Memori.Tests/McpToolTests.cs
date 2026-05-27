using Memori.Embeddings;
using Memori.Mcp;
using Memori.Mcp.Models;
using Memori.Mcp.Tools;
using Memori.Storage;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System.Text.Json;

namespace Memori.Tests;

[TestFixture]
public sealed class McpToolTests
{
    static MemoriTools CreateTools()
    {
        var store = new InMemoriVectorStore();
        var facts = store.GetCollection<string, McpFactRecord>("mcp_facts");
        var options = Options.Create(new MemoriMcpOptions());
        return new MemoriTools(facts, options);
    }

    static MemoriTools CreateToolsWithVectors()
    {
        var store = new InMemoriVectorStore();
        var facts = store.GetCollection<string, McpFactRecord>("mcp_facts");
        var options = Options.Create(new MemoriMcpOptions { EnableFullText = false });
        var generator = new NgramEmbeddingGenerator();
        return new MemoriTools(facts, options, generator);
    }

    [Test]
    public async Task memori_remember_returns_success()
    {
        var tools = CreateTools();
        var result = await tools.Remember("test fact");
        var doc = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.That(doc.GetProperty("status").GetString(), Is.EqualTo("remembered"));
    }

    [Test]
    public async Task memori_remember_with_metadata()
    {
        var tools = CreateTools();
        var result = await tools.Remember("preference fact", memoryType: "preference", tags: "tag1,tag2");
        var doc = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.That(doc.GetProperty("status").GetString(), Is.EqualTo("remembered"));
    }

    [Test]
    public async Task memori_search_returns_results()
    {
        var tools = CreateTools();
        await tools.Remember("blue is my favorite color");
        await tools.Remember("I like green as well");

        var result = await tools.Search("blue");
        var items = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.That(items.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(items.GetArrayLength(), Is.GreaterThan(0));
    }

    [Test]
    public async Task memori_search_empty_query_returns_empty()
    {
        var tools = CreateTools();
        await tools.Remember("blue is my favorite color");

        var result = await tools.Search("nonexistent_xyzzy");
        var items = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.That(items.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(items.GetArrayLength(), Is.EqualTo(0));
    }

    [Test]
    public async Task memori_search_with_vectors_returns_results()
    {
        var tools = CreateToolsWithVectors();
        await tools.Remember("blue is my favorite color");
        await tools.Remember("I like green as well");

        var result = await tools.Search("blue");
        var items = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.That(items.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(items.GetArrayLength(), Is.GreaterThan(0));
    }

    [Test]
    public async Task memori_search_with_vectors_finds_semantically_similar()
    {
        var tools = CreateToolsWithVectors();
        await tools.Remember("exploring the oceans and marine life");
        await tools.Remember("the weather is nice and sunny today");

        var result = await tools.Search("deep sea ocean exploration");
        var items = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.That(items.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(items.GetArrayLength(), Is.GreaterThan(0));
    }

    [Test]
    public async Task memori_list_returns_memories()
    {
        var tools = CreateTools();
        await tools.Remember("fact one");
        await tools.Remember("fact two");

        var result = await tools.List();
        var items = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.That(items.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(items.GetArrayLength(), Is.EqualTo(2));
    }

    [Test]
    public async Task memori_list_respects_pagination()
    {
        var tools = CreateTools();
        await tools.Remember("fact one");
        await tools.Remember("fact two");
        await tools.Remember("fact three");

        var result = await tools.List(skip: 1, take: 1);
        var items = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.That(items.GetArrayLength(), Is.EqualTo(1));
    }

    [Test]
    public async Task memori_get_returns_memory()
    {
        var tools = CreateTools();
        await tools.Remember("test content");

        var list = JsonSerializer.Deserialize<JsonElement>(await tools.List());
        var id = list[0].GetProperty("id").GetString()!;

        var result = await tools.Get(id);
        var doc = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.That(doc.GetProperty("id").GetString(), Is.EqualTo(id));
    }

    [Test]
    public async Task memori_get_unknown_id_returns_error()
    {
        var tools = CreateTools();
        var result = await tools.Get("nonexistent-id");
        var doc = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.That(doc.TryGetProperty("error", out _), Is.True);
    }

    [Test]
    public async Task memori_update_modifies_content()
    {
        var tools = CreateTools();
        await tools.Remember("original content");

        var list = JsonSerializer.Deserialize<JsonElement>(await tools.List());
        var id = list[0].GetProperty("id").GetString()!;

        var updateResult = await tools.Update(id, "updated content");
        var updateDoc = JsonSerializer.Deserialize<JsonElement>(updateResult);
        Assert.That(updateDoc.GetProperty("status").GetString(), Is.EqualTo("updated"));

        var getResult = await tools.Get(id);
        var getDoc = JsonSerializer.Deserialize<JsonElement>(getResult);
        Assert.That(getDoc.GetProperty("content").GetString(), Is.EqualTo("updated content"));
    }

    [Test]
    public async Task memori_update_unknown_id_returns_error()
    {
        var tools = CreateTools();
        var result = await tools.Update("nonexistent-id", "new content");
        var doc = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.That(doc.TryGetProperty("error", out _), Is.True);
    }

    [Test]
    public async Task memori_delete_soft_deletes()
    {
        var tools = CreateTools();
        await tools.Remember("to delete");

        var list = JsonSerializer.Deserialize<JsonElement>(await tools.List());
        var id = list[0].GetProperty("id").GetString()!;

        var deleteResult = await tools.Delete(id);
        var deleteDoc = JsonSerializer.Deserialize<JsonElement>(deleteResult);
        Assert.That(deleteDoc.GetProperty("status").GetString(), Is.EqualTo("deleted"));
    }

    [Test]
    public async Task memori_delete_unknown_id_returns_error()
    {
        var tools = CreateTools();
        var result = await tools.Delete("nonexistent-id");
        var doc = JsonSerializer.Deserialize<JsonElement>(result);
        Assert.That(doc.TryGetProperty("error", out _), Is.True);
    }

    [Test]
    public async Task memori_clear_clears_all()
    {
        var tools = CreateTools();
        await tools.Remember("fact one");
        await tools.Remember("fact two");
        await tools.Remember("fact three");

        var clearResult = await tools.Clear();
        var clearDoc = JsonSerializer.Deserialize<JsonElement>(clearResult);
        Assert.That(clearDoc.GetProperty("status").GetString(), Is.EqualTo("cleared"));
        Assert.That(clearDoc.GetProperty("count").GetInt32(), Is.EqualTo(3));
    }
}
