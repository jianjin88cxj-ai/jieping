namespace Jieping.App.Models;

public sealed class UpdateCheckResult
{
    public UpdateCheckResult(
        Version currentVersion,
        Version latestVersion,
        string packageUrl,
        string? sha256,
        string? releaseNotes,
        DateTimeOffset? publishedAt,
        string? fileName = null,
        long? sizeBytes = null,
        string? packageType = null)
    {
        CurrentVersion = currentVersion;
        LatestVersion = latestVersion;
        PackageUrl = packageUrl;
        Sha256 = sha256;
        ReleaseNotes = releaseNotes;
        PublishedAt = publishedAt;
        FileName = fileName;
        SizeBytes = sizeBytes;
        PackageType = packageType;
    }

    public Version CurrentVersion { get; }

    public Version LatestVersion { get; }

    public string PackageUrl { get; }

    public string? Sha256 { get; }

    public string? ReleaseNotes { get; }

    public DateTimeOffset? PublishedAt { get; }

    public string? FileName { get; }

    public long? SizeBytes { get; }

    public string? PackageType { get; }

    public bool IsUpdateAvailable => LatestVersion > CurrentVersion;

    public string Summary => IsUpdateAvailable
        ? $"Version {LatestVersion} is available."
        : $"Jieping is up to date at version {CurrentVersion}.";
}
