namespace Carnicerias.Bootstrap;

public sealed record TerminalProvisionArguments(Guid BranchId, string Name);

public static class TerminalProvisionArgumentParser
{
    private const string Usage =
        "Usage: Carnicerias.Bootstrap create-terminal --branch-id <guid> --name <name>.";

    public static TerminalProvisionArguments Parse(IReadOnlyList<string> args)
    {
        if (args.Count != 5 || args[0] != "create-terminal")
            throw new ArgumentException(Usage);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Count; index += 2)
        {
            if (args[index] is not ("--branch-id" or "--name") ||
                !values.TryAdd(args[index], args[index + 1]))
                throw new ArgumentException(Usage);
        }

        if (!values.TryGetValue("--branch-id", out var branchValue) ||
            !Guid.TryParse(branchValue, out var branchId) || branchId == Guid.Empty ||
            !values.TryGetValue("--name", out var name) || string.IsNullOrWhiteSpace(name) ||
            name.Trim().Length > 120)
            throw new ArgumentException(Usage);

        return new TerminalProvisionArguments(branchId, name.Trim());
    }
}
