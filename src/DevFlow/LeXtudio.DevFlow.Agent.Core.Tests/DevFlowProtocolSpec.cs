using System.Text.RegularExpressions;

namespace LeXtudio.DevFlow.Agent.Core.Tests;

/// <summary>
/// Reads the DevFlow protocol contract from the OpenAPI document that ships in the pinned
/// <c>maui-labs</c> submodule at <c>docs/DevFlow/spec/openapi.yaml</c>.
/// </summary>
/// <remarks>
/// Upstream states in that directory's README that the spec "is intended to stay framework-agnostic so
/// the same DevFlow contract can be implemented across MAUI and other UI stacks", which makes it the
/// authority for what a non-MAUI agent such as <c>LeXtudio.DevFlow.Agent</c> should expose.
/// </remarks>
internal static partial class DevFlowProtocolSpec
{
    [GeneratedRegex(@"^  (/api/v1/\S*):\s*$", RegexOptions.Multiline)]
    private static partial Regex PathLine();

    [GeneratedRegex(@"^    (get|post|put|delete|patch):\s*$", RegexOptions.Multiline)]
    private static partial Regex MethodLine();

    internal static string SpecRoot { get; } = ResolveSpecRoot();

    /// <summary>
    /// Returns the contract surface as method + path pairs, for example <c>("get", "/api/v1/ui/tree")</c>.
    /// </summary>
    internal static IReadOnlySet<string> GetDeclaredOperations()
    {
        var openApiPath = Path.Combine(SpecRoot, "openapi.yaml");
        if (!File.Exists(openApiPath))
        {
            throw new FileNotFoundException(
                $"The pinned DevFlow protocol contract was not found at '{openApiPath}'. Bump the maui-labs submodule or fix the spec copy in the test project.",
                openApiPath);
        }

        var operations = new HashSet<string>(StringComparer.Ordinal);
        var currentPath = (string?)null;

        foreach (var line in File.ReadLines(openApiPath))
        {
            var pathMatch = PathLine().Match(line);
            if (pathMatch.Success)
            {
                currentPath = pathMatch.Groups[1].Value;
                continue;
            }

            var methodMatch = MethodLine().Match(line);
            if (methodMatch.Success && currentPath is not null)
            {
                operations.Add($"{methodMatch.Groups[1].Value.ToLowerInvariant()} {currentPath}");
            }
        }

        if (operations.Count == 0)
        {
            throw new InvalidOperationException($"No operations were parsed from '{openApiPath}'.");
        }

        return operations;
    }

    internal static IReadOnlySet<string> ToOperations(IEnumerable<(string Method, string Path)> routes)
        => routes.Select(r => $"{r.Method.ToLowerInvariant()} {r.Path}").ToHashSet(StringComparer.Ordinal);

    private static string ResolveSpecRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "spec", "openapi.yaml");
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate)!;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the copied DevFlow spec directory starting from '{AppContext.BaseDirectory}'.");
    }
}