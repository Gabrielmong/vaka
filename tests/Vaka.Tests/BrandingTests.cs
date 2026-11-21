using Xunit;

namespace Vaka.Tests;

/// <summary>One mark everywhere: branding/vaka.svg is copied to the site and editor, and drawn into vaka.ico.</summary>
public class BrandingTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Vaka.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    [Theory]
    [InlineData("site/public/favicon.svg")]
    [InlineData("src/editor-ui/public/favicon.svg")]
    public void FaviconsAreTheBrandMark(string copy)
    {
        var root = RepoRoot();
        Assert.Equal(File.ReadAllText(Path.Combine(root, "branding", "vaka.svg")), File.ReadAllText(Path.Combine(root, copy)));
    }

    [Fact]
    public void TrayIconIsTheBrandIcon()
    {
        using var icon = Vaka.App.TrayIconFactory.Create(16);
        Assert.Equal(16, icon.Width);
        using var bmp = icon.ToBitmap();
        Assert.Equal(System.Drawing.Color.FromArgb(255, 0x4a, 0xa3, 0xff), bmp.GetPixel(2, 3));   // top of the frame outline
        Assert.Equal(0, bmp.GetPixel(0, 0).A);                                                   // transparent corner
    }
}
