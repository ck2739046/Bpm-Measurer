using System.IO;

namespace BpmMeasurer;

/// <summary>
/// 把一次拖放的路径集合归类为「一个音频文件 + 一个配置文件」。
/// 纯静态、无窗口状态,便于单独测试。
/// </summary>
public static class DroppedFiles
{
    /// <summary>配置文件扩展名(只认 .txt)。</summary>
    public const string ConfigExtension = ".txt";

    /// <summary>
    /// 已知音频/视频容器扩展名,仅用于在多个候选中挑出最可能的一个;
    /// 未命中者仍可作为候选,能否解码交由 BASS 判定。
    /// </summary>
    private static readonly string[] AudioExtensions =
    {
        ".mp3", ".mp2", ".mp1", ".wav", ".ogg", ".oga", ".flac", ".aac", ".m4a",
        ".m4b", ".mp4", ".opus", ".mka", ".mkv", ".webm", ".aiff", ".aif"
    };

    /// <summary>
    /// 归类拖入的路径。音频取拖入顺序中第一个已知音频扩展名,否则回退第一个非 .txt 文件;
    /// 配置优先取与所选音频同主名的 .txt,否则取第一个 .txt。目录与空路径跳过。
    /// </summary>
    public static (string? Audio, string? Config) Resolve(IEnumerable<string> paths)
    {
        var files = paths
            .Where(p => !string.IsNullOrWhiteSpace(p) && !Directory.Exists(p))
            .ToList();

        var configs = files.Where(IsConfig).ToList();
        var audio = files.FirstOrDefault(IsKnownAudio) ?? files.FirstOrDefault(p => !IsConfig(p));
        if (audio is null)
            return (null, configs.FirstOrDefault());

        var config = configs.FirstOrDefault(c => SameBaseName(c, audio)) ?? configs.FirstOrDefault();
        return (audio, config);
    }

    private static bool IsConfig(string path) =>
        string.Equals(Path.GetExtension(path), ConfigExtension, StringComparison.OrdinalIgnoreCase);

    private static bool IsKnownAudio(string path) =>
        AudioExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static bool SameBaseName(string a, string b) =>
        string.Equals(Path.GetFileNameWithoutExtension(a), Path.GetFileNameWithoutExtension(b),
            StringComparison.OrdinalIgnoreCase);
}
