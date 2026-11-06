using PrintShelf.Web.Services;

namespace PrintShelf.Web.Tests;

public sealed class SlicerLocatorTests
{
    [Fact]
    public void Detect_returns_all_supported_slicer_entries()
    {
        var slicers = new SlicerLocator(new SlicerOptions()).Detect();

        Assert.Equal(3, slicers.Count);
        Assert.Equal(["orca", "prusa", "cura"], slicers.Select(s => s.Key));
    }

    [Fact]
    public void Detect_uses_explicit_executable_paths()
    {
        string directory = Path.Combine(Path.GetTempPath(), "printshelf-slicer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string orca = Path.Combine(directory, "orca-slicer.exe");
            string prusa = Path.Combine(directory, "prusa-slicer.exe");
            string cura = Path.Combine(directory, "Cura.exe");
            File.WriteAllText(orca, "");
            File.WriteAllText(prusa, "");
            File.WriteAllText(cura, "");

            var locator = new SlicerLocator(new SlicerOptions
            {
                OrcaPath = orca,
                PrusaPath = prusa,
                CuraPath = cura
            });

            var slicers = locator.Detect().ToDictionary(s => s.Key);

            Assert.All(slicers.Values, s => Assert.True(s.Available));
            Assert.Equal(Path.GetFullPath(orca), slicers["orca"].ExecutablePath);
            Assert.Equal(Path.GetFullPath(prusa), slicers["prusa"].ExecutablePath);
            Assert.Equal(Path.GetFullPath(cura), slicers["cura"].ExecutablePath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
