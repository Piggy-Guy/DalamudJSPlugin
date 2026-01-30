using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Common.Math;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static Jumpscare.Configuration;

namespace Jumpscare.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;
    private readonly Plugin plugin;

    private string newImagePath = "";
    private string newSoundPath = "";
    private GifPreviewWindow previewWindow;
    private IWavePlayer? previewPlayer;
    private AudioFileReader? previewReader;

    // Store rejection messages so they persist between frames
    private string imageRejectionMessage = "";
    private string soundRejectionMessage = "";

    public ConfigWindow(Plugin plugin) : base("Configuration###WithConstantID")
    {
        SizeCondition = ImGuiCond.Always;

        previewWindow = new GifPreviewWindow();
        plugin.WindowSystem.AddWindow(previewWindow);

        this.plugin = plugin;
        configuration = plugin.Configuration;
    }

    public void Dispose() { StopPreview(); }

    public override void PreDraw()
    {
        if (configuration.IsConfigWindowMovable)
            Flags &= ~ImGuiWindowFlags.NoMove;
        else
            Flags |= ImGuiWindowFlags.NoMove;
    }

    public override void Draw()
    {
        // --- Status indicator ---
        if (plugin.MainWindow.IsRunning)
            ImGui.TextColored(new Vector4(0f, 1f, 0f, 1f), "Jumpscare timer is ACTIVE");
        else
            ImGui.TextColored(new Vector4(1f, 0f, 0f, 1f), "Jumpscare timer is INACTIVE");

        // --- Jumpscare toggle button ---
        if (plugin.MainWindow.IsRunning)
        {
            if (ImGui.Button("Stop Timer"))
            {
                plugin.MainWindow.Toggle(); // stops jumpscare
            }
        }
        else
        {
            if (ImGui.Button("Start Timer"))
            {
                plugin.MainWindow.Toggle(); // starts jumpscare
            }
        }
        bool showTimer = configuration.ShowCountdownTimer;
        if (ImGui.Checkbox("Show Countdown Timer", ref showTimer))
        {
            configuration.ShowCountdownTimer = showTimer;
            configuration.Save();
        }

        ImGui.Separator();
        ImGui.Text("Changing Settings (aside from Trigger Timing) resets the timer");

        ImGui.Text("Trigger Timing");

        // --- Min seconds ---
        int minSecs = configuration.MinTriggerSeconds;
        if (ImGui.InputInt("Min Seconds (10)", ref minSecs))
        {
            // Clamp minSecs between 10 and (maxSecs - 1)
            minSecs = Math.Clamp(minSecs, 10, configuration.MaxTriggerSeconds - 1);
            configuration.MinTriggerSeconds = minSecs;
            configuration.Save();
        }

        // --- Max seconds ---
        int maxSecs = configuration.MaxTriggerSeconds;
        if (ImGui.InputInt("Max Seconds (100000)", ref maxSecs))
        {
            // Clamp maxSecs between (minSecs + 1) and 100000
            maxSecs = Math.Clamp(maxSecs, configuration.MinTriggerSeconds + 1, 100000);
            configuration.MaxTriggerSeconds = maxSecs;
            configuration.Save();
        }
        ImGui.Separator();

        // --- Images ---
        if (ImGui.TreeNode("Images"))
        {
            if (ImGui.Button("Reset Images to Defaults"))
            {
                configuration.Images = new List<MediaEntry>(Configuration.DefaultImages.Select(e => new MediaEntry
                {
                    Enabled = e.Enabled,
                    Path = e.Path
                }));
                configuration.Save();

            }


            if (ImGui.BeginTable("ImagesTable", 2))
            {
                ImGui.TableSetupColumn("Enabled", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableSetupColumn("Path");
                ImGui.TableHeadersRow();

                for (int i = 0; i < configuration.Images.Count; i++)
                {
                    var entry = configuration.Images[i];

                    ImGui.TableNextRow();

                    ImGui.TableNextColumn();
                    bool enabled = entry.Enabled;
                    if (ImGui.Checkbox($"##img_enabled_{i}", ref enabled))
                    {
                        if (!enabled && entry.Enabled && !CanDisable(configuration.Images))
                        {
                            // Block disabling the last enabled image
                            enabled = true;
                        }
                        else
                        {
                            entry.Enabled = enabled;
                            configuration.Save();
                            ReloadMedia();
                        }
                    }


                    ImGui.TableNextColumn();
                    if (ImGui.Selectable(entry.Path))
                    {
                        previewWindow.SetPath(entry.Path); // only update the GIF
                    }

                }
                ImGui.EndTable();
            }

            // ✅ ADD MEDIA CONTROLS GO HERE
            DrawAddMedia("GIF/PNG/JPG", configuration.Images, ref newImagePath);

            if (!string.IsNullOrEmpty(imageRejectionMessage))
            {
                ImGui.TextColored(new Vector4(1f, 0f, 0f, 1f), imageRejectionMessage);
            }

            ImGui.TreePop();
        }

        ImGui.Separator();

        // --- Sounds ---
        if (ImGui.TreeNode("Sounds"))
        {
            if (ImGui.Button("Reset Sounds to Defaults"))
            {
                configuration.Sounds = new List<MediaEntry>(Configuration.DefaultSounds.Select(e => new MediaEntry
                {
                    Enabled = e.Enabled,
                    Path = e.Path
                }));
                configuration.Save();

            }

            if (ImGui.BeginTable("SoundsTable", 2))
            {
                ImGui.TableSetupColumn("Enabled", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableSetupColumn("Path");
                ImGui.TableHeadersRow();

                for (int i = 0; i < configuration.Sounds.Count; i++)
                {
                    var entry = configuration.Sounds[i];

                    ImGui.TableNextRow();

                    // --- Enabled checkbox ---
                    ImGui.TableNextColumn();
                    bool enabled = entry.Enabled;
                    if (ImGui.Checkbox($"##snd_enabled_{i}", ref enabled))
                    {
                        if (!enabled && entry.Enabled && !CanDisable(configuration.Sounds))
                        {
                            // Prevent disabling last enabled sound
                            enabled = true;
                        }
                        else
                        {
                            entry.Enabled = enabled;
                            configuration.Save();
                            ReloadMedia();
                        }
                    }

                    // --- File path ---
                    ImGui.TableNextColumn();
                    if (ImGui.Selectable(entry.Path))
                    {
                        PlayAudioPreview(ResolveSoundPath(entry.Path));
                    }
                }

                ImGui.EndTable();
            }

            // --- Add new audio file ---
            DrawAddMedia("WAV/MP3", configuration.Sounds, ref newSoundPath);

            if (!string.IsNullOrEmpty(soundRejectionMessage))
            {
                ImGui.TextColored(new Vector4(1f, 0f, 0f, 1f), soundRejectionMessage);
            }

            ImGui.TreePop();
        }
        ImGui.Separator();

    }
    private static string? PickRandomEnabled(List<MediaEntry> entries)
    {
        var enabled = entries.Where(e => e.Enabled).ToList();
        if (enabled.Count == 0)
            return null;

        return enabled[Random.Shared.Next(enabled.Count)].Path;
    }


    private (string imagePath, string soundPath) ResolveCurrentMediaPaths()
    {
        string? imgFile = PickRandomEnabled(configuration.Images);
        string? sndFile = PickRandomEnabled(configuration.Sounds);

        if (imgFile == null || sndFile == null)
            return (null, null);

        string imgPath = ResolveImagePath(imgFile);
        string sndPath = ResolveSoundPath(sndFile);

        return (imgPath, sndPath);
    }

    private void DrawAddMedia(
    string label,
    List<MediaEntry> targetList,
    ref string pathBuffer)
    {
        ImGui.InputText($"New {label} Path", ref pathBuffer, 256);

        if (ImGui.Button($"Add {label}") && !string.IsNullOrWhiteSpace(pathBuffer))
        {
            string path = pathBuffer; // copy to local to avoid ref capture

            if (!File.Exists(path))
            {
                SetRejection(label, $"File not found: {path}");
                return;
            }

            // --- Size check ---
            long maxSize = 30L * 1024 * 1024; // 30 MB
            var fileInfo = new FileInfo(path);
            if (fileInfo.Length > maxSize)
            {
                SetRejection(label, $"File too large (>30MB)");
                return;
            }

            var ext = Path.GetExtension(path).ToLowerInvariant();
            bool valid =
                (label == "GIF/PNG/JPG" && (ext == ".gif" || ext == ".png" || ext == ".jpg" || ext == ".jpeg")) ||
                (label == "WAV/MP3" && (ext == ".wav" || ext == ".mp3"));

            if (!valid)
            {
                SetRejection(label, $"Invalid file type: {ext}");
                return;
            }

            string fileNameOrPath = path; // keep absolute if external
            if (!targetList.Exists(e => e.Path.Equals(fileNameOrPath, StringComparison.OrdinalIgnoreCase)))
            {
                targetList.Add(new MediaEntry
                {
                    Path = fileNameOrPath,
                    Enabled = true
                });

                configuration.Save();
                ReloadMedia();
            }


            pathBuffer = "";
            ClearRejection(label);
        }
    }

    private void PlayAudioPreview(string path)
    {
        try
        {
            StopPreview(); // prevent overlap

            previewReader = new AudioFileReader(path);
            previewPlayer = new WaveOutEvent();
            previewPlayer.Init(previewReader);
            previewPlayer.Play();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Failed to play audio preview: {ex}");
            StopPreview();
        }
    }

    private void StopPreview()
    {
        previewPlayer?.Stop();
        previewPlayer?.Dispose();
        previewPlayer = null;

        previewReader?.Dispose();
        previewReader = null;
    }
    private void SetRejection(string label, string message)
    {
        if (label == "GIF/PNG/JPG")
            imageRejectionMessage = message;
        else if (label == "WAV/MP3")
            soundRejectionMessage = message;
    }
    private void ClearRejection(string label)
    {
        if (label == "GIF/PNG/JPG")
            imageRejectionMessage = "";
        else if (label == "WAV/MP3")
            soundRejectionMessage = "";
    }
    private string ResolveImagePath(string fileNameOrPath)
    {
        // If absolute path and exists, use as-is
        if (Path.IsPathRooted(fileNameOrPath) && File.Exists(fileNameOrPath))
            return fileNameOrPath;

        // Otherwise, resolve relative to Data/visual
        string baseDir = Plugin.PluginInterface.AssemblyLocation.Directory?.FullName
                         ?? Plugin.PluginInterface.GetPluginConfigDirectory();
        return Path.Combine(baseDir, "Data", "visual", fileNameOrPath);
    }

    private string ResolveSoundPath(string fileNameOrPath)
    {
        if (Path.IsPathRooted(fileNameOrPath) && File.Exists(fileNameOrPath))
            return fileNameOrPath;

        string baseDir = Plugin.PluginInterface.AssemblyLocation.Directory?.FullName
                         ?? Plugin.PluginInterface.GetPluginConfigDirectory();
        return Path.Combine(baseDir, "Data", "audio", fileNameOrPath);
    }

    private void ReloadMedia()
    {
        var paths = ResolveCurrentMediaPaths();
        plugin.MainWindow.ResetPlayback();
    }



    public (string imagePath, string soundPath) ResolveInitialMedia()
    {
        string baseDir = Plugin.PluginInterface.AssemblyLocation.Directory?.FullName
                         ?? Plugin.PluginInterface.GetPluginConfigDirectory();

        // Ensure at least one enabled image and sound exist
        Configuration.EnsureAtLeastOneEnabled(configuration.Images);
        Configuration.EnsureAtLeastOneEnabled(configuration.Sounds);

        var imgFile = configuration.Images.First(e => e.Enabled).Path;
        var sndFile = configuration.Sounds.First(e => e.Enabled).Path;

        string imgPath = Path.Combine(baseDir, "Data", "visual", imgFile);
        string sndPath = Path.Combine(baseDir, "Data", "audio", sndFile);

        Plugin.Log.Information($"[ResolveInitialMedia] Image: {imgPath}");
        Plugin.Log.Information($"[ResolveInitialMedia] Sound: {sndPath}");

        return (imgPath, sndPath);
    }

    private static bool CanDisable(List<MediaEntry> entries)
    {
        return entries.Count(e => e.Enabled) > 1;
    }

}
