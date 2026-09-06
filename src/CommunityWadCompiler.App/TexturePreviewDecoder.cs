using System;
using System.Collections.Generic;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace CommunityWadCompiler.App;

/// <summary>
/// Simple Doom texture decoder for preview purposes.
/// Supports flats (64x64 raw) and simple patches (Doom picture format).
/// </summary>
public static class TexturePreviewDecoder
{
    private static readonly byte[] DoomPalette = GenerateDoomPalette();

    /// <summary>
    /// Attempts to decode texture data as a flat (64x64) or patch (Doom picture format).
    /// Returns an Avalonia WriteableBitmap for display, or null if format not supported.
    /// </summary>
    public static WriteableBitmap? Decode(byte[] data, string name)
    {
        if (data == null || data.Length == 0)
            return null;

        // Try as flat (64x64 = 4096 bytes exactly)
        if (data.Length == 4096)
            return DecodeFlat(data);

        // Try as Doom picture format (patch)
        if (data.Length >= 8)
            return DecodePatch(data);

        return null;
    }

    private static WriteableBitmap? DecodeFlat(byte[] data)
    {
        try
        {
            var bitmap = new WriteableBitmap(new Avalonia.PixelSize(64, 64), new Avalonia.Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
            using (var frameBuffer = bitmap.Lock())
            {
                unsafe
                {
                    byte* ptr = (byte*)frameBuffer.Address.ToPointer();
                    int stride = frameBuffer.RowBytes;
                    for (int y = 0; y < 64; y++)
                    {
                        for (int x = 0; x < 64; x++)
                        {
                            byte colorIndex = data[y * 64 + x];
                            var color = DoomPalette[colorIndex * 3];
                            int i = y * stride + x * 4;
                            ptr[i] = DoomPalette[colorIndex * 3 + 2]; // B
                            ptr[i + 1] = DoomPalette[colorIndex * 3 + 1]; // G
                            ptr[i + 2] = DoomPalette[colorIndex * 3];     // R
                            ptr[i + 3] = 255; // A
                        }
                    }
                }
            }
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static WriteableBitmap? DecodePatch(byte[] data)
    {
        try
        {
            // Doom picture format header (8 bytes):
            // short width, short height, short leftoffset, short topoffset
            if (data.Length < 8)
                return null;

            short width = BitConverter.ToInt16(data, 0);
            short height = BitConverter.ToInt16(data, 2);
            short leftOffset = BitConverter.ToInt16(data, 4);
            short topOffset = BitConverter.ToInt16(data, 6);

            if (width <= 0 || height <= 0 || width > 1024 || height > 1024)
                return null;

            // Column offsets (4 bytes each, little-endian)
            int columnOffsetsStart = 8;
            if (data.Length < columnOffsetsStart + width * 4)
                return null;

            var columnOffsets = new int[width];
            for (int x = 0; x < width; x++)
            {
                columnOffsets[x] = BitConverter.ToInt32(data, columnOffsetsStart + x * 4);
            }

            var bitmap = new WriteableBitmap(new Avalonia.PixelSize(width, height), new Avalonia.Vector(96, 96), Avalonia.Platform.PixelFormat.Bgra8888, Avalonia.Platform.AlphaFormat.Premul);
            using (var frameBuffer = bitmap.Lock())
            {
                unsafe
                {
                    byte* ptr = (byte*)frameBuffer.Address.ToPointer();
                    int stride = frameBuffer.RowBytes;

                    // Initialize to transparent
                    for (int i = 0; i < stride * height; i++)
                        ptr[i] = 0;

                    for (int x = 0; x < width; x++)
                    {
                        int colOffset = columnOffsets[x];
                        if (colOffset >= data.Length)
                            continue;

                        int y = 0;
                        while (y < height)
                        {
                            if (colOffset >= data.Length)
                                break;
                            byte topDelta = data[colOffset++];
                            if (topDelta == 255)
                                break; // End of column

                            if (colOffset >= data.Length)
                                break;
                            byte length = data[colOffset++];
                            if (length == 0 || colOffset >= data.Length)
                                continue;

                            if (colOffset >= data.Length)
                                break;
                            colOffset++; // Skip padding byte

                            int destY = y + topDelta;
                            for (int i = 0; i < length && destY < height && colOffset < data.Length; i++, destY++, colOffset++)
                            {
                                byte colorIndex = data[colOffset];
                                int rowOffset = destY * (int)(stride / 4) + x;
                                if (rowOffset * 4 + 3 < stride * height)
                                {
                                    byte* pixel = ptr + destY * stride + x * 4;
                                    *pixel++ = DoomPalette[colorIndex * 3 + 2]; // B
                                    *pixel++ = DoomPalette[colorIndex * 3 + 1]; // G
                                    *pixel++ = DoomPalette[colorIndex * 3];     // R
                                    *pixel = 255; // A
                                }
                            }

                            if (colOffset >= data.Length)
                                break;
                            colOffset++; // Skip padding byte

                            y = destY + 1;
                        }
                    }
                }
            }
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static byte[] GenerateDoomPalette()
    {
        // Standard Doom palette (256 colors * 3 = 768 bytes)
        // This is a simplified version - in reality you'd load PLAYPAL lump
        var palette = new byte[768];
        for (int i = 0; i < 256; i++)
        {
            // Simple grayscale fallback - real app would load PLAYPAL
            byte v = (byte)(i < 128 ? i * 2 : 255 - (i - 128) * 2);
            palette[i * 3] = v;     // R
            palette[i * 3 + 1] = v; // G
            palette[i * 3 + 2] = v; // B
        }
        // Add some color variation for better visibility
        for (int i = 0; i < 16; i++)
        {
            palette[i * 3] = (byte)(i * 16);
            palette[i * 3 + 1] = (byte)(i * 16);
            palette[i * 3 + 2] = (byte)(255 - i * 16);
        }
        return palette;
    }
}