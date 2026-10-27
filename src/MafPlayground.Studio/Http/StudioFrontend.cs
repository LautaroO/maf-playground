using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace MafPlayground.Studio.Http;

/// <summary>Local compiled assets only; install after UseStudioAccess in the dedicated host.</summary>
public static class StudioFrontend
{
    public static bool UseStudioFrontend(this WebApplication app, string assetsDirectory)
    {
        string index = Path.Combine(Path.GetFullPath(assetsDirectory), "index.html");
        if (!File.Exists(index))
        {
            app.MapGet("/studio", MissingAssets);
            app.MapGet("/studio/{*path:nonfile}", MissingAssets);
            return false;
        }

        var files = new PhysicalFileProvider(Path.GetDirectoryName(index)!);
        app.Lifetime.ApplicationStopped.Register(files.Dispose);
        var types = new FileExtensionContentTypeProvider();
        types.Mappings[".mjs"] = "text/javascript";
        types.Mappings[".woff2"] = "font/woff2";
        types.Mappings[".ttf"] = "font/ttf";
        app.UseStaticFiles(new StaticFileOptions { FileProvider = files, RequestPath = "/studio", ContentTypeProvider = types });
        app.MapGet("/studio", () => Results.File(index, "text/html"));
        // The nonfile constraint keeps absent scripts, workers and fonts as real 404s.
        app.MapFallback("/studio/{*path:nonfile}", () => Results.File(index, "text/html"));
        return true;
    }

    private static IResult MissingAssets() => Results.Content("""
        <!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>Local Agent Studio</title>
        <h1>Local Agent Studio</h1><p>Frontend assets are not installed in this build.</p>
        <p>From the repository root, run <code>./scripts/build-studio.sh</code>, then rebuild or publish the CLI and restart Studio.</p>
        <p>The authenticated HTTP API and MCP remain available with <code>studio --no-open</code>.</p>
        <p><a href="/api/studio/v1/entities">Registered entities</a></p></html>
        """, "text/html");
}
