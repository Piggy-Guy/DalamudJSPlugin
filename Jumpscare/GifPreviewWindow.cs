using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
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
        : base("Jumpscare Preview###Jumpscare_Preview")
    {
        var viewport = ImGui.GetMainViewport();
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(viewport.Size.X * 0.10f, viewport.Size.Y * 0.10f)
        };
        lastFrameTime = DateTime.Now;
        IsOpen = false;
    }

    public void SetPath(string path)
    {
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

        if (!File.Exists(currentPath))
        {
            resourcesLoaded = true;
            return;
        }

        if (new FileInfo(currentPath).Length > 30L * 1024 * 1024)
        {
            resourcesLoaded = true;
            return;
        }

        Task.Run(() =>
        {
            try
            {
                if (!File.Exists(currentPath))
                {
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
                resourcesLoaded = true;
            }
        }, token);
    }

    public override void Draw()
    {
        if (!IsOpen) return;

        var windowSize = ImGui.GetContentRegionAvail();
        float topPadding = windowSize.Y * 0.1f;

        //forces gif down from 0,0 so it doesn't get cut off by the title bar
        using var child = ImRaii.Child("GifHolder", new Vector2(windowSize.X, windowSize.Y - topPadding), false,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoInputs);
        ImGui.SetCursorPosY(topPadding);

        if (!child.Success)
            return;

        if (!resourcesLoaded)
        {
            ImGui.TextUnformatted("Loading preview...");
        }
        else if (gif != null && gif.FramePaths.Count > 0)
        {
            var now = DateTime.Now;
            float deltaMs = (float)(now - lastFrameTime).TotalMilliseconds;
            lastFrameTime = now;

            var remainingSize = new Vector2(windowSize.X, windowSize.Y - topPadding);
            gif.Update(deltaMs);
            gif.Render(remainingSize, 1f);

            if (gif.Finished)
                gif.Reset();
        }
        else
        {
            ImGui.TextUnformatted("GIF has changed paths, is over 30MB, or is invalid.");
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
