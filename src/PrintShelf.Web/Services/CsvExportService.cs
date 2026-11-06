using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PrintShelf.Web.Data;

namespace PrintShelf.Web.Services;

/// <summary>
/// CSV export (P6, FR-10). UTF-8 **with BOM** so Excel opens it correctly,
/// RFC 4180 quoting, and formula-injection protection for text fields.
/// </summary>
public sealed class CsvExportService(IDbContextFactory<AppDbContext> dbFactory)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public async Task<byte[]> ModelsCsvAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Models.AsNoTracking()
            .OrderBy(m => m.Name)
            .Select(m => new
            {
                m.Name,
                m.Category,
                m.Description,
                m.FileBytes,
                m.TriCount,
                m.BboxX,
                m.BboxY,
                m.BboxZ,
                m.VolumeMm3,
                m.PrintUnit,
                m.FileSha256,
                m.CreatedAt,
                Tags = m.ModelTags.Select(mt => mt.Tag.Label).ToList(),
                Prints = m.PrintRuns.Count,
                Successes = m.PrintRuns.Count(r => r.Result == "success"),
                LastPrinted = m.PrintRuns.Max(r => (DateTimeOffset?)r.PrintedAt)
            })
            .ToListAsync(ct);

        var sb = new StringBuilder();
        Line(sb, "Name", "Category", "Tags", "Description", "FileBytes", "Triangles",
            "SizeX", "SizeY", "SizeZ", "Unit", "VolumeMm3", "Sha256", "Added",
            "PrintRuns", "SuccessRate", "LastPrinted");

        foreach (var r in rows)
        {
            Line(sb,
                Text(r.Name),
                Text(r.Category),
                Text(string.Join("; ", r.Tags)),
                Text(r.Description),
                r.FileBytes.ToString(Inv),
                r.TriCount.ToString(Inv),
                r.BboxX.ToString("0.###", Inv),
                r.BboxY.ToString("0.###", Inv),
                r.BboxZ.ToString("0.###", Inv),
                Text(r.PrintUnit),
                r.VolumeMm3?.ToString("0.###", Inv) ?? "",
                r.FileSha256.Trim(),
                r.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss", Inv),
                r.Prints.ToString(Inv),
                r.Prints == 0 ? "" : (r.Successes * 100.0 / r.Prints).ToString("0.0", Inv),
                r.LastPrinted?.ToString("yyyy-MM-dd HH:mm:ss", Inv) ?? "");
        }

        return WithBom(sb.ToString());
    }

    public async Task<byte[]> PrintRunsCsvAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.PrintRuns.AsNoTracking()
            .OrderByDescending(r => r.PrintedAt)
            .Select(r => new
            {
                Model = r.Model.Name,
                r.Printer,
                r.Material,
                r.Color,
                r.NozzleMm,
                r.LayerMm,
                r.DurationMin,
                r.Result,
                r.Cost,
                r.PrintedAt,
                r.Notes
            })
            .ToListAsync(ct);

        var sb = new StringBuilder();
        Line(sb, "Model", "Printer", "Material", "Color", "NozzleMm", "LayerMm",
            "DurationMin", "Result", "Cost", "PrintedAt", "Notes");

        foreach (var r in rows)
        {
            Line(sb,
                Text(r.Model),
                Text(r.Printer),
                Text(r.Material),
                Text(r.Color),
                r.NozzleMm?.ToString("0.##", Inv) ?? "",
                r.LayerMm?.ToString("0.###", Inv) ?? "",
                r.DurationMin?.ToString(Inv) ?? "",
                Text(r.Result),
                r.Cost?.ToString("0.00", Inv) ?? "",
                r.PrintedAt.ToString("yyyy-MM-dd HH:mm:ss", Inv),
                Text(r.Notes));
        }

        return WithBom(sb.ToString());
    }

    private static byte[] WithBom(string text)
    {
        byte[] body = Encoding.UTF8.GetBytes(text);
        byte[] bom = Encoding.UTF8.GetPreamble();
        byte[] result = new byte[bom.Length + body.Length];
        bom.CopyTo(result, 0);
        body.CopyTo(result, bom.Length);
        return result;
    }

    private static void Line(StringBuilder sb, params string[] fields)
    {
        for (int i = 0; i < fields.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(fields[i]);
        }
        sb.Append("\r\n");
    }

    /// <summary>Quote, double embedded quotes, and neutralise spreadsheet formulas.</summary>
    private static string Text(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        char first = value[0];
        if (first is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = "'" + value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
