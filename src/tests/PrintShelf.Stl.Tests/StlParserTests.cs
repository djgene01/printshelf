using System.Diagnostics;
using System.Text;
using PrintShelf.Stl;

namespace PrintShelf.Stl.Tests;

/// <summary>
/// Golden-fixture tests for the strict STL parser (docs/05-testing.md §1).
/// Fixtures are committed under fixtures/ and copied next to the test binary.
/// Regenerate with: python tools/make_fixtures.py
/// </summary>
public class StlParserTests
{
    private const double CubeVolume = 1000.0;

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", name);

    private static StlMesh ParseFixture(string name) => StlParser.ParseFile(Fixture(name));

    [Fact]
    public void Binary_cube_reports_exact_triangle_count_bbox_and_volume()
    {
        var mesh = ParseFixture("cube_binary.stl");

        Assert.True(mesh.IsBinary);
        Assert.Equal(12, mesh.TriangleCount);
        Assert.Equal(10.0, mesh.SizeX, 9);
        Assert.Equal(10.0, mesh.SizeY, 9);
        Assert.Equal(10.0, mesh.SizeZ, 9);
        Assert.Equal(CubeVolume, mesh.VolumeMm3, 9);
        Assert.Equal(600.0, mesh.SurfaceArea, 6);
        Assert.Equal(0, mesh.DegenerateTriangles);
        Assert.False(mesh.HasInvertedWinding);
        Assert.Empty(mesh.Warnings);
    }

    [Fact]
    public void Ascii_cube_agrees_with_binary_cube()
    {
        var binary = ParseFixture("cube_binary.stl");
        var ascii = ParseFixture("cube_ascii.stl");

        Assert.False(ascii.IsBinary);
        Assert.Equal(binary.TriangleCount, ascii.TriangleCount);
        Assert.Equal(binary.SizeX, ascii.SizeX, 6);
        Assert.Equal(binary.SizeY, ascii.SizeY, 6);
        Assert.Equal(binary.SizeZ, ascii.SizeZ, 6);
        Assert.Equal(binary.VolumeMm3, ascii.VolumeMm3, 6);
    }

