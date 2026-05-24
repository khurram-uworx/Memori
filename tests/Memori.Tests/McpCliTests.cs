using Memori.Mcp;
using NUnit.Framework;

namespace Memori.Tests;

[TestFixture]
public sealed class McpCliTests
{
    [Test]
    public void Parse_default_returns_ephemeral()
    {
        var (mode, storagePath, verbose, enableVectors) = CliParser.Parse([]);
        Assert.That(mode, Is.EqualTo(MemoriMode.Ephemeral));
        Assert.That(storagePath, Is.Null);
        Assert.That(verbose, Is.False);
        Assert.That(enableVectors, Is.False);
    }

    [Test]
    public void Parse_long_flag_sets_durable()
    {
        var (mode, _, _, _) = CliParser.Parse(["--long"]);
        Assert.That(mode, Is.EqualTo(MemoriMode.Durable));
    }

    [Test]
    public void Parse_mode_durable_sets_durable()
    {
        var (mode, _, _, _) = CliParser.Parse(["--mode", "durable"]);
        Assert.That(mode, Is.EqualTo(MemoriMode.Durable));
    }

    [Test]
    public void Parse_mode_ephemeral_sets_ephemeral()
    {
        var (mode, _, _, _) = CliParser.Parse(["--mode", "ephemeral"]);
        Assert.That(mode, Is.EqualTo(MemoriMode.Ephemeral));
    }

    [Test]
    public void Parse_path_sets_storagePath()
    {
        var (_, path, _, _) = CliParser.Parse(["--path", "./my-memories"]);
        Assert.That(path, Is.EqualTo("./my-memories"));
    }

    [Test]
    public void Parse_path_shortflag_sets_storagePath()
    {
        var (_, path, _, _) = CliParser.Parse(["-p", "./data"]);
        Assert.That(path, Is.EqualTo("./data"));
    }

    [Test]
    public void Parse_verbose_sets_verbose()
    {
        var (_, _, verbose, _) = CliParser.Parse(["--verbose"]);
        Assert.That(verbose, Is.True);
    }

    [Test]
    public void Parse_verbose_shortflag_sets_verbose()
    {
        var (_, _, verbose, _) = CliParser.Parse(["-v"]);
        Assert.That(verbose, Is.True);
    }

    [Test]
    public void Parse_long_with_path_and_verbose()
    {
        var (mode, path, verbose, _) = CliParser.Parse(["--long", "--path", "/tmp/mem", "--verbose"]);
        Assert.That(mode, Is.EqualTo(MemoriMode.Durable));
        Assert.That(path, Is.EqualTo("/tmp/mem"));
        Assert.That(verbose, Is.True);
    }

    [Test]
    public void Parse_unknown_mode_throws()
    {
        Assert.That(() => CliParser.Parse(["--mode", "invalid"]),
            Throws.ArgumentException.With.Message.Contains("Unknown mode"));
    }

    [Test]
    public void Parse_mode_shortflag()
    {
        var (mode, _, _, _) = CliParser.Parse(["-m", "durable"]);
        Assert.That(mode, Is.EqualTo(MemoriMode.Durable));
    }

    [Test]
    public void Parse_fulltext_sets_enableFullText()
    {
        var (_, _, _, enableFullText) = CliParser.Parse(["--fulltext"]);
        Assert.That(enableFullText, Is.True);
    }

    [Test]
    public void Parse_default_vectors_false()
    {
        var (_, _, _, enableVectors) = CliParser.Parse(["--long"]);
        Assert.That(enableVectors, Is.False);
    }
}
