using System;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using FloatTodo.Services;

namespace FloatTodo.Services;

/// <summary>
/// 可选的用户本地 Codex V1/V2 PNG 图集。仅使用 idle / waiting / review 行。
/// 图片需由用户自行提供，不随 FloatTodo 分发任何第三方图像。
/// </summary>
public sealed class SkinAtlas
{
    public const int CellWidth = 192;
    public const int CellHeight = 208;
    private readonly BitmapSource _atlas;

    public static string StoragePath => Path.Combine(AppPaths.DataDir, "skins", "black-cat", "spritesheet.png");

    private SkinAtlas(BitmapSource atlas) => _atlas = atlas;

    public static bool TryOpen(string path, out SkinAtlas? atlas, out string? error)
    {
        atlas = null;
        error = null;
        try
        {
            if (!File.Exists(path)) { error = "图片文件不存在。"; return false; }
            if (!string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            { error = "目前只支持 PNG 图集，请将 WebP 转换为 PNG 后导入。"; return false; }
            using var stream = File.OpenRead(path);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            // 8列×9行或8列×11行；单格192×208。
            if (bitmap.PixelWidth != 1536 || (bitmap.PixelHeight != 1872 && bitmap.PixelHeight != 2288))
            {
                error = $"图集尺寸为 {bitmap.PixelWidth}×{bitmap.PixelHeight}，要求 1536×1872 或 1536×2288。";
                return false;
            }
            atlas = new SkinAtlas(bitmap);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Runtime.InteropServices.COMException)
        {
            error = "无法读取该 PNG：" + ex.Message;
            return false;
        }
    }

    public BitmapSource Frame(int row, int col)
    {
        if (row is not (0 or 6 or 8) || col < 0 || col >= 6)
            throw new ArgumentOutOfRangeException(nameof(row));
        var cropped = new CroppedBitmap(_atlas,
            new Int32Rect(col * CellWidth, row * CellHeight, CellWidth, CellHeight));
        cropped.Freeze();
        return cropped;
    }
}
