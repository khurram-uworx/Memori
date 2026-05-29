using Memori.Models;
using Memori.Search;
using Memori.Storage;
using NUnit.Framework;

namespace Memori.Tests;

public class PromptContextFormattingTests
{
    static readonly DateTimeOffset FactTime = new(2026, 5, 6, 12, 0, 0, TimeSpan.Zero);
    static readonly DateTimeOffset SummaryTime = new(2026, 5, 6, 12, 30, 0, TimeSpan.Zero);

    static RecallResult createResult()
        => new(
            "fact-1",
            "The user lives in Karachi.",
            0.8,
            0.9,
            FactTime,
            [new MemorySummary("Lives in Karachi.", SummaryTime)],
            0.7,
            "profile");

    [Test]
    public void BuildPromptContext_ReturnsStructuredFactsSummariesMetadataAndRenderedText()
    {
        var options = new MemoriOptions
        {
            PromptContextTagName = "custom_context",
            PromptContextInstruction = "Use this only when relevant.",
            PromptFactsHeading = "Facts:",
            PromptSummariesHeading = "Summaries:",
            RecallRelevanceThreshold = 0,
        };
        var search = new MemorySearchService(new InMemoriVectorStore().GetCollection<string, MemoryFactRecord>("test"), options: options);

        var context = search.BuildPromptContext([createResult()]);

        Assert.That(context.Metadata.TagName, Is.EqualTo("custom_context"));
        Assert.That(context.Metadata.Instruction, Is.EqualTo("Use this only when relevant."));
        Assert.That(context.Metadata.FactsHeading, Is.EqualTo("Facts:"));
        Assert.That(context.Metadata.SummariesHeading, Is.EqualTo("Summaries:"));
        Assert.That(context.Facts, Has.Count.EqualTo(1));
        Assert.That(context.Facts[0].FactId, Is.EqualTo("fact-1"));
        Assert.That(context.Facts[0].Content, Is.EqualTo("The user lives in Karachi."));
        Assert.That(context.Facts[0].MemoryType, Is.EqualTo("profile"));
        Assert.That(context.Facts[0].RenderedText, Does.Contain("Stated at 2026-05-06 12:00:00"));
        Assert.That(context.Summaries, Has.Count.EqualTo(1));
        Assert.That(context.Summaries[0].Content, Is.EqualTo("Lives in Karachi."));
        Assert.That(context.Summaries[0].RenderedText, Does.Contain("[2026-05-06 12:30:00]"));
        Assert.That(context.RenderedText, Does.StartWith("<custom_context>"));
        Assert.That(context.RenderedText, Does.Contain("Facts:"));
        Assert.That(context.RenderedText, Does.Contain("Summaries:"));
        Assert.That(context.RenderedText, Does.EndWith("</custom_context>"));
    }

    [Test]
    public void FormatPromptContext_MatchesStructuredRenderedText()
    {
        var search = new MemorySearchService(new InMemoriVectorStore().GetCollection<string, MemoryFactRecord>("test"));
        var results = new[] { createResult() };

        var structured = search.BuildPromptContext(results);
        var rendered = search.FormatPromptContext(results);

        Assert.That(rendered, Is.EqualTo(structured.RenderedText));
    }

    [Test]
    public void BuildPromptContext_AppliesBulletTimestampHeadingAndSummaryOptions()
    {
        var options = new MemoriOptions
        {
            IncludeFactTimestampsInPrompt = false,
            IncludeSummariesInPrompt = false,
            PromptFactBullet = "*",
            PromptSummaryBullet = ">",
            PromptFactsHeading = "Memory facts:",
            PromptSummariesHeading = "Memory summaries:",
            PromptTimestampFormat = "yyyy-MM-dd",
        };
        var search = new MemorySearchService(new InMemoriVectorStore().GetCollection<string, MemoryFactRecord>("test"), options: options);

        var context = search.BuildPromptContext([createResult()]);

        Assert.That(context.Facts[0].RenderedText, Is.EqualTo("* The user lives in Karachi."));
        Assert.That(context.Summaries, Is.Empty);
        Assert.That(context.RenderedText, Does.Contain("Memory facts:"));
        Assert.That(context.RenderedText, Does.Not.Contain("Memory summaries:"));
        Assert.That(context.RenderedText, Does.Not.Contain("Stated at"));
        Assert.That(context.RenderedText, Does.Not.Contain("Lives in Karachi."));
    }

    [Test]
    public void BuildPromptContext_UsesConfiguredTimestampFormat()
    {
        var options = new MemoriOptions
        {
            PromptTimestampFormat = "yyyy-MM-dd",
        };
        var search = new MemorySearchService(new InMemoriVectorStore().GetCollection<string, MemoryFactRecord>("test"), options: options);

        var context = search.BuildPromptContext([createResult()]);

        Assert.That(context.Facts[0].RenderedText, Does.Contain("Stated at 2026-05-06"));
        Assert.That(context.Summaries[0].RenderedText, Does.Contain("[2026-05-06]"));
    }

