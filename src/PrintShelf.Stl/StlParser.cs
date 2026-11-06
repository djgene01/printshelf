using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace PrintShelf.Stl;

/// <summary>
/// Strict, dependency-free STL reader for binary and ASCII files
/// (U-01 default, docs/03-research-and-reuse.md §2).
///
/// Design rules:
///  * the byte layout wins over the extension and over header text, so a binary
///    file whose 80-byte header starts with "solid" is still read as binary;
///  * every rejection throws <see cref="StlParseException"/> with a specific code;
///  * stats are a single pass: bbox, signed volume, surface area, degenerate count.
/// </summary>
public static class StlParser
{
    public const long BinaryHeaderBytes = 84;
    public const int BinaryTriangleBytes = 50;

    /// <summary>Sanity cap: 20M triangles ≈ 1 GB of binary STL.</summary>
    public const uint MaxTriangles = 20_000_000;

    private const int SniffBytes = 512;

    /// <summary>Parse a file on disk. This is the path used by the upload pipeline.</summary>
    public static StlMesh ParseFile(string path)
    {
        using var fs = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1 << 16, FileOptions.SequentialScan);
        return Parse(fs);
    }

    /// <summary>Parse from the stream's current position to its end.</summary>
    public static StlMesh Parse(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        Stream source = stream;
        MemoryStream? buffered = null;
        try
        {
            if (!stream.CanSeek)
            {
                buffered = new MemoryStream();
                stream.CopyTo(buffered);
                buffered.Position = 0;
                source = buffered;
            }

            long start = source.Position;
            long length = source.Length - start;
            if (length == 0)
                throw new StlParseException(StlErrorCode.EmptyFile, "The file is empty (0 bytes).");

            byte[] head = ReadAt(source, start, (int)Math.Min(SniffBytes, length));
            bool asciiLooking = LooksAscii(head);

            if (length >= BinaryHeaderBytes)
            {
                byte[] prefix = ReadAt(source, start, (int)BinaryHeaderBytes);
                uint declared = BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(80, 4));
                long expected = BinaryHeaderBytes + (long)BinaryTriangleBytes * declared;

                // Exact binary size always wins — this is the "solid in the header" trap.
                if (expected == length)
                {
                    if (declared > MaxTriangles)
                        throw BadCount(declared);
                    return ParseBinary(source, start, declared);
                }

                if (asciiLooking)
                {
                    // Text that is not exactly a binary STL: let the ASCII reader
                    // produce the specific error (malformed/truncated).
                    return ParseAscii(source, start);
                }

                if (declared > MaxTriangles)
                    throw new StlParseException(
                        StlErrorCode.NotAnStl,
                        $"File is not an STL: the bytes at the binary triangle-count offset decode to an implausible {declared:N0} triangles.");

                if (expected > length)
                    throw new StlParseException(
                        StlErrorCode.Truncated,
                        $"Binary STL is truncated: header declares {declared:N0} triangles, which needs {expected:N0} bytes, but the file is {length:N0} bytes.");

                throw new StlParseException(
                    StlErrorCode.TrailingData,
                    $"Binary STL has {length - expected:N0} unexpected trailing bytes after {declared:N0} triangles.");
            }

            if (asciiLooking)
                return ParseAscii(source, start);

            throw new StlParseException(
                StlErrorCode.NotAnStl,
                "File is not an STL: no binary header and no ASCII 'solid' header were found.");
        }
        finally
        {
            buffered?.Dispose();
        }
    }

    private static StlParseException BadCount(uint declared) =>
        new(StlErrorCode.BadTriangleCount,
            $"Binary STL header declares an implausible triangle count ({declared:N0}; limit is {MaxTriangles:N0}).");

    /// <summary>Text sniff: UTF-8 BOM/whitespace then "solid", no NUL bytes.</summary>
    private static bool LooksAscii(byte[] head)
    {
        int i = 0;
        if (head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)
            i = 3;
        while (i < head.Length && (head[i] == ' ' || head[i] == '\t' || head[i] == '\r' || head[i] == '\n'))
            i++;

        if (i + 5 > head.Length)
            return false;
        ReadOnlySpan<byte> solid = "solid"u8;
        for (int k = 0; k < solid.Length; k++)
        {
            int c = head[i + k] | 0x20; // lowercase ASCII
            if (c != solid[k])
                return false;
        }

        for (int j = i; j < head.Length; j++)
        {
            byte b = head[j];
            if (b == 0)
                return false;
        }
        return true;
    }

    private static StlMesh ParseBinary(Stream stream, long start, uint triangleCount)
    {
        stream.Position = start + BinaryHeaderBytes;

        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
        double volume = 0, area = 0;
        int degenerate = 0;

        byte[] record = new byte[BinaryTriangleBytes];
        for (uint t = 0; t < triangleCount; t++)
        {
            ReadExactly(stream, record);
            // 0..11 normal (ignored: exporters are inconsistent), 12..47 vertices, 48..49 attribute.
            float ax = BinaryPrimitives.ReadSingleLittleEndian(record.AsSpan(12, 4));
            float ay = BinaryPrimitives.ReadSingleLittleEndian(record.AsSpan(16, 4));
            float az = BinaryPrimitives.ReadSingleLittleEndian(record.AsSpan(20, 4));
            float bx = BinaryPrimitives.ReadSingleLittleEndian(record.AsSpan(24, 4));
            float by = BinaryPrimitives.ReadSingleLittleEndian(record.AsSpan(28, 4));
            float bz = BinaryPrimitives.ReadSingleLittleEndian(record.AsSpan(32, 4));
            float cx = BinaryPrimitives.ReadSingleLittleEndian(record.AsSpan(36, 4));
            float cy = BinaryPrimitives.ReadSingleLittleEndian(record.AsSpan(40, 4));
            float cz = BinaryPrimitives.ReadSingleLittleEndian(record.AsSpan(44, 4));

            if (!float.IsFinite(ax) || !float.IsFinite(ay) || !float.IsFinite(az) ||
                !float.IsFinite(bx) || !float.IsFinite(by) || !float.IsFinite(bz) ||
                !float.IsFinite(cx) || !float.IsFinite(cy) || !float.IsFinite(cz))
            {
                throw new StlParseException(
                    StlErrorCode.NonFiniteVertex,
                    $"Triangle {t + 1:N0} has a non-finite vertex coordinate (NaN/Infinity).");
            }

            Accumulate(
                ax, ay, az, bx, by, bz, cx, cy, cz,
                ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ,
                ref volume, ref area, ref degenerate);
        }

        return Build((int)triangleCount, true, minX, minY, minZ, maxX, maxY, maxZ,
            volume, area, degenerate, new List<string>());
    }

    private static StlMesh ParseAscii(Stream stream, long start)
    {
        stream.Position = start;
        using var reader = new StreamReader(
            stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
            bufferSize: 1 << 16, leaveOpen: true);

        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
        double volume = 0, area = 0;
        int triangles = 0, degenerate = 0;
        int verticesInFacet = 0;
        bool sawSolid = false, sawEndSolid = false, inFacet = false;
        double[] v = new double[9];
        var warnings = new List<string>();
        long lineNo = 0;

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNo++;
            if (line.Length > 65_536)
                throw Malformed(lineNo, "line is implausibly long");

            string trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;

            // Keep tokens cheap: the grammar is one keyword plus up to four numbers.
            string[] parts = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            string keyword = parts[0].ToLowerInvariant();

            switch (keyword)
            {
                case "solid":
                    if (inFacet)
                        throw Malformed(lineNo, "'solid' inside a facet");
                    sawSolid = true;
                    break;

                case "facet":
                    if (inFacet)
                        throw Malformed(lineNo, "nested 'facet'");
                    inFacet = true;
                    verticesInFacet = 0;
                    break;

                case "outer":
                    // "outer loop" — the loop keyword itself carries no data.
                    break;

                case "vertex":
                    if (!inFacet)
                        throw Malformed(lineNo, "'vertex' outside a facet");
                    if (parts.Length != 4)
                        throw Malformed(lineNo, "'vertex' needs exactly three coordinates");
                    if (verticesInFacet >= 3)
                        throw Malformed(lineNo, "facet has more than three vertices");
                    if (!TryParseDouble(parts[1], out double x) ||
                        !TryParseDouble(parts[2], out double y) ||
                        !TryParseDouble(parts[3], out double z))
                        throw Malformed(lineNo, $"invalid vertex coordinate '{trimmed}'");
                    if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
                        throw new StlParseException(
                            StlErrorCode.NonFiniteVertex,
                            $"Line {lineNo:N0}: vertex has a non-finite coordinate (NaN/Infinity).");
                    v[verticesInFacet * 3] = x;
                    v[verticesInFacet * 3 + 1] = y;
                    v[verticesInFacet * 3 + 2] = z;
                    verticesInFacet++;
                    break;

                case "endloop":
                    break;

                case "endfacet":
                    if (!inFacet)
                        throw Malformed(lineNo, "'endfacet' without 'facet'");
                    if (verticesInFacet != 3)
                        throw Malformed(lineNo, $"facet has {verticesInFacet} vertices, expected 3");
                    Accumulate(
                        v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8],
                        ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ,
                        ref volume, ref area, ref degenerate);
                    triangles++;
                    inFacet = false;
                    verticesInFacet = 0;
                    break;

                case "endsolid":
                    sawEndSolid = true;
                    break;

                default:
                    throw Malformed(lineNo, $"unexpected keyword '{parts[0]}'");
            }
        }

        if (!sawSolid)
            throw new StlParseException(StlErrorCode.NotAnStl, "ASCII text does not start with a 'solid' header.");
        if (inFacet)
            throw new StlParseException(StlErrorCode.Truncated, "ASCII STL ends in the middle of a facet.");
        if (!sawEndSolid)
            warnings.Add("ASCII file has no 'endsolid' line (accepted).");

        return Build(triangles, false, minX, minY, minZ, maxX, maxY, maxZ,
            volume, area, degenerate, warnings);
    }

    private static StlMesh Build(
        int triangles, bool isBinary,
        double minX, double minY, double minZ,
        double maxX, double maxY, double maxZ,
        double volume, double area, int degenerate,
        List<string> warnings)
    {
        if (triangles == 0)
        {
            minX = minY = minZ = maxX = maxY = maxZ = 0;
            warnings.Add("Mesh contains no triangles.");
        }
        else
        {
            if (volume < 0)
                warnings.Add("Winding appears inverted (negative signed volume); the volume shown is the absolute value.");
            if (Math.Abs(volume) < 1e-9)
                warnings.Add("Estimated volume is ~0: the mesh is probably not watertight (common for downloaded STLs).");
        }

        if (degenerate > 0)
            warnings.Add($"{degenerate:N0} degenerate (zero-area) triangle(s) ignored for volume.");

        return new StlMesh(
            triangles, isBinary,
            minX, minY, minZ, maxX, maxY, maxZ,
            volume, area, degenerate, warnings);
    }

    private static void Accumulate(
        double ax, double ay, double az,
        double bx, double by, double bz,
        double cx, double cy, double cz,
        ref double minX, ref double minY, ref double minZ,
        ref double maxX, ref double maxY, ref double maxZ,
        ref double volume, ref double area, ref int degenerate)
    {
        if (ax < minX) minX = ax; if (ax > maxX) maxX = ax;
        if (ay < minY) minY = ay; if (ay > maxY) maxY = ay;
        if (az < minZ) minZ = az; if (az > maxZ) maxZ = az;

        if (bx < minX) minX = bx; if (bx > maxX) maxX = bx;
        if (by < minY) minY = by; if (by > maxY) maxY = by;
        if (bz < minZ) minZ = bz; if (bz > maxZ) maxZ = bz;

        if (cx < minX) minX = cx; if (cx > maxX) maxX = cx;
        if (cy < minY) minY = cy; if (cy > maxY) maxY = cy;
        if (cz < minZ) minZ = cz; if (cz > maxZ) maxZ = cz;

        double ux = bx - ax, uy = by - ay, uz = bz - az;
        double wx = cx - ax, wy = cy - ay, wz = cz - az;
        double nx = uy * wz - uz * wy;
        double ny = uz * wx - ux * wz;
        double nz = ux * wy - uy * wx;
        double doubledArea = Math.Sqrt(nx * nx + ny * ny + nz * nz);
        area += doubledArea * 0.5;

        if (doubledArea <= 1e-12)
        {
            degenerate++;
            return;
        }

        // Signed tetrahedron volume from the origin: V = dot(a, cross(b,c)) / 6.
        double crossX = by * cz - bz * cy;
        double crossY = bz * cx - bx * cz;
        double crossZ = bx * cy - by * cx;
        volume += (ax * crossX + ay * crossY + az * crossZ) / 6.0;
    }

    private static bool TryParseDouble(string s, out double value) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static StlParseException Malformed(long line, string detail) =>
        new(StlErrorCode.MalformedAscii, $"ASCII STL line {line:N0}: {detail}.");

    private static byte[] ReadAt(Stream stream, long position, int count)
    {
        stream.Position = position;
        byte[] buffer = new byte[count];
        ReadExactly(stream, buffer);
        return buffer;
    }

    private static void ReadExactly(Stream stream, byte[] buffer)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read == 0)
                throw new StlParseException(StlErrorCode.Truncated, "STL file ended unexpectedly.");
            offset += read;
        }
    }
}
