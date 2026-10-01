namespace MediaAcquisitionManager.TestInfrastructure.Files;

/// <summary>
/// Stand-ins for the ffprobe executable (MAM-9): a <c>sh</c> script on Linux and a <c>.cmd</c> script on
/// Windows, run through the real process code, so the tests cover starting, reading both streams, the exit
/// code, the time limit and the kill without FFmpeg installed (CI has none).
/// </summary>
/// <remarks>
/// Each stub gets its paths quoted for its shell; the tests' paths come from the temp folder and contain
/// neither quotes, spaces nor <c>%</c>. ffprobe's last argument is <c>file:</c> followed by the path. A
/// <c>.cmd</c> script splits its arguments at commas too, which <c>-format_whitelist</c>'s value has, so the
/// Windows stubs take the last argument by shifting and record the command line whole
/// (<see cref="RecordedArguments"/>).
/// </remarks>
public static class FfprobeStubs
{
    /// <summary>What a demuxer typically says on the way before ffprobe's verdict: noise, not the reason.</summary>
    public const string DemuxerNoise = "Truncating packet of size 83 to 70";

    /// <summary>Takes a <c>.cmd</c> script's last argument into <c>%last%</c>.</summary>
    private const string ShiftToLastArgument = "set \"last=\"\r\n:next\r\nif not \"%~2\"==\"\" (shift & goto next)\r\nset \"last=%~1\"";

    /// <summary>Prints the file <paramref name="outputPath"/> and exits with 0, like ffprobe that read a file.</summary>
    public static string Printing(string folder, string outputPath) => Create(
        folder, "ffprobe-printing",
        $"cat '{outputPath}'",
        $"type \"{outputPath}\"");

    /// <summary>
    /// Fails the way ffprobe does: writes an empty line and <paramref name="noise"/> — what a demuxer says on the
    /// way — and then its verdict, <c>&lt;its input&gt;: <paramref name="verdict"/></c>, to its error stream, and
    /// exits with <paramref name="exitCode"/>. Each part is left out when <c>null</c>.
    /// </summary>
    public static string Failing(string folder, int exitCode, string? verdict, string? noise = DemuxerNoise)
    {
        var unix = "for last in \"$@\"; do :; done\n";
        var windows = ShiftToLastArgument + "\r\n";
        if (noise is not null)
        {
            unix += $"printf '\\n%s\\n' '{noise}' >&2\n";
            windows += $"echo. 1>&2\r\necho {noise} 1>&2\r\n";
        }

        if (verdict is not null)
        {
            unix += $"printf '%s: %s\\n' \"$last\" '{verdict}' >&2\n";
            windows += $"echo %last%: {verdict} 1>&2\r\n";
        }

        return Create(folder, "ffprobe-failing", unix + $"exit {exitCode}", windows + $"exit /b {exitCode}");
    }

    /// <summary>Never finishes on its own: it waits ten minutes in a child process, so only killing the whole
    /// tree ends it.</summary>
    public static string Hanging(string folder) => Create(
        folder, "ffprobe-hanging",
        "sleep 600",
        "ping -n 600 127.0.0.1 >nul");

    /// <summary>Writes its arguments to <paramref name="recordPath"/> (read them with
    /// <see cref="RecordedArguments"/>), then prints <paramref name="outputPath"/>.</summary>
    public static string Recording(string folder, string recordPath, string outputPath) => Create(
        folder, "ffprobe-recording",
        $"for argument in \"$@\"; do printf '%s\\n' \"$argument\"; done > '{recordPath}'\ncat '{outputPath}'",
        $">\"{recordPath}\" echo(%*\r\ntype \"{outputPath}\"");

    /// <summary>
    /// The arguments a <see cref="Recording"/> stub was given: one per line on Linux; on Windows the command
    /// line, split at its spaces, which none of the tests' arguments has.
    /// </summary>
    public static IReadOnlyList<string> RecordedArguments(string recordPath) =>
        OperatingSystem.IsWindows()
            ? File.ReadAllText(recordPath).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            : File.ReadAllLines(recordPath);

    /// <summary>Copies the file it was asked to read to <paramref name="copyPath"/>, then prints
    /// <paramref name="outputPath"/>: what ffprobe really got to read.</summary>
    public static string Copying(string folder, string copyPath, string outputPath) => Create(
        folder, "ffprobe-copying",
        $"for last in \"$@\"; do :; done\ncat \"${{last#file:}}\" > '{copyPath}'\ncat '{outputPath}'",
        $"{ShiftToLastArgument}\r\ntype \"%last:~5%\" > \"{copyPath}\"\r\ntype \"{outputPath}\"");

    /// <summary>Appends to the file it was asked to read, then prints <paramref name="outputPath"/>: a file still
    /// being written while ffprobe reads it.</summary>
    public static string Appending(string folder, string outputPath) => Create(
        folder, "ffprobe-appending",
        $"for last in \"$@\"; do :; done\nprintf 'grown' >> \"${{last#file:}}\"\ncat '{outputPath}'",
        $"{ShiftToLastArgument}\r\n>>\"%last:~5%\" echo grown\r\ntype \"{outputPath}\"");

    /// <summary>Writes the file <paramref name="errorPath"/> to its error stream, then prints
    /// <paramref name="outputPath"/>: a chatty ffprobe that succeeds.</summary>
    public static string Chatty(string folder, string errorPath, string outputPath) => Create(
        folder, "ffprobe-chatty",
        $"cat '{errorPath}' >&2\ncat '{outputPath}'",
        $"type \"{errorPath}\" 1>&2\r\ntype \"{outputPath}\"");

    private static string Create(string folder, string name, string unixScript, string windowsScript)
    {
        if (OperatingSystem.IsWindows())
        {
            var batch = Path.Combine(folder, name + ".cmd");
            File.WriteAllText(batch, "@echo off\r\n" + windowsScript + "\r\n");
            return batch;
        }

        var script = Path.Combine(folder, name);
        File.WriteAllText(script, "#!/bin/sh\n" + unixScript + "\n");
        File.SetUnixFileMode(
            script,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        return script;
    }
}
