using LunaTV.Logic.LibMpvDynamic;
using System.Threading.Tasks;

namespace LunaTV.Logic;

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