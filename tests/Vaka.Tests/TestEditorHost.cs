using Vaka.Editor;
using Vaka.Layout;
using Vaka.Sensors;

namespace Vaka.Tests;

/// <summary>Editor host backed by a temp folder holding a copy of default.json.</summary>
internal sealed class TestEditorHost : IEditorHost, IDisposable
{
    public string Root { get; } = Directory.CreateTempSubdirectory("sp-editor-").FullName;
    public LayoutLibrary Library { get; }
    public ImageStore Images { get; }
    public List<string> PageList { get; set; } = ["default.json"];
    public int Seconds { get; set; } = 15;
    public string? Shown;
    public List<string> SavedNames { get; } = new();

    public TestEditorHost(SensorCatalog? catalog = null)
    {
        Library = new LayoutLibrary(Path.Combine(Root, "layouts"));
        Images = new ImageStore(Path.Combine(Root, "images"));
        Directory.CreateDirectory(Library.Directory);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "default.json"), Path.Combine(Library.Directory, "default.json"));
        Catalog = catalog;
    }

    public PagesInfo Pages => new(PageList, Seconds, Shown ?? PageList[0]);
    public void SetPages(IReadOnlyList<string> pages, int seconds) { PageList = pages.ToList(); Seconds = seconds; }
    public void ShowLayout(string name) => Shown = name;
    public void LayoutSaved(string name) => SavedNames.Add(name);
    public SensorCatalog? Catalog { get; }
    public IReadOnlyDictionary<string, double?> LastValues { get; } = new Dictionary<string, double?>();
    public SensorHistory History { get; } = new();
    public void MarkEditorActivity() { }
    public System.Drawing.Size? PanelSize { get; set; }
    public List<TimeSpan> FpsLeases { get; } = new();
    public void LeaseFps(TimeSpan duration) => FpsLeases.Add(duration);

    public void Dispose()
    {
        try { Directory.Delete(Root, true); } catch { /* best effort */ }
    }
}
