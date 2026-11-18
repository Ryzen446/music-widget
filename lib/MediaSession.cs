using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Media.Control;
using Windows.Storage.Streams;

public class MediaSessionReader
{
    public string Title { get; private set; } = "Musique";
    public string Artist { get; private set; } = "";
    public bool IsPlaying { get; private set; } = false;
    public bool HasMedia { get; private set; } = false;
    public byte[] ThumbnailBytes { get; private set; } = null;

    public async Task RefreshAsync()
    {
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = manager?.GetCurrentSession();
            if (session == null)
            {
                Reset();
                return;
            }

            var props = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();

            if (props == null || (string.IsNullOrWhiteSpace(props.Title) && string.IsNullOrWhiteSpace(props.Artist)))
            {
                Reset();
                return;
            }

            Title = string.IsNullOrWhiteSpace(props.Title) ? "Lecture en cours" : props.Title;
            Artist = string.IsNullOrWhiteSpace(props.Artist) ? "" : props.Artist;
            IsPlaying = playback != null && playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            HasMedia = true;

            if (props.Thumbnail != null)
            {
                try
                {
                    using (var stream = await props.Thumbnail.OpenReadAsync())
                    {
                        var size = (int)stream.Size;
                        if (size > 0 && size < 10 * 1024 * 1024)
                        {
                            var bytes = new byte[size];
                            using (var reader = new DataReader(stream))
                            {
                                await reader.LoadAsync((uint)size);
                                reader.ReadBytes(bytes);
                                ThumbnailBytes = bytes;
                            }
                        }
                        else
                        {
                            ThumbnailBytes = null;
                        }
                    }
                }
                catch
                {
                    ThumbnailBytes = null;
                }
            }
            else
            {
                ThumbnailBytes = null;
            }
        }
        catch
        {
            Reset();
        }
    }

    private void Reset()
    {
        Title = "Musique";
        Artist = "";
        IsPlaying = false;
        HasMedia = false;
        ThumbnailBytes = null;
    }
}
