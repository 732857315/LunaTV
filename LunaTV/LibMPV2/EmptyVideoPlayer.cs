using System.Threading.Tasks;
using LunaTV.LibMpv2;
using LunaTV.LibMpv2.LibMpvDynamic;
using LunaTV.LibMPV2.LibMpvDynamic;

namespace LunaTV.LibMPV2;

public class EmptyVideoPlayer : IVideoPlayer
{
    public string Name => string.Empty;
    public string FileName { get; private set; } = string.Empty;

    public bool IsPlaying => false;

    public bool IsPaused => true;

    public double Position
    {
        get => 0;
        set { }
    }

    public double Duration => 0;

    public int VolumeMaximum => LibMpvDynamicPlayer.MaxVolume;

    public double Volume
    {
        get => 0;
        set { }
    }

    public double Speed
    {
        get => 0;
        set { }
    }

    public bool CanLoad()
    {
        return true;
    }

    public void CloseFile()
    {
        FileName = string.Empty;
    }

    public Task LoadFile(string fileName)
    {
        FileName = fileName;
        return Task.CompletedTask;
    }

    public void Pause()
    {
    }

    public void Play()
    {
    }

    public void PlayOrPause()
    {
    }

    public void Stop()
    {
    }

    public AudioTrackInfo? ToggleAudioTrack()
    {
        return null;
    }
}