    [Test]
    public void BuildPromptContext_WithEmptyResults_ReturnsEmptyRenderedText()
    {
        var search = new MemorySearchService(new InMemoriVectorStore().GetCollection<string, MemoryFactRecord>("test"));

        var context = search.BuildPromptContext([]);

        Assert.That(context.Facts, Is.Empty);
        Assert.That(context.Summaries, Is.Empty);
        Assert.That(context.RenderedText, Is.Empty);
    }

    [Test]
    public void BuildPromptContext_WithNullResults_Throws()
    {
        var search = new MemorySearchService(new InMemoriVectorStore().GetCollection<string, MemoryFactRecord>("test"));

        Assert.That(() => search.BuildPromptContext(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void BuildPromptContext_WithSpecialCharactersInContent_DoesNotFail()
    {
        var options = new MemoriOptions { PromptTimestampFormat = "yyyy-MM-dd" };
        var search = new MemorySearchService(new InMemoriVectorStore().GetCollection<string, MemoryFactRecord>("test"), options: options);
        var result = new RecallResult(
            "fact-1",
            "The user said price was <$100 & >$50 (100% sure) with \"special\" chars.",
            0.8, 0.9, FactTime, memoryType: "profile");

        var context = search.BuildPromptContext([result]);

        Assert.That(context.Facts, Has.Count.EqualTo(1));
        Assert.That(context.RenderedText, Does.Contain("<$100"));
        Assert.That(context.RenderedText, Does.Contain(">"));
        Assert.That(context.RenderedText, Does.Contain("\"special\""));
    }

    [Test]
    public void BuildPromptContext_WithSpaceBullet_TrimsWhitespace()
    {
        var options = new MemoriOptions
        {
            PromptFactBullet = "  *  ",
            PromptTimestampFormat = "yyyy-MM-dd",
        };
        var search = new MemorySearchService(new InMemoriVectorStore().GetCollection<string, MemoryFactRecord>("test"), options: options);

        var context = search.BuildPromptContext([createResult()]);

        Assert.That(context.Facts[0].RenderedText, Does.StartWith("* The user lives in Karachi."));
    }

    [Test]
    public void BuildPromptContext_WithLeadingAndTrailingWhitespaceInTagName_Trims()
    {
        var options = new MemoriOptions
        {
            PromptContextTagName = "  my_context  ",
            PromptTimestampFormat = "yyyy-MM-dd",
        };
        var search = new MemorySearchService(new InMemoriVectorStore().GetCollection<string, MemoryFactRecord>("test"), options: options);

        var context = search.BuildPromptContext([createResult()]);

        Assert.That(context.RenderedText, Does.StartWith("<my_context>"));
        Assert.That(context.RenderedText, Does.EndWith("</my_context>"));
    }

    [Test]
    public void FormatPromptContext_WithEmptyResults_ReturnsEmptyString()
    {
        var search = new MemorySearchService(new InMemoriVectorStore().GetCollection<string, MemoryFactRecord>("test"));

        var rendered = search.FormatPromptContext([]);

        Assert.That(rendered, Is.Empty);
    }

    [Test]
    public void FormatPromptContext_WithNullResults_Throws()
    {
        var search = new MemorySearchService(new InMemoriVectorStore().GetCollection<string, MemoryFactRecord>("test"));

        Assert.That(() => search.FormatPromptContext(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void FormatFactLines_WithEmptyResults_ReturnsEmpty()
    {
        var search = new MemorySearchService(new InMemoriVectorStore().GetCollection<string, MemoryFactRecord>("test"));

        var lines = search.FormatFactLines([]);

        Assert.That(lines, Is.Empty);
    }

    [Test]
    public void FormatSummaryLines_ReturnsEmptyWhenSummariesDisabled()
    {
        var options = new MemoriOptions { IncludeSummariesInPrompt = false };
        var search = new MemorySearchService(new InMemoriVectorStore().GetCollection<string, MemoryFactRecord>("test"), options: options);

        var lines = search.FormatSummaryLines([createResult()]);

        Assert.That(lines, Is.Empty);
    }

    [Test]
    public void FormatSummaryLines_DeduplicatesDuplicateSummaries()
    {
        var options = new MemoriOptions { IncludeSummariesInPrompt = true };
        var search = new MemorySearchService(new InMemoriVectorStore().GetCollection<string, MemoryFactRecord>("test"), options: options);
        var result = new RecallResult(
            "fact-1",
            "The user lives in Karachi.",
            0.8, 0.9, FactTime,
            [new MemorySummary("Lives in Karachi.", SummaryTime), new MemorySummary("Lives in Karachi.", SummaryTime)],
            0.7, "profile");

        var lines = search.FormatSummaryLines([result]);

        Assert.That(lines, Has.Count.EqualTo(1));
    }
}
