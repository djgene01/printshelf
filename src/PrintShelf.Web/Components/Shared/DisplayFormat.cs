namespace PrintShelf.Web.Components.Shared;

/// <summary>Small display helpers shared by pages and components.</summary>
public static class DisplayFormat
{
    public static string Bytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024d;
        if (kb < 1024) return $"{kb:0.#} KB";
        double mb = kb / 1024d;
        if (mb < 1024) return $"{mb:0.##} MB";
        return $"{mb / 1024d:0.##} GB";
    }

    public static string Number(long value) => value.ToString("N0");

    public static string Date(DateTimeOffset value) => value.ToLocalTime().ToString("d MMM yyyy");

    public static string DateTime(DateTimeOffset value) => value.ToLocalTime().ToString("d MMM yyyy HH:mm");

    public static string Ago(DateTimeOffset value)
    {
        TimeSpan age = DateTimeOffset.UtcNow - value;
        if (age.TotalMinutes < 1) return "just now";
        if (age.TotalHours < 1) return $"{(int)age.TotalMinutes} min ago";
        if (age.TotalDays < 1) return $"{(int)age.TotalHours} h ago";
        if (age.TotalDays < 30) return $"{(int)age.TotalDays} d ago";
        return Date(value);
    }
}
