using System.Text;

namespace MediaAcquisitionManager.TestInfrastructure.Files;

/// <summary>
/// A real media file without FFmpeg to make it (MAM-9): a PCM WAVE file of silence, whose duration, bit rate
/// and single audio track any ffprobe reads exactly.
/// </summary>
public static class WaveFile
{
    /// <summary>The samples per second the file is written with: 16 000 bytes a second, 16-bit mono.</summary>
    public const int SampleRate = 8000;

    /// <summary>Writes <paramref name="seconds"/> of 16-bit mono silence to <paramref name="path"/>, after a
    /// 44-byte header.</summary>
    public static void WriteSilence(string path, int seconds)
    {
        const short channels = 1;
        const short bitsPerSample = 16;
        const short blockAlign = channels * bitsPerSample / 8;
        var dataBytes = SampleRate * blockAlign * seconds;

        using var writer = new BinaryWriter(File.Create(path), Encoding.ASCII);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);                       // the size of a PCM format chunk
        writer.Write((short)1);                 // PCM
        writer.Write(channels);
        writer.Write(SampleRate);
        writer.Write(SampleRate * blockAlign);  // bytes per second
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        writer.Write(new byte[dataBytes]);
    }
}
