using Dalamud.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Jumpscare;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool IsConfigWindowMovable { get; set; } = true;
    public int MinTriggerSeconds { get; set; } = 10;
    public int MaxTriggerSeconds { get; set; } = 10000;
    public bool ShowCountdownTimer { get; set; } = false;
    public List<MediaEntry> Images { get; set; } = new();
    public List<MediaEntry> Sounds { get; set; } = new();

    public class MediaEntry
    {
        public bool Enabled { get; set; } = true;
        public string Path { get; set; } = "";
    }

    public static readonly MediaEntry[] DefaultImages =
    {
        new() { Enabled = true, Path= "foxy-jumpscare.gif" },
        new() { Enabled = false, Path= "lick.gif" },
        new() { Enabled = false, Path= "pikmin.png" },
        new() { Enabled = false, Path= "pipe.png" },
        new() { Enabled = false, Path= "don.gif" },
        new() { Enabled = false, Path= "zenos.png" },
        new() { Enabled = false, Path= "skull.png" },
        new() { Enabled = false, Path= "autism.gif" }
    };

    public readonly static MediaEntry[] DefaultSounds =
    {
        new() { Enabled = true, Path= "foxy.wav" },
        new() { Enabled = false, Path= "apocbird.wav" },
        new() { Enabled = false, Path= "pikmin.wav" },
        new() { Enabled = false, Path= "pipe.wav" },
        new() { Enabled = false, Path= "don.wav" },
        new() { Enabled = false, Path= "zenos.wav" },
        new() { Enabled = false, Path= "bone.wav" }, 
        new() { Enabled = false, Path= "yippee.wav" }
    };

    public void EnsureDefaults()
    {
        if (Images == null || Images.Count == 0)
            Images = new List<MediaEntry>(DefaultImages);
        if (Sounds == null || Sounds.Count == 0)
            Sounds = new List<MediaEntry>(DefaultSounds);
        // clamp min/max between 10–100000
        if (MinTriggerSeconds < 10) MinTriggerSeconds = 10;
        if (MaxTriggerSeconds > 100000) MaxTriggerSeconds = 100000;
        if (MaxTriggerSeconds <= MinTriggerSeconds)
            MaxTriggerSeconds = MinTriggerSeconds + 1;
    }
    public static void EnsureAtLeastOneEnabled(List<MediaEntry> entries)
    {
        if (entries.Count == 0)
            return;

        if (!entries.Any(e => e.Enabled))
            entries[0].Enabled = true;
    }


    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
