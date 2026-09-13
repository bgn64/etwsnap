using EtwSnap.Contracts.Protocol;

namespace EtwSnap.UnitTests.Protocol;

public sealed class UserScopeNamesTests
{
    [Fact]
    public void PipeNamesSeparateUsersSessionsAndPrivilegeScopes()
    {
        var standard = UserScopeNames.PipeName("S-1-5-21-1", 12, HostPrivilegeScope.Standard);

        Assert.NotEqual(standard, UserScopeNames.PipeName("S-1-5-21-2", 12, HostPrivilegeScope.Standard));
        Assert.NotEqual(standard, UserScopeNames.PipeName("S-1-5-21-1", 13, HostPrivilegeScope.Standard));
        Assert.NotEqual(standard, UserScopeNames.PipeName("S-1-5-21-1", 12, HostPrivilegeScope.Elevated));
        Assert.StartsWith(ProtocolConstants.PipeNamePrefix, standard, StringComparison.Ordinal);
    }
}