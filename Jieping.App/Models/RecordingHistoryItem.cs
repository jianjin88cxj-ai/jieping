using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Jieping.App.Models;

public sealed class RecordingHistoryItem
{
    public RecordingHistoryItem(string path, string artifactType, DateTimeOffset createdAt, string details)
    {
        Path = path;
        RawArtifactType = artifactType;
        CreatedAt = createdAt;
        RawDetails = details;
    }

    public string Path { get; }

    public string RawArtifactType { get; }

    public DateTimeOffset CreatedAt { get; }

    public string RawDetails { get; }

    public string ArtifactType => LocalizeArtifactType(RawArtifactType);

    public string Details => LocalizeDetails(RawDetails);

    public string FileName => System.IO.Path.GetFileName(Path);

    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;

    public string CreatedAtDisplay => CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");

    public bool Exists => File.Exists(Path);

    private static bool IsChineseUi => CultureInfo.CurrentUICulture.Name.Equals("zh-CN", StringComparison.OrdinalIgnoreCase);

    private static string LocalizeArtifactType(string value)
    {
        if (!IsChineseUi)
        {
            return value switch
            {
                "Trimmed MP4" => "Trimmed MP4",
                "File" => "File",
                _ => value
            };
        }

        return value switch
        {
            "Trimmed MP4" => "裁剪 MP4",
            "File" => "文件",
            _ => value
        };
    }

    private static string LocalizeDetails(string value)
    {
        if (!IsChineseUi)
        {
            return value switch
            {
                "Region, 30 FPS" => value,
                _ => value.Replace("FullScreen", "Full Screen", StringComparison.Ordinal)
            };
        }

        var trimMatch = Regex.Match(value, @"^Trimmed (?<start>[0-9.]+)s start, (?<end>[0-9.]+)s end$");
        if (trimMatch.Success)
        {
            return $"裁剪：开始 {trimMatch.Groups["start"].Value} 秒，结束 {trimMatch.Groups["end"].Value} 秒";
        }

        var gifMatch = Regex.Match(value, @"^(?<fps>[0-9]+) FPS, max width (?<width>[0-9]+)px$");
        if (gifMatch.Success)
        {
            return $"{gifMatch.Groups["fps"].Value} FPS，最大宽度 {gifMatch.Groups["width"].Value}px";
        }

        var recordingMatch = Regex.Match(value, @"^(?<mode>Region|Window|FullScreen|Full Screen), (?<fps>[0-9]+) FPS$");
        if (recordingMatch.Success)
        {
            var mode = recordingMatch.Groups["mode"].Value switch
            {
                "Region" => "区域",
                "Window" => "窗口",
                "FullScreen" or "Full Screen" => "全屏",
                _ => recordingMatch.Groups["mode"].Value
            };
            return $"{mode}，{recordingMatch.Groups["fps"].Value} FPS";
        }

        return value;
    }
}
