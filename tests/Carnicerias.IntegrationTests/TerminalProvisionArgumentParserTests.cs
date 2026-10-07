using Carnicerias.Bootstrap;

namespace Carnicerias.IntegrationTests;

public sealed class TerminalProvisionArgumentParserTests
{
    [Fact]
    public void AcceptsExactBranchAndNameWithoutASecretArgument()
    {
        var branchId = Guid.NewGuid();
        var parsed = TerminalProvisionArgumentParser.Parse(
            ["create-terminal", "--branch-id", branchId.ToString(), "--name", "Caja 1"]);

        Assert.Equal(branchId, parsed.BranchId);
        Assert.Equal("Caja 1", parsed.Name);
    }

    [Theory]
    [InlineData("create-terminal", "--branch-id", "not-a-guid", "--name", "Caja 1")]
    [InlineData("create-terminal", "--branch-id", "00000000-0000-0000-0000-000000000000", "--name", "Caja 1")]
    [InlineData("create-terminal", "--token", "plaintext", "--name", "Caja 1")]
    public void RejectsInvalidOrSecretArguments(params string[] args)
    {
        Assert.Throws<ArgumentException>(() => TerminalProvisionArgumentParser.Parse(args));
    }
}
