using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using MediaAcquisitionManager.Core.Texts;

namespace MediaAcquisitionManager.Infrastructure.Files;

/// <summary>
/// Runs ffprobe over one file and reads its JSON (MAM-9): an Adapter from the ffprobe executable to
/// <see cref="IMediaProbeProcess"/>, bounded in time and in how much it may say.
/// </summary>
/// <remarks>
/// <para>
/// The file name goes to ffprobe as one argument of <see cref="ProcessStartInfo.ArgumentList"/> and no shell
/// ever sees it: a downloaded file can be named anything, <c>$(…)</c> and <c>;</c> included.
/// </para>
/// <para>
/// ADR 0009 — ffprobe parses files downloaded from the internet, so it gets nothing it could be talked into
/// using: <c>file:</c> before the path makes it a file even when the name contains a colon (which ffprobe
/// would otherwise read as a protocol, <c>http:</c> or <c>concat:</c>), <c>-protocol_whitelist file</c> stops a
/// crafted playlist inside a downloaded file from making the backend open network addresses (specification
/// §4), and <c>-format_whitelist</c> (<see cref="ContainerFormats"/>) stops one from opening other files: a
/// file named <c>.mkv</c> whose content is an <c>ffconcat</c> list or an HLS playlist would otherwise be read
/// as one, and ffprobe would open whatever files it names, outside the allowed roots included.
/// </para>
/// </remarks>
/// <param name="executable">The ffprobe to run: a name looked up on the PATH, or a full path.</param>
/// <param name="timeLimit">How long one run may take before it is killed.</param>
/// <param name="maximumOutputBytes">How much JSON one run may print before it is killed.</param>
internal sealed partial class FfprobeProcess(string executable, TimeSpan timeLimit, int maximumOutputBytes) : IMediaProbeProcess
{
    /// <summary>ffprobe from the PATH: the Docker image installs FFmpeg's package, which puts it there.</summary>
    public const string DefaultExecutable = "ffprobe";

    /// <summary>
    /// 8 MiB. ffprobe's answer for a two-hour remux with eleven streams and 24 chapters is about 32 KB
    /// (<c>VendorSamples/Ffprobe</c>), and streams and format alone are less; a file that makes it print
    /// 250 times that is not a library file, and its answer is not read into memory.
    /// </summary>
    public const int MaximumOutputBytes = 8 * 1024 * 1024;

    /// <summary>
    /// 64 KiB of error text is kept: only one line of it is ever shown. The rest is still read and dropped, so
    /// a chatty ffprobe never blocks on a full pipe.
    /// </summary>
    public const int MaximumErrorBytes = 64 * 1024;

    /// <summary>
    /// The demuxers ffprobe may use, by FFmpeg's names: the containers films and episodes come in — Matroska
    /// and WebM, MP4 and MOV, AVI, MPEG-TS and M2TS, MPEG-PS and VOB, WMV, FLV, Ogg, RealMedia — and WAV, which
    /// the tests write to run the real ffprobe. Each reads the one file it is given; the demuxers that open
    /// files a file names (concat, HLS, DASH, image sequences) are not here, so a file whose content is one of
    /// those is refused, whatever its name says.
    /// </summary>
    public const string ContainerFormats = "matroska,mov,avi,mpegts,mpeg,asf,flv,ogg,rm,wav";

    /// <summary>
    /// 2 minutes. One ffprobe reads a file's headers in well under a second; a disk that has spun down takes
    /// about 15 s to answer the first read, and a NAS mount over a busy network a little more. A run still
    /// going after 2 minutes is stuck on the file (a damaged one ffprobe keeps searching), and bounding it
    /// keeps one bad file from holding one of the inspection's two slots for good.
    /// </summary>
    public static readonly TimeSpan DefaultTimeLimit = TimeSpan.FromMinutes(2);

    private const int MaximumQuotedErrorLength = 300;
    private const int ReadBufferBytes = 16 * 1024;

