using FinFlow.Application.Chat.Interfaces;
using FinFlow.Application.Chat.Services;

namespace FinFlow.UnitTests.Application.Chat;

public sealed class StreamingOutputFilterTests
{
    private sealed class FakeFilter : IChatOutputFilter
    {
        public int SanitizeCallCount;
        public List<string> SanitizeArgs = new();
        public List<ChatOutputFilterResult> ReturnValues = new();

        public ChatOutputFilterResult Sanitize(string text)
        {
            SanitizeCallCount++;
            SanitizeArgs.Add(text);
            var result = ReturnValues.Count > 0
                ? ReturnValues[Math.Min(SanitizeCallCount - 1, ReturnValues.Count - 1)]
                : new ChatOutputFilterResult(text, 0, Array.Empty<string>());
            return result;
        }
    }

    [Fact]
    public void Append_SmallToken_DoesNotEmit()
    {
        var fake = new FakeFilter();
        var filter = new StreamingOutputFilter(fake);

        var emit = filter.Append("hello");

        Assert.Equal(string.Empty, emit);
        Assert.Equal(0, fake.SanitizeCallCount);
    }

    [Fact]
    public void Append_ExceedsLookahead_FlushesPrefix()
    {
        var fake = new FakeFilter
        {
            ReturnValues = new List<ChatOutputFilterResult>
            {
                new("prefix", 0, Array.Empty<string>()),
                new("suffix", 0, Array.Empty<string>())
            }
        };
        var filter = new StreamingOutputFilter(fake);

        var pad = new string('x', 300);
        var emit1 = filter.Append(pad);
        var emit2 = filter.Append("suffix");

        Assert.Equal("prefix", emit1);
        Assert.Equal("suffix", emit2);
        Assert.Equal(2, fake.SanitizeCallCount);
    }

    [Fact]
    public void Append_PiiAtBoundary_FlushesBeforeLeak()
    {
        var fake = new FakeFilter
        {
            ReturnValues = new List<ChatOutputFilterResult>
            {
                new("clean ", 0, Array.Empty<string>())
            }
        };
        var filter = new StreamingOutputFilter(fake);

        var part1 = new string('x', 250);
        var part2 = "accounting@finflow.com" + new string('y', 10);

        var emit1 = filter.Append(part1);
        var emit2 = filter.Append(part2);

        Assert.Equal(string.Empty, emit1);
        Assert.NotEqual(part2, emit2);
        Assert.DoesNotContain("finflow.com", emit2);
    }

    [Fact]
    public void Append_PartialEmailAcrossChunks_IsRedacted()
    {
        var fake = new FakeFilter
        {
            ReturnValues = new List<ChatOutputFilterResult>
            {
                new("", 0, Array.Empty<string>())
            }
        };
        var filter = new StreamingOutputFilter(fake);

        var prefix = new string('a', 240);
        var partialEmail = "user@";
        var suffix = "finflow.com more text";

        filter.Append(prefix);
        filter.Append(partialEmail);
        var tail = filter.Append(suffix);

        Assert.DoesNotContain("finflow.com", tail);
        Assert.DoesNotContain("@finflow", tail);
    }

    [Fact]
    public void Append_PartialPhoneAcrossChunks_IsRedacted()
    {
        var fake = new FakeFilter
        {
            ReturnValues = new List<ChatOutputFilterResult>
            {
                new("", 0, Array.Empty<string>())
            }
        };
        var filter = new StreamingOutputFilter(fake);

        var prefix = new string('b', 245);
        var partialPhone = "0901";
        var rest = "234567 full message";

        filter.Append(prefix);
        filter.Append(partialPhone);
        var tail = filter.Append(rest);

        Assert.DoesNotContain("0901234567", tail);
    }

    [Fact]
    public void Flush_EmitsRemainingBuffer()
    {
        var fake = new FakeFilter
        {
            ReturnValues = new List<ChatOutputFilterResult>
            {
                new("remaining", 0, Array.Empty<string>())
            }
        };
        var filter = new StreamingOutputFilter(fake);

        filter.Append(new string('c', 300));
        filter.Append("final");

        var tail = filter.Flush();

        Assert.Equal("remaining", tail);
    }

    [Fact]
    public void Flush_EmptyBuffer_ReturnsEmpty()
    {
        var fake = new FakeFilter();
        var filter = new StreamingOutputFilter(fake);

        var result = filter.Flush();

        Assert.Equal(string.Empty, result);
        Assert.Equal(0, fake.SanitizeCallCount);
    }

    [Fact]
    public void Flush_CalledTwice_ReturnsEmptySecondTime()
    {
        var fake = new FakeFilter
        {
            ReturnValues = new List<ChatOutputFilterResult>
            {
                new("first", 0, Array.Empty<string>())
            }
        };
        var filter = new StreamingOutputFilter(fake);

        filter.Append("some text");
        var first = filter.Flush();
        var second = filter.Flush();

        Assert.Equal("first", first);
        Assert.Equal(string.Empty, second);
    }

    [Fact]
    public void Append_TracksRedactionCount()
    {
        var fake = new FakeFilter
        {
            ReturnValues = new List<ChatOutputFilterResult>
            {
                new("redacted", 1, new[] { "Email" }),
                new("redacted2", 1, new[] { "Email" })
            }
        };
        var filter = new StreamingOutputFilter(fake);

        filter.Append(new string('d', 300));
        filter.Append("test");

        Assert.Equal(2, filter.TotalRedactionCount);
        Assert.Contains("Email", filter.RedactionTypes);
    }

    [Fact]
    public void Append_BufferExactlyAtBoundary_EmitsEmpty()
    {
        var fake = new FakeFilter
        {
            ReturnValues = new List<ChatOutputFilterResult>
            {
                new("exact", 0, Array.Empty<string>())
            }
        };
        var filter = new StreamingOutputFilter(fake);

        var emit = filter.Append(new string('e', 256));

        Assert.Equal(string.Empty, emit);
        Assert.Equal(0, fake.SanitizeCallCount);
    }

    [Fact]
    public void Append_OneCharBeyondBoundary_Emits()
    {
        var fake = new FakeFilter
        {
            ReturnValues = new List<ChatOutputFilterResult>
            {
                new("emit", 0, Array.Empty<string>())
            }
        };
        var filter = new StreamingOutputFilter(fake);

        var emit = filter.Append(new string('f', 257));

        Assert.Equal("emit", emit);
        Assert.Equal(1, fake.SanitizeCallCount);
    }

    [Fact]
    public void Append_NullOrEmpty_ReturnsEmpty()
    {
        var fake = new FakeFilter();
        var filter = new StreamingOutputFilter(fake);

        Assert.Equal(string.Empty, filter.Append(null!));
        Assert.Equal(string.Empty, filter.Append(string.Empty));

        Assert.Equal(0, fake.SanitizeCallCount);
    }

    [Fact]
    public void Constructor_NullFilter_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new StreamingOutputFilter(null!));
    }
}