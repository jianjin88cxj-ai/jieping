using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Text.Json;
using Jieping.App.Models;
using Jieping.App.Recording;

namespace Jieping.App.Services;

public sealed class ManifestUpdateCheckService : IUpdateCheckService
{
    private static readonly HttpClient HttpClient = new();
    private static readonly Regex Sha256Regex = new("^[a-fA-F0-9]{64}$", RegexOptions.Compiled);

    public async Task<UpdateCheckResult> CheckAsync(
        string manifestLocation,
        Version currentVersion,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(manifestLocation))
        {
            throw new VideoRecorderException("Enter an update manifest URL or local manifest path.");
        }

        var json = await ReadManifestAsync(manifestLocation, cancellationToken).ConfigureAwait(false);
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new VideoRecorderException("Update manifest is empty or invalid.");

        if (string.IsNullOrWhiteSpace(manifest.Version) ||
            !Version.TryParse(manifest.Version, out var latestVersion))
        {
            throw new VideoRecorderException("Update manifest version is missing or invalid.");
        }

        if (!string.IsNullOrWhiteSpace(manifest.AppId) &&
            !string.Equals(manifest.AppId, "Jieping", StringComparison.OrdinalIgnoreCase))
        {
            throw new VideoRecorderException("Update manifest appId does not match Jieping.");
        }

        if (!string.IsNullOrWhiteSpace(manifest.Runtime) &&
            !string.Equals(manifest.Runtime, "win-x64", StringComparison.OrdinalIgnoreCase))
        {
            throw new VideoRecorderException("Update manifest runtime does not match this package.");
        }

        if (!string.IsNullOrWhiteSpace(manifest.Sha256) && !Sha256Regex.IsMatch(manifest.Sha256))
        {
            throw new VideoRecorderException("Update manifest sha256 is invalid.");
        }

        var packageUrl = string.IsNullOrWhiteSpace(manifest.PackageUrl)
            ? manifest.DownloadUrl
            : manifest.PackageUrl;
        if (string.IsNullOrWhiteSpace(packageUrl))
        {
            throw new VideoRecorderException("Update manifest package URL is missing.");
        }

        return new UpdateCheckResult(
            currentVersion,
            latestVersion,
            packageUrl,
            manifest.Sha256,
            manifest.ReleaseNotes,
            manifest.PublishedAt,
            manifest.FileName,
            manifest.SizeBytes,
            manifest.PackageType);
    }

    private static async Task<string> ReadManifestAsync(string manifestLocation, CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(manifestLocation, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is "http" or "https")
            {
                return await HttpClient.GetStringAsync(uri, cancellationToken).ConfigureAwait(false);
            }

            if (uri.Scheme == Uri.UriSchemeFile)
            {
                return await File.ReadAllTextAsync(uri.LocalPath, cancellationToken).ConfigureAwait(false);
            }
        }

        if (!File.Exists(manifestLocation))
        {
            throw new VideoRecorderException("Update manifest was not found.");
        }

        return await File.ReadAllTextAsync(manifestLocation, cancellationToken).ConfigureAwait(false);
    }

    private sealed class UpdateManifest
    {
        public string? AppId { get; set; }

        public string? Runtime { get; set; }

        public string? Version { get; set; }

        public string? PackageUrl { get; set; }

        public string? DownloadUrl { get; set; }

        public string? Sha256 { get; set; }

        public string? FileName { get; set; }

        public long? SizeBytes { get; set; }

        public string? PackageType { get; set; }

        public string? ReleaseNotes { get; set; }

        public DateTimeOffset? PublishedAt { get; set; }
    }
}
