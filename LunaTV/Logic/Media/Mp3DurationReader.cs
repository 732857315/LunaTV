using System;
using System.IO;

namespace LunaTV.Logic.Media;

/// <summary>
///     Computes the exact duration of an MP3 file by walking every audio frame and
///     summing per-frame durations. Used as a workaround for libmpv reporting a
///     bitrate-based duration estimate for VBR MP3 files that lack a Xing/Info
///     header, which makes playback stop a few seconds before the real end of the
///     file (see issue #10953).
/// </summary>
public static class Mp3DurationReader
{
    // Bitrate tables in kbps. The first index is the encoding column derived
    // from MPEG version + layer; the second index is the 4-bit bitrate field
    // from the frame header. 0 = "free", 15 = "bad"; both are treated as invalid.
    private static readonly int[,] BitrateTable = new int[5, 16]
    {
        // V1, L1
        { 0, 32, 64, 96, 128, 160, 192, 224, 256, 288, 320, 352, 384, 416, 448, -1 },
        // V1, L2
        { 0, 32, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 384, -1 },
        // V1, L3
        { 0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, -1 },
        // V2/2.5, L1
        { 0, 32, 48, 56, 64, 80, 96, 112, 128, 144, 160, 176, 192, 224, 256, -1 },
        // V2/2.5, L2 or L3
        { 0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, -1 }
    };

    // Sample rate by MPEG version (V1, V2, V2.5) and the 2-bit sample-rate
    // field; the fourth slot is reserved.
    private static readonly int[,] SampleRateTable = new int[3, 4]
    {
        { 44100, 48000, 32000, -1 }, // MPEG 1
        { 22050, 24000, 16000, -1 }, // MPEG 2
        { 11025, 12000, 8000, -1 }   // MPEG 2.5
    };

    /// <summary>
    ///     Returns the exact duration of <paramref name="path" /> in seconds, or
    ///     null if the file is not a recognisable MP3 (or no frames could be
    ///     decoded). The file is opened read-only and never modified.
    /// </summary>
    public static double? TryGetDurationSeconds(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return ReadDurationSeconds(stream);
        }
        catch
        {
            return null;
        }
    }

    private static double? ReadDurationSeconds(Stream stream)
    {
        long length = stream.Length;
        long pos = SkipId3v2(stream);

        // ID3v1 lives in the last 128 bytes if present; never scan into it.
        long end = length;
        if (length >= 128)
        {
            stream.Position = length - 128;
            Span<byte> tagProbe = stackalloc byte[3];
            if (stream.Read(tagProbe) == 3 && tagProbe[0] == (byte)'T' && tagProbe[1] == (byte)'A' && tagProbe[2] == (byte)'G')
            {
                end = length - 128;
            }
        }

        stream.Position = pos;
        double totalSeconds = 0;
        int frameCount = 0;

        Span<byte> header = stackalloc byte[4];
        while (pos + 4 <= end)
        {
            stream.Position = pos;
            if (stream.Read(header) != 4)
            {
                break;
            }

            // Sync word is 11 set bits: 0xFF in byte 0, top 3 bits of byte 1 set.
            if (header[0] != 0xFF || (header[1] & 0xE0) != 0xE0)
            {
                pos++;
                continue;
            }

            if (!TryParseFrame(header, out int frameSize, out double frameDurationSeconds))
            {
                pos++;
                continue;
            }

            if (frameSize <= 4 || pos + frameSize > end)
            {
                break;
            }

            totalSeconds += frameDurationSeconds;
            frameCount++;
            pos += frameSize;
        }

        return frameCount > 0 ? totalSeconds : null;
    }

    private static long SkipId3v2(Stream stream)
    {
        if (stream.Length < 10)
        {
            return 0;
        }

        stream.Position = 0;
        Span<byte> header = stackalloc byte[10];
        if (stream.Read(header) != 10)
        {
            return 0;
        }

        if (header[0] != (byte)'I' || header[1] != (byte)'D' || header[2] != (byte)'3')
        {
            return 0;
        }

        // Bytes 6..9 are a synchsafe 28-bit integer giving the tag size after the header.
        int size = (header[6] & 0x7F) << 21 | (header[7] & 0x7F) << 14 | (header[8] & 0x7F) << 7 | header[9] & 0x7F;

        // Bit 4 of the flags byte (header[5]) indicates a footer (extra 10 bytes).
        bool hasFooter = (header[5] & 0x10) != 0;
        return 10L + size + (hasFooter ? 10L : 0L);
    }

    private static bool TryParseFrame(ReadOnlySpan<byte> header, out int frameSize, out double frameDurationSeconds)
    {
        frameSize = 0;
        frameDurationSeconds = 0;

        int versionBits = header[1] >> 3 & 0x03; // 00=V2.5, 01=reserved, 10=V2, 11=V1
        int layerBits = header[1] >> 1 & 0x03;   // 00=reserved, 01=L3, 10=L2, 11=L1
        if (versionBits == 1 || layerBits == 0)
        {
            return false;
        }

        int bitrateIndex = header[2] >> 4 & 0x0F;
        int sampleRateIndex = header[2] >> 2 & 0x03;
        int padding = header[2] >> 1 & 0x01;
        if (bitrateIndex == 0 || bitrateIndex == 15 || sampleRateIndex == 3)
        {
            return false;
        }

        bool isV1 = versionBits == 3;
        int layer = 4 - layerBits; // 1, 2, or 3

        int bitrateRow;
        if (isV1)
        {
            bitrateRow = layer - 1; // L1=0, L2=1, L3=2
        }
        else
        {
            bitrateRow = layer == 1 ? 3 : 4; // V2/2.5 L1 has its own row; L2 and L3 share row 4
        }

        int bitrateKbps = BitrateTable[bitrateRow, bitrateIndex];
        if (bitrateKbps <= 0)
        {
            return false;
        }

        int versionRow;
        if (isV1)
        {
            versionRow = 0;
        }
        else if (versionBits == 2)
        {
            versionRow = 1;
        }
        else
        {
            versionRow = 2; // MPEG 2.5
        }

        int sampleRate = SampleRateTable[versionRow, sampleRateIndex];
        if (sampleRate <= 0)
        {
            return false;
        }

        int samplesPerFrame;
        if (layer == 1)
        {
            samplesPerFrame = 384;
        }
        else if (layer == 2)
        {
            samplesPerFrame = 1152;
        }
        else
        {
            // Layer III: 1152 for MPEG 1, 576 for MPEG 2 / 2.5.
            samplesPerFrame = isV1 ? 1152 : 576;
        }

        int bitrateBps = bitrateKbps * 1000;
        if (layer == 1)
        {
            frameSize = (12 * bitrateBps / sampleRate + padding) * 4;
        }
        else
        {
            frameSize = samplesPerFrame / 8 * bitrateBps / sampleRate + padding;
        }

        if (frameSize <= 4)
        {
            return false;
        }

        frameDurationSeconds = (double)samplesPerFrame / sampleRate;
        return true;
    }
}