using Microsoft.Extensions.Options;

namespace HelpDesk.Infrastructure.Attachments;

public sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>Folder for attachments and the keys that sign sessions and survey links. Mount it as a volume.</summary>
    public string Path { get; set; } = "data";

    public long MaxFileBytes { get; set; } = 10 * 1024 * 1024;
}

/// <summary>Attachment bytes on disk, under random names so nothing a user sends decides a path.</summary>
public sealed class AttachmentStore(IOptions<StorageOptions> options)
{
    private string Root => System.IO.Path.GetFullPath(System.IO.Path.Combine(options.Value.Path, "attachments"));

    public async Task<string> SaveAsync(Stream content, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var key = $"{now:yyyy}/{now:MM}/{Guid.CreateVersion7(now):N}";
        var path = PathFor(key);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(file, cancellationToken);
        return key;
    }

    public Stream Open(string key) => new FileStream(PathFor(key), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

    public void Delete(string key)
    {
        var path = PathFor(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string PathFor(string key)
    {
        var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, key));
        if (!path.StartsWith(Root + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Ruta de adjunto inválida.");
        }

        return path;
    }
}

/// <summary>What may be uploaded and how it is served back.</summary>
public static class AttachmentRules
{
    public const int MaxFilesPerMessage = 5;

    // Files that run when opened on Windows; a help desk has no reason to pass them around.
    private static readonly HashSet<string> Blocked = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".com", ".scr", ".msi", ".msp", ".ps1", ".vbs", ".vbe", ".js", ".jse",
        ".wsf", ".wsh", ".hta", ".jar", ".lnk", ".reg", ".cpl", ".dll", ".pif", ".html", ".htm", ".svg",
    };

    // Shown inline as previews; everything else is served as a download.
    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/gif", "image/webp",
    };

    public static string? Problem(string fileName, long size, long maxBytes)
    {
        if (size <= 0)
        {
            return $"El archivo {fileName} está vacío.";
        }

        if (size > maxBytes)
        {
            return $"El archivo {fileName} pesa más de {maxBytes / (1024 * 1024)} MB.";
        }

        return Blocked.Contains(System.IO.Path.GetExtension(fileName))
            ? $"Por seguridad no se aceptan archivos {System.IO.Path.GetExtension(fileName)}."
            : null;
    }

    /// <summary>A plain name without folders or control characters, at most 200 characters.</summary>
    public static string CleanName(string? fileName)
    {
        var name = System.IO.Path.GetFileName((fileName ?? "").Replace('\\', '/'));
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (name.Length == 0)
        {
            name = "archivo";
        }

        return name.Length <= 200 ? name : name[..200];
    }

    public static bool IsImage(string contentType) => Images.Contains(contentType);

    /// <summary>Only images keep their type; the rest is served as bytes so a browser never runs it.</summary>
    public static string StoredType(string? contentType) =>
        contentType is not null && IsImage(contentType) ? contentType.ToLowerInvariant() : "application/octet-stream";
}
