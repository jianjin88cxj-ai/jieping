using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Jieping.App.Models;
using Jieping.App.Recording;

namespace Jieping.App.Services;

public sealed class UpdatePackageDownloadService : IUpdatePackageDownloadService
{
    private static readonly HttpClient HttpClient = new();

    public string DownloadDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Jieping",
        "Updates");

    public async Task<UpdatePackageDownloadResult> DownloadAsync(
        UpdateCheckResult update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (!update.IsUpdateAvailable)
        {
            throw new VideoRecorderException("No update package is available to download.");
        }

        Directory.CreateDirectory(DownloadDirectory);
        var packageFileName = GetPackageFileName(update.PackageUrl);
        var destinationPath = Path.Combine(DownloadDirectory, packageFileName);
        var tempPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            await CopyPackageAsync(update.PackageUrl, tempPath, cancellationToken).ConfigureAwait(false);
            VerifySha256(tempPath, update.Sha256);
            File.Move(tempPath, destinationPath, overwrite: true);
            return new UpdatePackageDownloadResult(
                destinationPath,
                new FileInfo(destinationPath).Length,
                !string.IsNullOrWhiteSpace(update.Sha256));
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static async Task CopyPackageAsync(string packageUrl, string destinationPath, CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(packageUrl, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is "http" or "https")
            {
                await using var source = await HttpClient.GetStreamAsync(uri, cancellationToken).ConfigureAwait(false);
                await using var destination = File.Create(destinationPath);
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (uri.Scheme == Uri.UriSchemeFile)
            {
                await CopyLocalFileAsync(uri.LocalPath, destinationPath, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        await CopyLocalFileAsync(packageUrl, destinationPath, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CopyLocalFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
        {
            throw new VideoRecorderException("Update package was not found.");
        }

        await using var source = File.OpenRead(sourcePath);
        await using var destination = File.Create(destinationPath);
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    private static void VerifySha256(string packagePath, string? expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
        {
            return;
        }

        using var stream = File.OpenRead(packagePath);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new VideoRecorderException("Downloaded update package SHA256 does not match the manifest.");
        }
    }

    private static string GetPackageFileName(string packageUrl)
    {
        if (Uri.TryCreate(packageUrl, UriKind.Absolute, out var uri))
        {
            var name = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        var fallback = Path.GetFileName(packageUrl);
        return string.IsNullOrWhiteSpace(fallback)
            ? "Jieping-update-package.zip"
            : fallback;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
