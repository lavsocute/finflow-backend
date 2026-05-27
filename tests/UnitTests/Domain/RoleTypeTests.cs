using FinFlow.Domain.Enums;

namespace FinFlow.UnitTests.Domain;

public sealed class RoleTypeTests
{
    [Fact]
    public void RoleType_DoesNotParseUnsupportedGuestRole()
    {
        var parsed = Enum.TryParse<RoleType>("Guest", out var role);

        Assert.False(parsed);
        Assert.Equal(default, role);
    }
}
