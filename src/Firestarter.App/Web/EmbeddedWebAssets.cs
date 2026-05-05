using System.Text;

namespace Firestarter.App.Web;

public static class EmbeddedWebAssets
{
    const string Scheme = "app";

    public static string StartUrl => $"{Scheme}://app/index.html";

    static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".htm"] = "text/html; charset=utf-8",
        [".js"] = "application/javascript; charset=utf-8",
        [".mjs"] = "application/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".map"] = "application/json; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".ico"] = "image/x-icon",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".ttf"] = "font/ttf",
        [".otf"] = "font/otf",
        [".txt"] = "text/plain; charset=utf-8",
    };

    public static Stream Handle(object sender, string scheme, string url, out string contentType)
    {
        _ = sender;
        _ = scheme;
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "wwwroot"));
        var relative = ExtractPath(url);
        if (string.IsNullOrEmpty(relative) || relative == "/") relative = "/index.html";

        var resolved = Path.GetFullPath(Path.Combine(root, relative.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(resolved))
            return NotFound(out contentType);

        contentType = ContentTypes.TryGetValue(Path.GetExtension(resolved), out var ct) ? ct : "application/octet-stream";
        return File.OpenRead(resolved);
    }

    static string ExtractPath(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "/index.html";
        return string.IsNullOrEmpty(uri.AbsolutePath) ? "/index.html" : uri.AbsolutePath;
    }

    static MemoryStream NotFound(out string contentType)
    {
        contentType = "text/plain; charset=utf-8";
        return new MemoryStream(Encoding.UTF8.GetBytes("404"));
    }
}
