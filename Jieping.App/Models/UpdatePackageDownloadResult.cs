namespace Jieping.App.Models;

public sealed class UpdatePackageDownloadResult
{
    public UpdatePackageDownloadResult(string packagePath, long sizeBytes, bool sha256Verified)
    {
        PackagePath = packagePath;
        SizeBytes = sizeBytes;
        Sha256Verified = sha256Verified;
    }

    public string PackagePath { get; }

    public long SizeBytes { get; }

    public bool Sha256Verified { get; }
}
