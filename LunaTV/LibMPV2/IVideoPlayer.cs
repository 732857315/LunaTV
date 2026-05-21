using System.Threading.Tasks;
using LunaTV.LibMPV2.LibMpvDynamic;

namespace LunaTV.LibMpv2;

public interface IVideoPlayer
{
    string Name { get; }
    string FileName { get; }

    bool IsPlaying { get; }
    bool IsPaused { get; }

    double Position { get; set; }
    double Duration { get; }

    int VolumeMaximum { get; }
    double Volume { get; set; }

    double Speed { get; set; }

    bool CanLoad();
    Task LoadFile(string fileName);
    void CloseFile();

    void Play();
    void PlayOrPause();
    void Pause();
    void Stop();
    AudioTrackInfo? ToggleAudioTrack();
}