    [Fact]
    public void Inverted_winding_is_flagged_and_volume_is_absolute()
    {
        var mesh = ParseFixture("cube_inverted.stl");

        Assert.True(mesh.HasInvertedWinding);
        Assert.Equal(-CubeVolume, mesh.SignedVolume, 6);
        Assert.Equal(CubeVolume, mesh.VolumeMm3, 6);
        Assert.Contains(mesh.Warnings, w => w.Contains("inverted", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Binary_file_with_solid_in_header_is_detected_as_binary()
    {
        // Classic false positive: 80-byte header starts with the word "solid".
        var mesh = ParseFixture("solid_binary_header.stl");

        Assert.True(mesh.IsBinary);
        Assert.Equal(12, mesh.TriangleCount);
        Assert.Equal(CubeVolume, mesh.VolumeMm3, 6);
    }

    [Fact]
    public void Truncated_binary_file_is_rejected_with_Truncated()
    {
        var ex = Assert.Throws<StlParseException>(() => ParseFixture("truncated_cube.stl"));
        Assert.Equal(StlErrorCode.Truncated, ex.Code);
        Assert.Contains("truncated", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Header_lying_about_triangle_count_is_rejected()
    {
        var ex = Assert.Throws<StlParseException>(() => ParseFixture("lying_header.stl"));
        Assert.True(ex.Code is StlErrorCode.Truncated or StlErrorCode.BadTriangleCount);
    }

    [Fact]
    public void NaN_vertex_is_rejected_with_NonFiniteVertex()
    {
        var ex = Assert.Throws<StlParseException>(() => ParseFixture("nan_vertex.stl"));
        Assert.Equal(StlErrorCode.NonFiniteVertex, ex.Code);
    }

    [Fact]
    public void Zero_triangle_binary_is_accepted_but_flagged()
    {
        var mesh = ParseFixture("zero_triangles.stl");

        Assert.Equal(0, mesh.TriangleCount);
        Assert.Equal(0.0, mesh.VolumeMm3);
        Assert.Contains(mesh.Warnings, w => w.Contains("no triangles", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Empty_file_is_rejected_with_EmptyFile()
    {
        var ex = Assert.Throws<StlParseException>(() => ParseFixture("empty.stl"));
        Assert.Equal(StlErrorCode.EmptyFile, ex.Code);
    }

    [Fact]
    public void Malformed_ascii_number_is_rejected_with_MalformedAscii()
    {
        var ex = Assert.Throws<StlParseException>(() => ParseFixture("ascii_malformed.stl"));
        Assert.Equal(StlErrorCode.MalformedAscii, ex.Code);
        Assert.Contains("line", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ascii_without_endsolid_is_accepted_with_warning()
    {
        var mesh = ParseFixture("ascii_no_endsolid.stl");

        Assert.Equal(12, mesh.TriangleCount);
        Assert.Contains(mesh.Warnings, w => w.Contains("endsolid", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Png_bytes_are_rejected_as_NotAnStl()
    {
        var ex = Assert.Throws<StlParseException>(() => ParseFixture("not_an_stl.bin"));
        Assert.Equal(StlErrorCode.NotAnStl, ex.Code);
    }

    [Fact]
    public void Non_seekable_stream_is_accepted()
    {
        byte[] bytes = File.ReadAllBytes(Fixture("cube_binary.stl"));
        using var stream = new NonSeekableStream(bytes);

        var mesh = StlParser.Parse(stream);

        Assert.Equal(12, mesh.TriangleCount);
        Assert.Equal(CubeVolume, mesh.VolumeMm3, 6);
    }

    [Fact]
    public void Hundred_thousand_triangles_parse_under_one_second()
    {
        const int segments = 224; // 224 * 224 * 2 = 100,352 triangles
        using var stream = new MemoryStream(BuildGridStl(segments));

        var sw = Stopwatch.StartNew();
        var mesh = StlParser.Parse(stream);
        sw.Stop();

        Assert.Equal(segments * segments * 2, mesh.TriangleCount);
        Assert.True(sw.ElapsedMilliseconds < 1000,
            $"100k-triangle parse took {sw.ElapsedMilliseconds} ms, budget is 1000 ms.");
    }

    [Fact]
    public void Volume_tolerance_on_cube_is_relative_1e6()
    {
        var mesh = ParseFixture("cube_binary.stl");
        double relativeError = Math.Abs(mesh.SignedVolume - CubeVolume) / CubeVolume;
        Assert.True(relativeError < 1e-6, $"relative error {relativeError:E3}");
    }

    /// <summary>Triangle strip of unit quads — only used for the performance budget.</summary>
    private static byte[] BuildGridStl(int segments)
    {
        int triangles = segments * segments * 2;
        using var ms = new MemoryStream(84 + triangles * 50 + 16);
        using var w = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);
        w.Write(new byte[80]);
        w.Write((uint)triangles);
        for (int i = 0; i < segments; i++)
        {
            for (int j = 0; j < segments; j++)
            {
                float x = i, y = j;
                WriteTri(w, x, y, 0, x + 1, y, 0, x + 1, y + 1, 0);
                WriteTri(w, x, y, 0, x + 1, y + 1, 0, x, y + 1, 0);
            }
        }
        w.Flush();
        return ms.ToArray();
    }

    private static void WriteTri(
        BinaryWriter w,
        float ax, float ay, float az,
        float bx, float by, float bz,
        float cx, float cy, float cz)
    {
        w.Write(0f); w.Write(0f); w.Write(1f);
        w.Write(ax); w.Write(ay); w.Write(az);
        w.Write(bx); w.Write(by); w.Write(bz);
        w.Write(cx); w.Write(cy); w.Write(cz);
        w.Write((ushort)0);
    }

    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
