using System.Reflection;

namespace Vaka.App;

/// <summary>The Vaka mark (vaka.ico, made by scripts/make-icons.ps1 from branding/vaka.svg), also the exe's icon.</summary>
internal static class TrayIconFactory
{
    /// <summary>The icon at the system's small-icon size (16 px at 100% scaling, 20 at 125%, ...).</summary>
    public static Icon Create() => Create(SystemInformation.SmallIconSize.Width);

    public static Icon Create(int size)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("vaka.ico")
            ?? throw new InvalidOperationException("vaka.ico is not embedded");
        return new Icon(stream, size, size);
    }
}
