using System.Text.RegularExpressions;
using SkiaSharp;

namespace Vaka.Layout;

public sealed record ImageInfo(string Name, int Width, int Height, long Bytes);

/// <summary>
/// Pictures used by image elements. They only ever enter through <see cref="Import"/>, which
/// decodes and re-encodes them, and are addressed by a plain file name inside one folder.
/// </summary>
public sealed partial class ImageStore(string directory)
{
    public const int MaxUploadBytes = 20 * 1024 * 1024;
    public const int MaxDimension = 4096;

    public string Directory { get; } = directory;

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9 _.-]{0,79}\.(png|jpg)$")]
    private static partial Regex NameRegex();

    public static bool IsValidName(string? name) =>
        name is not null && NameRegex().IsMatch(name) && !name.Contains("..");

    /// <summary>Full path for a valid name, otherwise null.</summary>
    public string? PathFor(string? name) => IsValidName(name) ? Path.Combine(Directory, name!) : null;

    public IReadOnlyList<ImageInfo> List()
    {
        if (!System.IO.Directory.Exists(Directory)) return [];
        var list = new List<ImageInfo>();
        foreach (var file in new DirectoryInfo(Directory).EnumerateFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!IsValidName(file.Name)) continue;
            using var codec = SKCodec.Create(file.FullName);
            if (codec is null) continue;
            list.Add(new ImageInfo(file.Name, codec.Info.Width, codec.Info.Height, file.Length));
        }
        return list;
    }

    /// <summary>
    /// Decodes <paramref name="data"/>, shrinks it to <see cref="MaxDimension"/> and stores it as
    /// PNG (or JPEG for JPEG input) under a unique name derived from <paramref name="suggestedName"/>.
    /// </summary>
    public string Import(byte[] data, string? suggestedName)
    {
        if (data.Length == 0 || data.Length > MaxUploadBytes)
            throw new InvalidDataException($"Images must be 1 byte to {MaxUploadBytes / 1024 / 1024} MB.");

        using var codec = SKCodec.Create(new SKMemoryStream(data))
            ?? throw new InvalidDataException("Not a supported image (PNG, JPEG, WebP, GIF, BMP).");
        bool jpeg = codec.EncodedFormat == SKEncodedImageFormat.Jpeg;
        using var decoded = SKBitmap.Decode(codec)
            ?? throw new InvalidDataException("The image couldn't be decoded.");

        var bitmap = decoded;
        SKBitmap? resized = null;
        float shrink = Math.Min(1f, (float)MaxDimension / Math.Max(decoded.Width, decoded.Height));
        if (shrink < 1f)
        {
            resized = decoded.Resize(new SKImageInfo(Math.Max(1, (int)(decoded.Width * shrink)), Math.Max(1, (int)(decoded.Height * shrink))),
                new SKSamplingOptions(SKCubicResampler.Mitchell));
            bitmap = resized ?? decoded;
        }

        using var encoded = bitmap.Encode(jpeg ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png, jpeg ? 92 : 100);
        resized?.Dispose();

        System.IO.Directory.CreateDirectory(Directory);
        var name = UniqueName(Stem(suggestedName), jpeg ? ".jpg" : ".png");
        var path = Path.Combine(Directory, name);
        File.WriteAllBytes(path + ".tmp", encoded.ToArray());
        File.Move(path + ".tmp", path, overwrite: false);
        return name;
    }

    public bool Delete(string name)
    {
        var path = PathFor(name);
        if (path is null || !File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    private static string Stem(string? suggested)
    {
        var stem = Path.GetFileNameWithoutExtension(Path.GetFileName(suggested ?? ""));
        var clean = new string(stem.Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or ' ' ? c : '-').ToArray()).Trim(' ', '-', '.');
        if (clean.Length == 0 || !char.IsAsciiLetterOrDigit(clean[0])) clean = "image" + (clean.Length > 0 ? "-" + clean : "");
        return clean.Length > 60 ? clean[..60].TrimEnd(' ', '-') : clean;
    }

    private string UniqueName(string stem, string ext)
    {
        var name = stem + ext;
        for (int i = 2; File.Exists(Path.Combine(Directory, name)); i++) name = $"{stem}-{i}{ext}";
        return name;
    }
}
