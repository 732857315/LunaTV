using Avalonia.Media;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using LunaTV.Constants;
using LunaTV.Extensions;
using SkiaSharp;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LunaTV.Logic.Media;

public partial class BurnInLogo : ObservableObject
{
    [ObservableProperty] private int _alpha;
    [ObservableProperty] private string _logoFileName;
    [ObservableProperty] private int _size;
    [ObservableProperty] private int _x;
    [ObservableProperty] private int _y;

    public BurnInLogo()
    {
        LogoFileName = string.Empty;
        Alpha = 100;
        Size = 100;
    }
}

public class FfmpegGenerator
{
    public static Process GenerateEmptyAudio(string outputFileName, float seconds, DataReceivedEventHandler? dataReceivedHandler = null)
    {
        var processMakeVideo = new Process
        {
            StartInfo =
            {
                FileName = GetFfmpegLocation(),
                Arguments = $"-f lavfi -i anullsrc -t {seconds.ToString(CultureInfo.InvariantCulture)} \"{outputFileName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        SetupDataReceiveHandler(dataReceivedHandler, processMakeVideo);

        return processMakeVideo;
    }

    public static Process MergeAudioTracks(string inputFileName1, string inputFileName2, string outputFileName, float startSeconds, bool forceStereo, DataReceivedEventHandler? dataReceivedHandler = null)
    {
        string filterSuffix = forceStereo ? ",aformat=channel_layouts=stereo" : string.Empty;
        string stereoParameter = forceStereo ? " -ac 2" : string.Empty;

        var processMakeVideo = new Process
        {
            StartInfo =
            {
                FileName = GetFfmpegLocation(),
                Arguments =
                    $"-i \"{inputFileName1}\" -i \"{inputFileName2}\" -filter_complex \"aevalsrc=0:d={startSeconds.ToString(CultureInfo.InvariantCulture)}[s1];[s1][1:a]concat=n=2:v=0:a=1[ac1];[0:a][ac1]amix=2:normalize=false{filterSuffix}[aout]\" -map [aout]{stereoParameter} \"{outputFileName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        SetupDataReceiveHandler(dataReceivedHandler, processMakeVideo);

        return processMakeVideo;
    }

    private static void SetupDataReceiveHandler(DataReceivedEventHandler? dataReceivedHandler, Process processMakeVideo)
    {
        if (dataReceivedHandler != null)
        {
            processMakeVideo.StartInfo.RedirectStandardOutput = true;
            processMakeVideo.StartInfo.RedirectStandardError = true;
            processMakeVideo.OutputDataReceived += dataReceivedHandler;
            processMakeVideo.ErrorDataReceived += dataReceivedHandler;
        }
    }

    /// <summary>
    ///     Generate ffmpeg parameters for a video with a burned-in Advanced Sub Station Alpha subtitle.
    /// </summary>
    public static string GenerateHardcodedVideoFile(string inputVideoFileName, string assaSubtitleFileName, string outputVideoFileName, int width, int height, string videoEncoding, string preset, string pixelFormat,
        string crf, string audioEncoding, bool forceStereo, string sampleRate, string tune, string audioBitRate, string pass, string twoPassBitRate, string? cutStart = null, string? cutEnd = null,
        string audioCutTrack = "", BurnInLogo? burnInLogo = null)
    {
        if (width % 2 == 1)
        {
            width++;
        }

        if (height % 2 == 1)
        {
            height++;
        }

        string videoEncodingSettings = string.Empty;
        if (!string.IsNullOrWhiteSpace(videoEncoding))
        {
            videoEncodingSettings = $"-c:v {videoEncoding}";
            if (videoEncoding == "libx265")
            {
                videoEncodingSettings += " -tag:v hvc1";
            }
        }

        string audioSettings = $"-c:a {audioEncoding}";
        if (audioEncoding != "copy")
        {
            audioSettings += $" -ar {sampleRate}";
            if (forceStereo)
            {
                audioSettings += " -ac 2";
            }
        }

        if (!string.IsNullOrWhiteSpace(pixelFormat))
        {
            pixelFormat = $"-pix_fmt {pixelFormat}";
        }

        audioSettings = audioCutTrack + " " + audioSettings;

        string presetSettings = string.Empty;
        if (!string.IsNullOrWhiteSpace(preset))
        {
            if (videoEncoding == "prores_ks")
            {
                if (preset == "proxy")
                {
                    preset = "0";
                }
                else if (preset == "lt")
                {
                    preset = "1";
                }
                else if (preset == "standard")
                {
                    preset = "2";
                }
                else if (preset == "hq")
                {
                    preset = "3";
                }
                else if (preset == "4444")
                {
                    preset = "4";
                }
                else if (preset == "4444xq")
                {
                    preset = "5";
                }
                else
                {
                    preset = string.Empty;
                }

                if (!string.IsNullOrWhiteSpace(preset))
                {
                    presetSettings = $" -profile:v {preset}";
                }
            }
            else
            {
                presetSettings = $" -preset {preset}";
            }
        }

        string crfSettings = string.Empty;
        if (!string.IsNullOrWhiteSpace(crf) && string.IsNullOrWhiteSpace(pass))
        {
            if (videoEncoding == "h264_nvenc" || videoEncoding == "hevc_nvenc")
            {
                crfSettings = $" -cq {crf}";
            }
            else if (videoEncoding == "h264_amf" || videoEncoding == "hevc_amf")
            {
                crfSettings = $" -quality {crf}";
            }
            else
            {
                crfSettings = $" -crf {crf}";
            }
        }

        string tuneParameter = string.Empty;
        if (!string.IsNullOrWhiteSpace(tune))
        {
            tuneParameter = $" -tune {tune}";
        }

        outputVideoFileName = $"\"{outputVideoFileName}\"";

        string passSettings = string.Empty;
        if (!string.IsNullOrWhiteSpace(pass) && !string.IsNullOrWhiteSpace(twoPassBitRate))
        {
            passSettings = $" -b:v {twoPassBitRate} -pass {pass}";

            if (!string.IsNullOrWhiteSpace(audioBitRate))
            {
                passSettings += $" -b:a {audioBitRate}";
            }

            if (pass == "1")
            {
                string ext = Path.GetExtension(outputVideoFileName.Trim('"')).ToLowerInvariant().TrimStart('.');
                string outputType = ext == "mkv" ? "matroska" : ext;
                outputVideoFileName = GlobalDefine.IsRunningOnWindows ? $"-f {outputType} NUL" : "-f mp4 /dev/null";
            }
        }

        if (!string.IsNullOrWhiteSpace(cutStart))
        {
            cutStart = " " + cutStart.Trim() + " ";
        }
        else
        {
            cutStart = " ";
        }

        if (!string.IsNullOrWhiteSpace(cutEnd))
        {
            cutEnd = " " + cutEnd.Trim() + " ";
        }
        else
        {
            cutEnd = " ";
        }

        // Add logo overlay if specified
        string logoInput = string.Empty;
        string filterParameter = $"-vf \"scale={width}:{height},ass={Path.GetFileName(assaSubtitleFileName)}\"";

        if (burnInLogo != null && !string.IsNullOrEmpty(burnInLogo.LogoFileName) && File.Exists(burnInLogo.LogoFileName))
        {
            logoInput = $" -i \"{burnInLogo.LogoFileName}\"";

            // Convert alpha percentage (0-100) to 0.0-1.0
            string alphaValue = (burnInLogo.Alpha / 100.0).ToString(CultureInfo.InvariantCulture);
            string sizePercent = burnInLogo.Size.ToString(CultureInfo.InvariantCulture);

            // Build filter_complex for video with logo overlay
            // 1. Scale main video and apply subtitles
            // 2. Scale logo by size percentage and apply alpha transparency
            // 3. Overlay logo at specified X, Y position
            string filterComplex = $"[0:v]scale={width}:{height},ass={Path.GetFileName(assaSubtitleFileName)}[withsubs];" +
                                   $"[1:v]scale=iw*{sizePercent}/100:ih*{sizePercent}/100,format=rgba,colorchannelmixer=aa={alphaValue}[logo];" +
                                   $"[withsubs][logo]overlay={burnInLogo.X}:{burnInLogo.Y}";

            filterParameter = $"-filter_complex \"{filterComplex}\"";
        }

        return
            $"{cutStart}-i \"{inputVideoFileName}\"{logoInput}{cutEnd} {filterParameter} -g 30 -bf 2 -s {width}x{height} {videoEncodingSettings} {passSettings} {presetSettings} {crfSettings} {pixelFormat} {audioSettings}{tuneParameter} -use_editlist 0 -movflags +faststart {outputVideoFileName}";
    }

    private static Process GetFFmpegProcess(string imageFileName, string outputFileName, int videoWidth, int videoHeight, int seconds, decimal frameRate, bool addTimeCode = false, string addTimeColor = "white")
    {
        string drawText = MakeDrawText(addTimeCode, frameRate, addTimeColor);

        return new Process
        {
            StartInfo =
            {
                FileName = GetFfmpegLocation(),
                Arguments =
                    $"-t {seconds} -loop 1 -r {frameRate.ToString(CultureInfo.InvariantCulture)} -i \"{imageFileName}\" -c:v libx264 -tune stillimage -shortest -s {videoWidth}x{videoHeight}{drawText} \"{outputFileName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
    }

    private static Process GetFFmpegProcess(Color color, string outputFileName, int videoWidth, int videoHeight, int seconds, decimal frameRate, bool addTimeCode = false, string addTimeColor = "white")
    {
        if (videoWidth % 2 == 1)
        {
            videoWidth++;
        }

        if (videoHeight % 2 == 1)
        {
            videoHeight++;
        }

        string htmlColor = $"#{(color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2")).ToUpperInvariant()}";

        string drawText = MakeDrawText(addTimeCode, frameRate, addTimeColor);

        return new Process
        {
            StartInfo =
            {
                FileName = GetFfmpegLocation(),
                Arguments =
                    $"-t {seconds} -f lavfi -i color=c={htmlColor}:r={frameRate.ToString(CultureInfo.InvariantCulture)}:s={videoWidth}x{videoHeight} -c:v libx264 -tune stillimage -shortest -s {videoWidth}x{videoHeight}{drawText} \"{outputFileName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
    }

    private static string MakeDrawText(bool addTimeCode, decimal frameRate, string addTimeColor)
    {
        string drawText = string.Empty;
        if (addTimeCode)
        {
            drawText = $" -vf \"drawtext=timecode='00\\:00\\:00\\:00':r={frameRate.ToString(CultureInfo.InvariantCulture)}:x=10:y=10:fontsize=34:fontcolor={addTimeColor}\"";
        }

        return drawText;
    }

    public static string GetScreenShot(string inputFileName, string timeCode, string colorMatrix = "")
    {
        timeCode = timeCode.Replace(',', '.');
        string outputFileName = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");
        string vfMatrix = string.Empty;
        if (!string.IsNullOrEmpty(colorMatrix))
        {
            vfMatrix = $"-vf colormatrix={colorMatrix}";
        }

        var process = new Process
        {
            StartInfo =
            {
                FileName = GetFfmpegLocation(),
                Arguments = $"-ss {timeCode} -i \"{inputFileName}\" {vfMatrix} -frames:v 1 -c:v png \"{outputFileName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

#pragma warning disable CA1416
        _ = process.Start();
#pragma warning restore CA1416

        process.WaitForExit();
        return outputFileName;
    }

    internal static string? GetScreenShotWithSubtitle(string previewSubtitle, int width, int height)
    {
        string tempAssFileName = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.ass");
        File.WriteAllText(tempAssFileName, previewSubtitle);

        string outputFileName = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.png");

        if (width % 2 == 1)
        {
            width++;
        }

        if (height % 2 == 1)
        {
            height++;
        }

        var process = new Process
        {
            StartInfo =
            {
                FileName = GetFfmpegLocation(),
                Arguments = $"-f lavfi -i \"color=c=black@0.0:s={width}x{height}:d=0.1,format=rgba,subtitles=f={Path.GetFileName(tempAssFileName)}:alpha=1\" -frames:v 1 -c:v png \"{outputFileName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(tempAssFileName) ?? string.Empty
            }
        };

#pragma warning disable CA1416
        _ = process.Start();
#pragma warning restore CA1416

        process.WaitForExit();

        try
        {
            if (File.Exists(tempAssFileName))
            {
                File.Delete(tempAssFileName);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }

        return File.Exists(outputFileName) ? outputFileName : null;
    }

    public static string[] GetScreenShotsForEachFrame(string videoFileName, string outputFolder)
    {
        Directory.CreateDirectory(outputFolder);
        string outputFileName = Path.Combine(outputFolder, "image%05d.png");
        var process = new Process
        {
            StartInfo =
            {
                FileName = GetFfmpegLocation(),
                Arguments = $"-i \"{videoFileName}\" -vf \"select=1\" -vsync vfr \"{outputFileName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

#pragma warning disable CA1416
        _ = process.Start();
#pragma warning restore CA1416
        process.WaitForExit();
        return Directory.GetFiles(outputFolder, "*.png").OrderBy(p => p).ToArray();
    }

    private static string GetFfmpegLocation()
    {
        string ffmpegLocation = GlobalDefine.FFmpegPath;
        if (!GlobalDefine.IsRunningOnWindows && (string.IsNullOrEmpty(ffmpegLocation) || !File.Exists(ffmpegLocation)))
        {
            ffmpegLocation = "ffmpeg";
        }

        return ffmpegLocation;
    }

    /// <summary>
    ///     Check if FFmpeg has rubberband filter support.
    /// </summary>
    public static bool IsRubberbandAvailable()
    {
        try
        {
            var process = new Process
            {
                StartInfo =
                {
                    FileName = GetFfmpegLocation(),
                    Arguments = "-filters",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
#pragma warning disable CA1416 // Validate platform compatibility
            _ = process.Start();
#pragma warning restore CA1416 // Validate platform compatibility
            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return output.Contains("rubberband");
        }
        catch
        {
            return false;
        }
    }

    public static Process ChangeSpeed(string inputFileName, string outputFileName, float inputSpeed, DataReceivedEventHandler? dataReceivedHandler = null)
    {
        float speed = Math.Max(0.5f, inputSpeed);
        speed = Math.Min(100, speed);
        speed = (float)Math.Round(speed, 3, MidpointRounding.AwayFromZero);

        var processMakeVideo = new Process
        {
            StartInfo =
            {
                FileName = GetFfmpegLocation(),
                Arguments = $"-i \"{inputFileName}\" -filter:a \"atempo={speed.ToString(CultureInfo.InvariantCulture)}\" \"{outputFileName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        SetupDataReceiveHandler(dataReceivedHandler, processMakeVideo);

        return processMakeVideo;
    }

    public static Process TrimSilenceStartAndEnd(string inputFileName, string outputFileName, DataReceivedEventHandler? dataReceivedHandler = null)
    {
        var processMakeVideo = new Process
        {
            StartInfo =
            {
                FileName = GetFfmpegLocation(),
                Arguments =
                    $"-i \"{inputFileName}\" -af \"areverse,atrim=start=0.1,silenceremove=start_periods=1:start_silence=0.1:start_threshold=0.01,areverse,atrim=start=0.1,silenceremove=start_periods=1:start_silence=0.1:start_threshold=0.01\" \"{outputFileName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        SetupDataReceiveHandler(dataReceivedHandler, processMakeVideo);

        return processMakeVideo;
    }

    /// <summary>
    ///     VAD-based internal silence compression: detects all silence gaps between words/phrases
    ///     and shortens them to a maximum duration, preserving speech segments untouched.
    ///     This is the first line of defense before time-stretching — it reduces audio duration
    ///     without affecting phonemes at all.
    /// </summary>
    /// <param name="maxSilenceSeconds">Maximum allowed silence duration between words (e.g. 0.15 for 150ms)</param>
    public static Process CompressInternalSilence(string inputFileName, string outputFileName, double maxSilenceSeconds = 0.15, DataReceivedEventHandler? dataReceivedHandler = null)
    {
        string maxSilence = maxSilenceSeconds.ToString("0.00", CultureInfo.InvariantCulture);
        // silenceremove: stop_periods=-1 processes ALL silence gaps (not just first)
        // stop_duration = max allowed silence length; stop_threshold = silence detection level
        // This keeps all speech intact and only compresses pauses between words
        string filter = $"silenceremove=stop_periods=-1:stop_duration={maxSilence}:stop_threshold=-40dB";

        var processMakeVideo = new Process
        {
            StartInfo =
            {
                FileName = GetFfmpegLocation(),
                Arguments = $"-i \"{inputFileName}\" -af \"{filter}\" \"{outputFileName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        SetupDataReceiveHandler(dataReceivedHandler, processMakeVideo);

        return processMakeVideo;
    }

    /// <summary>
    ///     High-quality pitch-preserving time-stretch using FFmpeg's rubberband filter (WSOLA-based).
    ///     Rubberband produces significantly better speech quality than atempo, especially at higher
    ///     speed factors, because it uses a proper WSOLA algorithm designed for speech/music.
    ///     Falls back to atempo if rubberband is not available in the FFmpeg build.
    /// </summary>
    public static Process ChangeSpeedHighQuality(string inputFileName, string outputFileName, float inputSpeed, DataReceivedEventHandler? dataReceivedHandler = null)
    {
        float speed = Math.Max(0.5f, inputSpeed);
        speed = Math.Min(100, speed);
        speed = (float)Math.Round(speed, 3, MidpointRounding.AwayFromZero);

        // rubberband filter: tempo parameter is the speed factor
        // transients=smooth: smoother transient handling for speech
        // engine=faster: use the faster engine (good enough for speech)
        // window=short: short analysis window, better for speech than music
        string speedStr = speed.ToString(CultureInfo.InvariantCulture);
        string filter = $"rubberband=tempo={speedStr}:transients=smooth:engine=faster:window=short";

        var processMakeVideo = new Process
        {
            StartInfo =
            {
                FileName = GetFfmpegLocation(),
                Arguments = $"-i \"{inputFileName}\" -af \"{filter}\" \"{outputFileName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        SetupDataReceiveHandler(dataReceivedHandler, processMakeVideo);

        return processMakeVideo;
    }

    public static Process AddAudioTrack(string inputFileName, string audioFileName, string outputFileName, string audioEncoding, bool? stereo, DataReceivedEventHandler? dataReceivedHandler = null)
    {
        string audioEncodingString = !string.IsNullOrEmpty(audioEncoding) ? "-c:a " + audioEncoding + " " : "-c:a copy ";
        string stereoString = stereo == true ? "-ac 2 " : string.Empty;

        var processMakeVideo = new Process
        {
            StartInfo =
            {
                FileName = GetFfmpegLocation(),
                Arguments = $"-i \"{inputFileName}\" -i \"{audioFileName}\" -c:v copy -map 0:v:0 -map 1:a:0 {audioEncodingString}{stereoString}\"{outputFileName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        SetupDataReceiveHandler(dataReceivedHandler, processMakeVideo);

        return processMakeVideo;
    }

    /// <summary>
    ///     Add audio track to video with ducking - reduce original audio volume and mix with TTS audio.
    /// </summary>
    public static Process AddAudioTrackWithDucking(string inputFileName, string audioFileName, string outputFileName, string audioEncoding, bool? stereo, int originalVolumePercent,
        DataReceivedEventHandler? dataReceivedHandler = null)
    {
        string audioEncodingString = !string.IsNullOrEmpty(audioEncoding) ? "-c:a " + audioEncoding + " " : string.Empty;
        string stereoString = stereo == true ? "-ac 2 " : string.Empty;
        string volumeFactor = Math.Clamp(originalVolumePercent / 100.0, 0.0, 1.0).ToString("0.00", CultureInfo.InvariantCulture);

        var processMakeVideo = new Process
        {
            StartInfo =
            {
                FileName = GetFfmpegLocation(),
                Arguments =
                    $"-i \"{inputFileName}\" -i \"{audioFileName}\" -filter_complex \"[0:a]volume={volumeFactor}[orig];[orig][1:a]amix=inputs=2:duration=longest:normalize=0[aout]\" -map 0:v:0 -map \"[aout]\" -c:v copy {audioEncodingString}{stereoString}\"{outputFileName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        SetupDataReceiveHandler(dataReceivedHandler, processMakeVideo);

        return processMakeVideo;
    }

    /// <summary>
    ///     Apply pro audio post-processing chain: low-pass, EQ warmth, compression, loudness normalization, noise gate, and
    ///     fade in/out.
    /// </summary>
    public static Process ApplyProAudioChain(string inputFileName, string outputFileName, DataReceivedEventHandler? dataReceivedHandler = null)
    {
        // Chain: low-pass 2400Hz → bass warmth +6dB@200Hz → treble reduce -5dB@2500Hz → noise gate → compression → loudness normalization → tiny fade in/out
        string filters = string.Join(",",
            "lowpass=f=2400",
            "equalizer=f=200:t=h:width=100:g=6",
            "equalizer=f=2500:t=h:width=500:g=-5",
            "agate=threshold=0.01:ratio=2:attack=5:release=50",
            "compand=attacks=0.3:decays=0.8:points=-80/-80|-45/-45|-27/-15|0/-3:soft-knee=6:gain=3",
            "loudnorm=I=-16:LRA=11:TP=-1.5",
            "afade=t=in:d=0.015",
            "areverse,afade=t=in:d=0.015,areverse");

        var processMakeVideo = new Process
        {
            StartInfo =
            {
                FileName = GetFfmpegLocation(),
                Arguments = $"-i \"{inputFileName}\" -af \"{filters}\" \"{outputFileName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        SetupDataReceiveHandler(dataReceivedHandler, processMakeVideo);

        return processMakeVideo;
    }

    /// <summary>
    ///     Generate a silence audio file with a given duration in milliseconds.
    /// </summary>
    public static Process GenerateSilence(string outputFileName, int durationMs, DataReceivedEventHandler? dataReceivedHandler = null)
    {
        string seconds = (durationMs / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);
        var processMakeVideo = new Process
        {
            StartInfo =
            {
                FileName = GetFfmpegLocation(),
                Arguments = $"-f lavfi -i anullsrc=r=24000:cl=mono -t {seconds} \"{outputFileName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        SetupDataReceiveHandler(dataReceivedHandler, processMakeVideo);

        return processMakeVideo;
    }

    /// <summary>
    ///     Concatenate two audio files (used for appending silence padding to a segment).
    /// </summary>
    public static Process ConcatAudio(string inputFileName1, string inputFileName2, string outputFileName, DataReceivedEventHandler? dataReceivedHandler = null)
    {
        var processMakeVideo = new Process
        {
            StartInfo =
            {
                FileName = GetFfmpegLocation(),
                Arguments = $"-i \"{inputFileName1}\" -i \"{inputFileName2}\" -filter_complex \"[0:a][1:a]concat=n=2:v=0:a=1[aout]\" -map \"[aout]\" \"{outputFileName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        SetupDataReceiveHandler(dataReceivedHandler, processMakeVideo);

        return processMakeVideo;
    }

    /// <summary>
    ///     Change sample rate of an audio file.
    /// </summary>
    public static Process ChangeSampleRate(string inputFileName, string outputFileName, int sampleRate, DataReceivedEventHandler? dataReceivedHandler = null)
    {
        var processMakeVideo = new Process
        {
            StartInfo =
            {
                FileName = GetFfmpegLocation(),
                Arguments = $"-i \"{inputFileName}\" -ar {sampleRate} \"{outputFileName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        SetupDataReceiveHandler(dataReceivedHandler, processMakeVideo);

        return processMakeVideo;
    }

    public static Process ConvertFormat(string inputFileName, string outputFileName, DataReceivedEventHandler? dataReceivedHandler = null)
    {
        var processMakeVideo = new Process
        {
            StartInfo =
            {
                FileName = GetFfmpegLocation(),
                Arguments = $"-i \"{inputFileName}\" \"{outputFileName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        SetupDataReceiveHandler(dataReceivedHandler, processMakeVideo);

        return processMakeVideo;
    }

    public static Process ConvertToAc2(string inputFileName, string outputFileName, DataReceivedEventHandler? dataReceivedHandler = null)
    {
        var processMakeVideo = new Process
        {
            StartInfo =
            {
                FileName = GetFfmpegLocation(),
                Arguments = $"-i \"{inputFileName}\" -ac 2 \"{outputFileName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        SetupDataReceiveHandler(dataReceivedHandler, processMakeVideo);

        return processMakeVideo;
    }

    /// <summary>
    ///     Resamples / mixes the input to mono PCM16 WAV at 24 kHz. Used for the Chatterbox TTS
    ///     voice-clone reference WAV, which only does "atomic" cloning at 24 kHz mono — other
    ///     sample rates / channel counts silently fall back to the default voice.
    /// </summary>
    public static Process ConvertToMono24kHzWav(string inputFileName, string outputFileName, DataReceivedEventHandler? dataReceivedHandler = null)
    {
        var process = new Process
        {
            StartInfo =
            {
                FileName = GetFfmpegLocation(),
                Arguments = $"-y -i \"{inputFileName}\" -ar 24000 -ac 1 -c:a pcm_s16le \"{outputFileName}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        SetupDataReceiveHandler(dataReceivedHandler, process);

        return process;
    }

    public static string GenerateTransparentVideoFile(string assaSubtitleFileName, string outputVideoFileName, int width, int height, string frameRate, string timeCode)
    {
        if (width % 2 == 1)
        {
            width++;
        }

        if (height % 2 == 1)
        {
            height++;
        }

        outputVideoFileName = $"\"{outputVideoFileName}\"";

        return
            $" -y -f lavfi -i \"color=c=black@0.0:s={width}x{height}:r={frameRate}:d={timeCode},format=rgba,subtitles=f={Path.GetFileName(assaSubtitleFileName)}:alpha=1\" -c:v prores_ks -profile:v 4444 -pix_fmt yuva444p10le {outputVideoFileName}"
                .TrimStart();
    }

    public static Process GenerateVideoFile(string previewFileName, int seconds, int width, int height, Color color, bool checkered, decimal frameRate, Bitmap? bitmap,
        DataReceivedEventHandler? dataReceivedHandler = null, bool addTimeCode = false, string addTimeColor = "white")
    {
        Process processMakeVideo;

        if (width % 2 == 1)
        {
            width++;
        }

        if (height % 2 == 1)
        {
            height++;
        }

        if (bitmap != null)
        {
            string tempImageFileName = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
            using (SKBitmap skBitmap = bitmap.ToSkBitmap())
            {
                using (SKBitmap resizedBitmap = ResizeBitmap(skBitmap, width, height))
                {
                    using (SKImage? image = SKImage.FromBitmap(resizedBitmap))
                    using (SKData? data = image.Encode(SKEncodedImageFormat.Png, 100))
                    using (FileStream stream = File.OpenWrite(tempImageFileName))
                    {
                        data.SaveTo(stream);
                    }
                }
            }
            processMakeVideo = GetFFmpegProcess(tempImageFileName, previewFileName, width, height, seconds, frameRate, addTimeCode, addTimeColor);
        }
        else if (checkered)
        {
            string tempImageFileName = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
            var skBitmap = new SKBitmap(width, height, true);
            using (var canvas = new SKCanvas(skBitmap))
            {
                UiUtil.DrawCheckerboardBackground(canvas, width, height);
                canvas.DrawBitmap(skBitmap, 0, 0);
            }

            using (SKBitmap resizedBitmap = ResizeBitmap(skBitmap, width, height))
            {
                using (SKImage? image = SKImage.FromBitmap(resizedBitmap))
                using (SKData? data = image.Encode(SKEncodedImageFormat.Png, 100))
                using (FileStream stream = File.OpenWrite(tempImageFileName))
                {
                    data.SaveTo(stream);
                }
            }

            processMakeVideo = GetFFmpegProcess(tempImageFileName, previewFileName, width, height, seconds, frameRate, addTimeCode, addTimeColor);
        }
        else
        {
            processMakeVideo = GetFFmpegProcess(color, previewFileName, width, height, seconds, frameRate, addTimeCode, addTimeColor);
        }

        SetupDataReceiveHandler(dataReceivedHandler, processMakeVideo);

        return processMakeVideo;
    }

    public static SKBitmap ResizeBitmap(SKBitmap originalBitmap, int width, int height)
    {
        var resizedBitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(resizedBitmap))
        {
            canvas.Clear(SKColors.Transparent);
            using (var paint = new SKPaint())
            {
                paint.IsAntialias = true;
                var destRect = new SKRect(0, 0, width, height);
                canvas.DrawBitmap(originalBitmap, destRect, paint);
            }
        }

        return resizedBitmap;
    }

    public static Process ReEncodeVideoForSubtitling(string inputVideoFileName, string outputVideoFileName, int width, int height, string frameRate, DataReceivedEventHandler? dataReceivedHandler)
    {
        if (width % 2 == 1)
        {
            width++;
        }

        if (height % 2 == 1)
        {
            height++;
        }

        outputVideoFileName = $"\"{outputVideoFileName}\"";
        int frameRateInt = (int)double.Parse(frameRate, CultureInfo.InvariantCulture);

        var processMakeVideo = new Process
        {
            StartInfo =
            {
                FileName = GetFfmpegLocation(),
                Arguments =
                    $"-y -i \"{inputVideoFileName}\" " +
                    $"-vf scale={width}:{height},fps={frameRate} " +
                    $"-c:v libx264 -preset ultrafast -movflags +faststart " +
                    $"-g {frameRateInt / 2} -keyint_min {frameRateInt / 2} -sc_threshold 0 " +
                    $"-pix_fmt yuv420p -c:a copy {outputVideoFileName}",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            }
        };

        processMakeVideo.StartInfo.Arguments = processMakeVideo.StartInfo.Arguments.Trim();

        SetupDataReceiveHandler(dataReceivedHandler, processMakeVideo);

        return processMakeVideo;
    }

    public static Process GetProcess(string parameters, DataReceivedEventHandler? dataReceivedHandler, string workingDirectory = "")
    {
        var processMakeVideo = new Process
        {
            StartInfo =
            {
                FileName = GetFfmpegLocation(),
                Arguments = parameters,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory
            }
        };

        processMakeVideo.StartInfo.Arguments = processMakeVideo.StartInfo.Arguments.Trim();

        SetupDataReceiveHandler(dataReceivedHandler, processMakeVideo);

        return processMakeVideo;
    }

    public static string GetReEncodeVideoForSubtitlingParameters(string inputVideoFileName, string outputVideoFileName, int width, int height, string frameRate)
    {
        if (width % 2 == 1)
        {
            width++;
        }

        if (height % 2 == 1)
        {
            height++;
        }

        outputVideoFileName = $"\"{outputVideoFileName}\"";

        string arguments =
            $"-y -i \"{inputVideoFileName}\" " +
            $"-vf scale={width}:{height},fps={frameRate} " +
            $"-c:v libx264 -preset veryfast -movflags +faststart " +
            $"-pix_fmt yuv420p -c:a copy {outputVideoFileName}";

        return arguments.Trim();
    }

    public static Process ListKeyFrames(string inputVideoFileName, DataReceivedEventHandler? dataReceivedHandler)
    {
        var process = new Process
        {
            StartInfo =
            {
                FileName = GetFfmpegLocation(),
                Arguments = $"-i \"{inputVideoFileName}\" -vf select='eq(pict_type\\,I)',showinfo -f null -",
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(inputVideoFileName) ?? string.Empty
            }
        };

        SetupDataReceiveHandler(dataReceivedHandler, process);

        return process;
    }

    internal static string ExtractAudioClipFromVideoParameters(
        string videoFileName,
        double startSeconds,
        double durationSeconds,
        bool useCenterChannelOnly,
        string outputFileName,
        int audioTrackFfIndex = -1)
    {
        string start = $"{startSeconds:0.000}".Replace(",", ".");
        string duration = $"{durationSeconds:0.000}".Replace(",", ".");

        // Base parameters
        string args = $"-y -ss {start} -t {duration} -i \"{videoFileName}\"";

        // Select the requested audio stream (e.g. for videos with multiple audio tracks).
        if (audioTrackFfIndex >= 0)
        {
            args += $" -map 0:{audioTrackFfIndex}";
        }

        args += " -vn -ar 16000 -b:a 32k";

        // Optional center-channel only
        if (useCenterChannelOnly)
        {
            // Extract center channel: pan mono|c0=c2
            args += " -af \"pan=mono|c0=c2\"";
        }

        // Add output file name
        args += $" \"{outputFileName}\"";

        return args;
    }
}