using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Jumpscare;
using System;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

public class GifPreviewWindow : Window, IDisposable
{
    private string? currentPath;
    private GIFConvert? gif;
    private bool resourcesLoaded = false;
    private CancellationTokenSource? preloadCts;
    private DateTime lastFrameTime;

    public GifPreviewWindow()
        : base("GIF Preview###GifPreview")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(300, 200)
        };
        lastFrameTime = DateTime.Now;
        IsOpen = false;
    }

    public void SetPath(string path)
    {
        if (currentPath == path)
            return;

        currentPath = ResolveImagePath(path);
        StartPreload();
        IsOpen = true;
    }

    private void StartPreload()
    {
        preloadCts?.Cancel();
        preloadCts?.Dispose();

        preloadCts = new CancellationTokenSource();
        var token = preloadCts.Token;

        resourcesLoaded = false;

        gif?.Dispose();
        gif = null;

        Task.Run(() =>
        {
            try
            {
                if (!File.Exists(currentPath))
                {
                    Plugin.Log.Error($"[GifPreview] File not found: {currentPath}");
                    resourcesLoaded = true;
                    return;
                }

                var newGif = new GIFConvert(currentPath);

                // load textures on framework thread
                Plugin.Framework.RunOnFrameworkThread(() =>
                {
                    try
                    {
                        if (token.IsCancellationRequested) return;

                        newGif.EnsureTexturesLoaded();
                        gif = newGif;

                        resourcesLoaded = true;
                    }
                    catch
                    {
                        // prevents errors silently while disposing
                    }
                });
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"[GifPreview] Failed to preload GIF: {ex}");
                resourcesLoaded = true;
            }
        }, token);
    }

    public override void Draw()
    {
        if (!IsOpen) return;

        if (!resourcesLoaded)
        {
            ImGui.TextUnformatted("Loading preview...");
        }

        else if (gif != null && gif.FramePaths.Count > 0)
        {
            var now = DateTime.Now;
            float deltaMs = (float)(now - lastFrameTime).TotalMilliseconds;
            lastFrameTime = now;

            gif.Update(deltaMs);
            gif.Render(ImGui.GetContentRegionAvail(), 1f);

            // loop gif
            if (gif.Finished)
                gif.Reset();
        }
        else
        {
            ImGui.TextUnformatted("GIF failed to load or is unsupported.");
        }
    }

    public void Dispose()
    {
        preloadCts?.Cancel();
        preloadCts?.Dispose();
        gif?.Dispose();
    }

    private static string ResolveImagePath(string fileNameOrPath)
    {
        if (Path.IsPathRooted(fileNameOrPath) && File.Exists(fileNameOrPath))
            return fileNameOrPath;

        string baseDir = Plugin.PluginInterface.AssemblyLocation.Directory?.FullName
                         ?? Plugin.PluginInterface.GetPluginConfigDirectory();
        return Path.Combine(baseDir, "Data", "visual", fileNameOrPath);
    }
}
