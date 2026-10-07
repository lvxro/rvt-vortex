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

    /// <summary>
    /// Error telemetry is off in this fork. The telemetry stack inherited from
    /// RevitCortex posts to the original project's ingest service, which this
    /// fork neither runs nor has any agreement with: its users' error reports
    /// would go to a third party, tagged with fork version numbers the
    /// original author cannot match to his own releases. While this is false
    /// nothing is queued or sent, the first-run consent dialog never shows and
    /// the Settings toggle is hidden. Only turn it on together with an endpoint
    /// this fork operates (CortexEnvironment.DefaultTelemetryEndpoint).
    /// (static readonly rather than const, so the guarded code still compiles
    /// without unreachable-code warnings.)
    /// </summary>
    public static readonly bool TelemetryEnabled = false;
}
