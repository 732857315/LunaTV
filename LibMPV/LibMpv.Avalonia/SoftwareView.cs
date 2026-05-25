using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace HanumanInstitute.LibMpv.Avalonia;

public class SoftwareView : Control, IVideoView
{
    // MpvContext property
    public static readonly DirectProperty<SoftwareView, MpvContext> MpvContextProperty =
        AvaloniaProperty.RegisterDirect<SoftwareView, MpvContext>(
            nameof(MpvContext), o => o.MpvContext, defaultBindingMode: BindingMode.OneWayToSource);

    private WriteableBitmap? _renderTarget;

    public SoftwareView()
    {
        ClipToBounds = true;
    }

    public MpvContext MpvContext { get; } = new();

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected override void OnInitialized()
    {
        MpvContext.StartSoftwareRendering(UpdateVideoView);
        MpvContext.SetOptionString("vo", "libmpv");
        base.OnInitialized();
    }

    public override void Render(DrawingContext context)
    {
        if (VisualRoot == null)
        {
            return;
        }

        PixelSize bitmapSize = GetPixelSize();

        if (_renderTarget == null ||
            _renderTarget.PixelSize.Width != bitmapSize.Width ||
            _renderTarget.PixelSize.Height != bitmapSize.Height)
        {
            _renderTarget = new WriteableBitmap(bitmapSize, new Vector(96.0, 96.0), PixelFormat.Bgra8888,
                AlphaFormat.Premul);
        }

        using (ILockedFramebuffer lockedBitmap = _renderTarget.Lock())
        {
#if ANDROID
            var pix = "rgba";
#else
            string pix = "bgra";
#endif
            MpvContext.SoftwareRender(lockedBitmap.Size.Width, lockedBitmap.Size.Height, lockedBitmap.Address, pix);
        }

        context.DrawImage(_renderTarget,
            new Rect(0, 0, _renderTarget.PixelSize.Width, _renderTarget.PixelSize.Height));
    }

    private PixelSize GetPixelSize()
    {
        double scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        //return new PixelSize(Math.Max(1, (int)(Bounds.Width * scaling)),Math.Max(1, (int)(Bounds.Height * scaling)));
        return new PixelSize((int)Bounds.Width, (int)Bounds.Height);
    }

    private void UpdateVideoView()
    {
        Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Background);
    }

    protected virtual void Dispose(bool disposing)
    {
        // ReleaseUnmanagedResources();
        if (disposing)
        {
            MpvContext.Dispose();
            _renderTarget?.Dispose();
        }
    }

    ~SoftwareView()
    {
        Dispose(false);
    }
}