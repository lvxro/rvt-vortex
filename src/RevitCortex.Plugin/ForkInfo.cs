namespace RevitCortex.Plugin;

/// <summary>
/// Where this fork (RVT Vortex) lives. Update checks and bug reports point
/// here instead of the original RevitCortex project, so users of the fork are
/// never offered the upstream build (which would replace the fork) and the
/// original author doesn't receive reports about code he doesn't maintain.
/// </summary>
internal static class ForkInfo
{
    public const string ProductName = "RVT Vortex";
    public const string Repo = "lvxro/rvt-vortex";
    public const string RepoUrl = "https://github.com/" + Repo;

    /// <summary>Update manifest written by .github/workflows/release.yml.</summary>
    public const string ManifestUrl = "https://raw.githubusercontent.com/" + Repo + "/main/latest.json";

    public const string NewIssueUrl = RepoUrl + "/issues/new";
}
