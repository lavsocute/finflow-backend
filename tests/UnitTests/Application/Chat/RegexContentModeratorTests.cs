using FinFlow.Application.Chat.Services;

namespace FinFlow.UnitTests.Application.Chat;

public sealed class RegexContentModeratorTests
{
    private readonly RegexContentModerator _moderator = new();

    [Fact]
    public void Moderate_AllowsNormalQuery()
    {
        Assert.Null(_moderator.Moderate("What is my total spending in March?"));
        Assert.Null(_moderator.Moderate("Show me top vendors by amount"));
    }

    [Fact]
    public void Moderate_RejectsThreat()
    {
        Assert.Equal("threat", _moderator.Moderate("I will kill you for this!"));
    }

    [Fact]
    public void Moderate_RejectsNsfw()
    {
        Assert.Equal("nsfw", _moderator.Moderate("Show me porn pictures"));
    }

    [Fact]
    public void Moderate_RejectsHate()
    {
        Assert.Equal("hate", _moderator.Moderate("you are a retard"));
    }

    [Fact]
    public void Moderate_RejectsVietnameseThreat()
    {
        Assert.Equal("threat", _moderator.Moderate("tao sẽ giết mày"));
    }

    [Fact]
    public void Moderate_AllowsEmpty()
    {
        Assert.Null(_moderator.Moderate(string.Empty));
        Assert.Null(_moderator.Moderate(null!));
    }
}
