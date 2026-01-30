using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace Jumpscare
{
    public class GIFConvert : IDisposable
    {
        private class Frame
        {
            public ISharedImmediateTexture Texture { get; }
            public int DelayMs { get; }

            public Frame(ISharedImmediateTexture texture, int delayMs)
            {
                Texture = texture;
                DelayMs = delayMs;
            }
        }

        private readonly List<Frame> frames = new();
        private readonly List<(string Path, int DelayMs)> framePaths = new();
        public IReadOnlyList<(string Path, int DelayMs)> FramePaths => framePaths;

        private int currentFrame = 0;
        private float timeAccumulator = 0f;
        private readonly string tempFolder;
        private bool texturesLoaded = false;
        private int previousFrame = -1;

        public bool Finished { get; private set; } = false;
        public float FadeTimer { get; private set; } = 0f;
        public float FadeDurationMs { get; set; } = 1000f;
        public bool ShouldCloseWindow => Finished && FadeTimer >= FadeDurationMs;
        private bool firstUpdate = true;

        public int Width { get; private set; }
        public int Height { get; private set; }

        public GIFConvert(string gifPath)
        {
            // create a unique folder for this instance
            tempFolder = Path.Combine(Path.GetTempPath(), "DalamudGifFrames", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempFolder);

            DecodeGifToPngs(gifPath);
        }

        private void DecodeGifToPngs(string gifPath)
        {
            using var img = Image.Load<Rgba32>(gifPath);

            for (int i = 0; i < img.Frames.Count; i++)
            {
                var frame = img.Frames[i];

                int delayMs = 20; // fallback
                try
                {
                    delayMs = frame.Metadata.GetGifMetadata().FrameDelay * 10;
                    if (delayMs <= 20) delayMs = 20;
                }
                catch (Exception ex)
                {
                    Plugin.Log.Warning($"GIFConvert: Failed to read frame delay for frame {i}, using fallback. Exception: {ex}");
                }

                // copy pixels into buffer
                var pixelBuffer = new Rgba32[frame.Width * frame.Height];
                frame.CopyPixelDataTo(pixelBuffer);

                // make new single-frame image
                using var singleFrameImage = Image.LoadPixelData<Rgba32>(
                    pixelBuffer,
                    frame.Width,
                    frame.Height
                );

                string framePath = Path.Combine(tempFolder, $"frame_{i}.png");
                singleFrameImage.Save(framePath); // Save as PNG

                framePaths.Add((framePath, delayMs));
            }
        }

        public void EnsureTexturesLoaded()
        {
            if (texturesLoaded || framePaths.Count == 0) return;

            foreach (var (path, delayMs) in framePaths)
            {
                var tex = Plugin.TextureProvider.GetFromFile(path);
                if (tex != null)
                {
                    frames.Add(new Frame(tex, delayMs));
                    var wrap = tex.GetWrapOrEmpty();
                    Width = wrap.Width;
                    Height = wrap.Height;
                }
            }

            texturesLoaded = frames.Count > 0;
        }

        public void Update(float deltaMs)
        {
            if (frames.Count == 0) return;
            if (firstUpdate) { firstUpdate = false; return; }

            if (!Finished)
            {
                timeAccumulator += deltaMs;

                while (timeAccumulator >= frames[currentFrame].DelayMs)
                {
                    timeAccumulator -= frames[currentFrame].DelayMs;
                    previousFrame = currentFrame;
                    currentFrame++;
                    if (currentFrame >= frames.Count)
                    {
                        currentFrame = frames.Count - 1;
                        Finished = true;
                        FadeTimer = 0f;
                        break;
                    }
                }
            }
            else
            {
                FadeTimer = Math.Min(FadeTimer + deltaMs, FadeDurationMs);
            }
        }

        public void Render(Vector2 size, float alpha = 1f)
        {
            if (frames.Count == 0) return;

            if (previousFrame >= 0 && previousFrame != currentFrame && currentFrame < frames.Count - 1)
            {
                var prevTex = frames[previousFrame].Texture;
                var prevWrap = prevTex.GetWrapOrEmpty();
                ImGui.SetCursorPos(Vector2.Zero);
                ImGui.Image(prevWrap.Handle, size, Vector2.Zero, Vector2.One, new Vector4(1f, 1f, 1f, alpha));
            }

            var tex = frames[currentFrame].Texture;
            var wrap = tex.GetWrapOrEmpty();
            ImGui.SetCursorPos(Vector2.Zero);
            ImGui.Image(wrap.Handle, size, Vector2.Zero, Vector2.One, new Vector4(1f, 1f, 1f, alpha));
        }
        public void Reset()
        {
            currentFrame = 0;
            timeAccumulator = 0f;
            Finished = false;
            FadeTimer = 0;
        }

        public void Dispose()
        {
            foreach (var (path, _) in framePaths)
            {
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }

            try { if (Directory.Exists(tempFolder)) Directory.Delete(tempFolder, true); } catch { }
        }
    }
}
