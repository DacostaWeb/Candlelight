param(
    [string]$SourceFile,
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (!$SourceFile) { $SourceFile = Join-Path $projectRoot 'assets/icon/candlelight-source.png' }
if (!$OutputDirectory) { $OutputDirectory = $projectRoot }
$sourcePath = [IO.Path]::GetFullPath($SourceFile)
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class CandlelightIconExporter
{
    public static string Export(string sourcePath, string outputPath)
    {
        int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };
        var frames = new List<byte[]>();
        using (var source = new Bitmap(sourcePath))
        {
            int left = source.Width, top = source.Height, right = -1, bottom = -1;
            for (int y = 0; y < source.Height; y++)
                for (int x = 0; x < source.Width; x++)
                    if (source.GetPixel(x, y).A > 16)
                    {
                        left = Math.Min(left, x); top = Math.Min(top, y);
                        right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                    }
            if (right < left) throw new InvalidDataException("The icon source is empty.");
            // Remove empty canvas padding, never change the candle/flame proportions.
            var crop = Rectangle.FromLTRB(Math.Max(0, left - 2), Math.Max(0, top - 2),
                Math.Min(source.Width, right + 3), Math.Min(source.Height, bottom + 3));
            foreach (int size in sizes)
                using (var frame = new Bitmap(size, size, PixelFormat.Format32bppArgb))
                {
                    using (var graphics = Graphics.FromImage(frame))
                    {
                        graphics.Clear(Color.Transparent);
                        graphics.CompositingMode = CompositingMode.SourceCopy;
                        graphics.CompositingQuality = CompositingQuality.HighQuality;
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        int padding = Math.Max(1, (int)Math.Round(size * 0.06));
                        float scale = (float)(size - padding * 2) / Math.Max(crop.Width, crop.Height);
                        float width = crop.Width * scale, height = crop.Height * scale;
                        var target = new RectangleF((size - width) / 2, (size - height) / 2, width, height);
                        using (var attributes = new ImageAttributes())
                        {
                            attributes.SetWrapMode(WrapMode.TileFlipXY);
                            graphics.DrawImage(source, Rectangle.Round(target), crop.X, crop.Y,
                                crop.Width, crop.Height, GraphicsUnit.Pixel, attributes);
                        }
                    }
                    frame.Save(Path.Combine(outputPath, "candlelight-" + size + ".png"), ImageFormat.Png);
                    if (size == 256)
                    {
                        frame.Save(Path.Combine(outputPath, "favicon.png"), ImageFormat.Png);
                        using (var stream = new MemoryStream())
                        {
                            frame.Save(stream, ImageFormat.Png);
                            frames.Add(stream.ToArray());
                        }
                    }
                    else
                    {
                        // Write a 32-bit DIB, preserving alpha instead of the
                        // 16-color fallback produced by Icon.FromHandle().Save().
                        using (var stream = new MemoryStream())
                        using (var writer = new BinaryWriter(stream))
                        {
                            int maskStride = ((size + 31) / 32) * 4;
                            writer.Write(40); writer.Write(size); writer.Write(size * 2);
                            writer.Write((ushort)1); writer.Write((ushort)32);
                            writer.Write(0); writer.Write(size * size * 4 + maskStride * size);
                            writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
                            for (int y = size - 1; y >= 0; y--)
                                for (int x = 0; x < size; x++)
                                {
                                    Color pixel = frame.GetPixel(x, y);
                                    writer.Write(pixel.B); writer.Write(pixel.G);
                                    writer.Write(pixel.R); writer.Write(pixel.A);
                                }
                            for (int y = size - 1; y >= 0; y--)
                            {
                                var mask = new byte[maskStride];
                                for (int x = 0; x < size; x++)
                                    if (frame.GetPixel(x, y).A == 0)
                                        mask[x / 8] |= (byte)(0x80 >> (x % 8));
                                writer.Write(mask);
                            }
                            writer.Flush();
                            frames.Add(stream.ToArray());
                        }
                    }
                }
            using (var writer = new BinaryWriter(File.Create(Path.Combine(outputPath, "favicon.ico"))))
            {
                writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
                int offset = 6 + sizes.Length * 16;
                for (int i = 0; i < sizes.Length; i++)
                {
                    writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                    writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                    writer.Write((byte)0); writer.Write((byte)0);
                    writer.Write((ushort)1); writer.Write((ushort)32);
                    writer.Write(frames[i].Length); writer.Write(offset);
                    offset += frames[i].Length;
                }
                foreach (var frame in frames) writer.Write(frame);
            }
            return "Crop=" + crop + "; 32-bit transparent ICO frames=" + String.Join(",", sizes);
        }
    }
}
'@ -ReferencedAssemblies System.Drawing

[CandlelightIconExporter]::Export($sourcePath, $outputPath)