    private static readonly string[] FixedArguments =
    [
        "-v", "error", "-protocol_whitelist", "file", "-format_whitelist", ContainerFormats,
        "-print_format", "json", "-show_format", "-show_streams",
    ];


    /// <summary>
    /// ffprobe writes UTF-8, and replaces what isn't valid UTF-8 in tags by default; decoding leniently anyway
    /// means a title tag in an old Windows code page can never make a whole file unreadable.
    /// </summary>
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    /// <summary>The production ffprobe: from the PATH, with <see cref="DefaultTimeLimit"/> and
    /// <see cref="MaximumOutputBytes"/>.</summary>
    public FfprobeProcess()
        : this(DefaultExecutable, DefaultTimeLimit, MaximumOutputBytes)
    {
    }

    /// <inheritdoc/>
    public async Task<MediaProbeResult> RunAsync(string probePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var process = new Process { StartInfo = StartInfoFor(probePath) };
        try
        {
            process.Start();
        }
        catch (Win32Exception exception)
        {
            // Not found, not executable: the operating system's own words, without the long preamble.
            return MediaProbeResult.Unavailable(Marshal.GetPInvokeErrorMessage(exception.NativeErrorCode));
        }

        // Its input is closed and empty rather than the app's own, so nothing can ever wait on it.
        process.StandardInput.Close();

        // Both streams are read at once: reading one to its end before the other would leave ffprobe blocked
        // on a full pipe as soon as it wrote 64 KB to the other.
        var output = CaptureAsync(process.StandardOutput.BaseStream, maximumOutputBytes, () => Stop(process));
        var errors = CaptureAsync(process.StandardError.BaseStream, MaximumErrorBytes, onOverflow: null);

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeLimit);
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException) when (limit.IsCancellationRequested)
        {
            // The whole tree: an ffprobe started through a wrapper (a shim, a script) must not live on holding the
            // file and the pipes.
            Stop(process);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(output, errors);
            cancellationToken.ThrowIfCancellationRequested();
            return MediaProbeResult.Failed(TimedOut(timeLimit));
        }

        var standardOutput = await output;
        var standardError = await errors;
        if (standardOutput.Overflowed)
            return MediaProbeResult.Failed(PrintedTooMuch(maximumOutputBytes));

        if (process.ExitCode != 0)
        {
            return MediaProbeResult.Failed(
                Reason(standardError.Bytes, "file:" + probePath)
                ?? Message.Of("files.ffprobe.exit-code-clause", ("code", process.ExitCode.ToString(CultureInfo.InvariantCulture))));
        }

        return MediaProbeResult.Succeeded(Utf8.GetString(standardOutput.Bytes));
    }

    private ProcessStartInfo StartInfoFor(string probePath)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // Its streams are read as bytes and decoded as UTF-8 here (Utf8); the encodings name the same.
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
            CreateNoWindow = true,
        };
        foreach (var argument in FixedArguments)
            startInfo.ArgumentList.Add(argument);
        startInfo.ArgumentList.Add("file:" + probePath);
        return startInfo;
    }

    /// <summary>
    /// Reads <paramref name="stream"/> to its end, keeping at most <paramref name="limit"/> bytes. Past the limit
    /// it calls <paramref name="onOverflow"/> once and keeps reading without keeping, so the writer is never
    /// blocked by a pipe nobody empties.
    /// </summary>
    private static async Task<CapturedStream> CaptureAsync(Stream stream, int limit, Action? onOverflow)
    {
        using var captured = new MemoryStream();
        var buffer = new byte[ReadBufferBytes];
        var overflowed = false;
        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                if (overflowed)
                    continue;

                var room = limit - (int)captured.Length;
                if (read <= room)
                {
                    captured.Write(buffer, 0, read);
                    continue;
                }

                captured.Write(buffer, 0, room);
                overflowed = true;
                onOverflow?.Invoke();
            }
        }
        catch (IOException)
        {
            // The pipe broke under a killed process: what was read so far is all there is.
        }

        return new CapturedStream(captured.ToArray(), overflowed);
    }

    private static void Stop(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception or AggregateException)
        {
            // It exited on its own meanwhile.
        }
    }

    /// <summary>
    /// Why ffprobe failed, as a clause for a sentence, from its error text; <c>null</c> when it said nothing.
    /// </summary>
    /// <remarks>
    /// ffprobe's verdict comes last, as <c>&lt;input&gt;: &lt;reason&gt;</c> ("I/O error", "Invalid data found when
    /// processing input"); the lines before it are what a demuxer said on the way, usually noise such as
    /// "Truncating packet of size 83 to 70" or a line naming a memory address. The input is cut off, as the
    /// sentence names the file itself. A format refused by <see cref="ContainerFormats"/> is named instead of
    /// the bare "Invalid argument" ffprobe then gives. Without a verdict, the first line that says anything.
    /// </remarks>
    private static Message? Reason(byte[] errorText, string input)
    {
        var lines = Utf8.GetString(errorText)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();
        if (lines.Select(line => FormatRefused().Match(line)).FirstOrDefault(match => match.Success) is { } refused)
            return Message.Of("files.ffprobe.not-a-container-clause", ("format", Clean(refused.Groups["format"].Value)));

        var verdictStart = input + ": ";
        var verdict = lines.LastOrDefault(line => line.StartsWith(verdictStart, StringComparison.Ordinal));
        var said = verdict is null ? lines.Select(Clean).FirstOrDefault(line => line.Text.Length > 0) : Clean(verdict[verdictStart.Length..]);
        return said is { Text.Length: > 0 } ? Message.Of("files.ffprobe.said-clause", ("said", said)) : null;
    }

    /// <summary>ffprobe's own words, cleaned and cut to one line's worth.</summary>
    private static VendorText Clean(string line) => VendorText.Clean(line, MaximumQuotedErrorLength);

    /// <summary>What libavformat logs when a file's content is in a format left out of
    /// <see cref="ContainerFormats"/>: <c>[concat @ 0x55…] Format not on whitelist '…'</c>.</summary>
    [GeneratedRegex(@"^\[(?<format>[^\]@]+?) @ [^\]]*\] Format not on whitelist", RegexOptions.CultureInvariant)]
    private static partial Regex FormatRefused();

    /// <summary>
    /// "it didn't finish within 2 minutes": the time limit in minutes from a minute up, else in seconds, to one decimal as
    /// the format <c>0.#</c> wrote it (a midpoint rounded away from zero). MAM-47 — rounded here, before the template
    /// counts it, so the plural follows the number shown: "1 minute" (pinned by <c>FfprobeEnglishFormatPinTests</c>).
    /// </summary>
    internal static Message TimedOut(TimeSpan duration) => duration.TotalSeconds >= 60
        ? Message.Of("files.ffprobe.timed-out-minutes-clause", ("count", Tenths(duration.TotalMinutes)))
        : Message.Of("files.ffprobe.timed-out-seconds-clause", ("count", Tenths(duration.TotalSeconds)));

    /// <summary>
    /// "it printed more than 8 MiB": MAM-47 — the limit is a size the template writes in the reader's units, by the exact
    /// unit rule <c>DescribeBytes</c> had (pinned by <c>FfprobeEnglishFormatPinTests</c>).
    /// </summary>
    internal static Message PrintedTooMuch(int bytes) =>
        Message.Of("files.ffprobe.too-much-output-clause", ("limit", new SizeValue(bytes)));

    private static NumberValue Tenths(double value) => new(Math.Round((decimal)value, 1, MidpointRounding.AwayFromZero));

    /// <summary>What a stream said, and whether it said more than was kept.</summary>
    private sealed record CapturedStream(byte[] Bytes, bool Overflowed);
}
