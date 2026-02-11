using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace Jumpscare.Windows;

public class MainWindow : Window, IDisposable
{
    private string imgPath;
    private string soundPath;

    private GIFConvert? GIF;

    private bool preloadStarted = false;
    private CancellationTokenSource? preloadCts;

    private DateTime lastFrameTime;
    private DateTime? triggerTime = null;
    private TimeSpan delay = TimeSpan.Zero;

    private readonly Random rng = new();

    private bool soundPlayed = false;
    private readonly Configuration config;

    private bool isRunning = false;
    public bool IsRunning => isRunning;

    public MainWindow(string imagePath, string wavPath, Configuration config)
    : base("Jumpscare##Jumpscare_Main",
           ImGuiWindowFlags.NoTitleBar
         | ImGuiWindowFlags.NoScrollbar
         | ImGuiWindowFlags.NoDecoration
         | ImGuiWindowFlags.NoFocusOnAppearing
         | ImGuiWindowFlags.NoNavFocus
         | ImGuiWindowFlags.NoInputs
         | ImGuiWindowFlags.NoMouseInputs
         | ImGuiWindowFlags.NoBackground)
    {
        this.config = config;

        // assign initial paths
        imgPath = imagePath ?? "";
        soundPath = wavPath ?? "";

        lastFrameTime = DateTime.Now;
    }

    public void Dispose()
    {
        // cancel any pending preload
        preloadCts?.Cancel();  
        GIF?.Dispose();
    }

    private void BeginPreload()
    {
        if (preloadStarted) return;
        preloadStarted = true;

        preloadCts = new CancellationTokenSource();
        var token = preloadCts.Token;

        Task.Run(() =>
        {
            try
            {
                if (!File.Exists(imgPath))
                {
                    Plugin.Log.Error($"Image not found: {imgPath}");
                    return;
                }

                GIF = new GIFConvert(imgPath);

                Plugin.Framework.RunOnFrameworkThread(() =>
                {
                    if (token.IsCancellationRequested) return;

                    GIF?.EnsureTexturesLoaded();
                });
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Preload failed: {ex}");
            }
        }, token);
    }

    private void ScheduleNextTrigger()
    {
        int min = Math.Clamp(config.MinTriggerSeconds, 10, 100000);
        int max = Math.Clamp(config.MaxTriggerSeconds, 10, 100000);

        if (max <= min) max = min + 1;

        int seconds = rng.Next(min, max + 1);
        delay = TimeSpan.FromSeconds(seconds);
        triggerTime = DateTime.Now + delay;
    }

    private string ResolveImagePath(string fileName)
    {
        string baseDir = Plugin.PluginInterface.AssemblyLocation.Directory?.FullName
                         ?? Plugin.PluginInterface.GetPluginConfigDirectory();
        return Path.Combine(baseDir, "Data", "visual", fileName);
    }

    private string ResolveSoundPath(string fileName)
    {
        string baseDir = Plugin.PluginInterface.AssemblyLocation.Directory?.FullName
                         ?? Plugin.PluginInterface.GetPluginConfigDirectory();
        return Path.Combine(baseDir, "Data", "audio", fileName);
    }

    public void ResetPlayback()
    {
        GIF?.Dispose();
        GIF = null;

        preloadStarted = false;
        triggerTime = null;
        soundPlayed = false;

        // picks new image
        var enabledImages = config.Images.Where(e => e.Enabled).ToList();
        if (enabledImages.Count > 0)
        {
            // randomly pick if more than one, otherwise pick the only one
            var imageEntry = enabledImages.Count == 1
                ? enabledImages[0]
                : enabledImages[rng.Next(enabledImages.Count)];

            imgPath = ResolveImagePath(imageEntry.Path);
        }

        // pick new sound
        var enabledSounds = config.Sounds.Where(e => e.Enabled).ToList();
        if (enabledSounds.Count > 0)
        {
            // randomly pick if more than one, otherwise pick the only one
            var soundEntry = enabledSounds.Count == 1
                ? enabledSounds[0]
                : enabledSounds[rng.Next(enabledSounds.Count)];

            soundPath = ResolveSoundPath(soundEntry.Path);
        }

        BeginPreload();

        if (isRunning)
            ScheduleNextTrigger();
    }

    public new void Toggle()
    {
        if (!IsOpen)
        {
            IsOpen = true;
            isRunning = true;
            BeginPreload();
            ScheduleNextTrigger();
        }
        else
        {
            triggerTime = null;
            isRunning = false;
            IsOpen = false;
        }
    }
    public override void PreDraw()
    {
        Size = ImGui.GetMainViewport().Size;
        SizeCondition = ImGuiCond.Always;

        Position = Vector2.Zero;
        PositionCondition = ImGuiCond.Always;
    }

    public override void Draw()
    {
        if (!triggerTime.HasValue)
            return;

        if (DateTime.Now < triggerTime.Value)
        {
            if (config.ShowCountdownTimer)
            {
                var remaining = triggerTime.Value - DateTime.Now;
                ImGui.TextUnformatted($"Waiting... {remaining.TotalSeconds:F1}s");
            }

            return;
        }

        if (GIF != null && GIF.FramePaths.Count > 0)
        {
            var now = DateTime.Now;
            float deltaMs = (float)(now - lastFrameTime).TotalMilliseconds;
            lastFrameTime = now;

            PlaySoundOnce();

            GIF.Update(deltaMs);

            float alpha = 1f;

            if (GIF.Finished)
            {
                alpha = 1f - Math.Min(GIF.FadeTimer / GIF.FadeDurationMs, 1f);

                if (alpha <= 0f)
                {
                    ResetPlayback();
                    return;
                }
            }
            GIF.Render(ImGui.GetContentRegionAvail(), alpha);
        }
        else if (GIF == null)
        {
            ImGui.TextUnformatted($"Image not found or unsupported: {imgPath}");
        }
    }

    private void PlaySoundOnce()
    {
        if (!soundPlayed && !string.IsNullOrEmpty(soundPath) && File.Exists(soundPath))
        {
            Sounds.Play(soundPath);
            soundPlayed = true;
        }
    }
}
