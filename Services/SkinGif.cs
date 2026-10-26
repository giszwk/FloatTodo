using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media.Imaging;

namespace FloatTodo.Services;

/// <summary>
/// 用户自行导入 GIF；WPF 解码并缓存帧，只在低频触发时播放一次，避免长时间循环干扰。
/// 提供尺寸、帧数、文件大小限制以防止桌面常驻应用内存增长。
/// </summary>
public sealed class SkinGif
{
    private const long MaxFileSizeBytes = 20 * 1024 * 1024;
    public static string FullPath => Path.Combine(AppPaths.DataDir, "skins", "black-cat", "full.gif");
    public static string PeekPath => Path.Combine(AppPaths.DataDir, "skins", "black-cat", "peek.gif");

    public IReadOnlyList<BitmapSource> Frames { get; }
    public IReadOnlyList<TimeSpan> Durations { get; }

    private SkinGif(List<BitmapSource> frames, List<TimeSpan> durations)
    {
        Frames = frames;
        Durations = durations;
    }

    public static bool TryOpen(string path, out SkinGif? animation, out string? error)
    {
        animation = null;
        error = null;
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists) { error = "找不到 GIF 文件。"; return false; }
            if (!string.Equals(file.Extension, ".gif", StringComparison.OrdinalIgnoreCase))
            { error = "请选择 .gif 动画文件。"; return false; }
            if (file.Length > MaxFileSizeBytes)
            { error = "GIF 不能超过 20 MB。"; return false; }
            using var stream = File.OpenRead(path);
            var decoder = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count < 1 || decoder.Frames.Count > 64)
            { error = "GIF 必须在 1–64 帧之间。"; return false; }
            var frames = new List<BitmapSource>();
            var delays = new List<TimeSpan>();
            foreach (var frame in decoder.Frames)
            {
                if (frame.PixelWidth > 640 || frame.PixelHeight > 640 || frame.PixelWidth < 1 || frame.PixelHeight < 1)
                { error = "GIF 单帧尺寸不能超过 640×640。"; return false; }
                frame.Freeze();
                frames.Add(frame);
                int durationMs = 150;
                try
                {
                    // BitmapFrame.Metadata 的静态类型是 ImageMetadata；
                    // GIF 的 GetQuery 方法属于派生类型 BitmapMetadata。
                    if (frame.Metadata is BitmapMetadata gifMetadata)
                    {
                        var delay = gifMetadata.GetQuery("/grctlext/Delay");
                        if (delay is ushort value) durationMs = value * 10;
                        else if (delay is byte b) durationMs = b * 10;
                    }
                }
                catch (Exception) { /* 未包含时长元数据时使用默认值 */ }
                delays.Add(TimeSpan.FromMilliseconds(Math.Clamp(durationMs, 90, 550)));
            }
            animation = new SkinGif(frames, delays);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException
                                or ArgumentException or System.Runtime.InteropServices.COMException)
        {
            error = "无法读取 GIF：" + ex.Message;
            return false;
        }
    }
}
