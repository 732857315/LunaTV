using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using System;
using System.Diagnostics;

namespace LunaTV.Logic.LibMpvDynamic;

public class LibMpvDynamicSoftwareControl : Control
{
    private bool _isInitialized;
    private WriteableBitmap? _renderTarget;

    public LibMpvDynamicSoftwareControl(LibMpvDynamicPlayer mpvPlayer)
    {
        Player = mpvPlayer;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    public LibMpvDynamicPlayer? Player { get; private set; }

    protected override void OnInitialized()
    {
        base.OnInitialized();

        if (Player == null)
        {
            throw new InvalidOperationException("MpvPlayer is not initialized");
        }

        Debug.WriteLine("Initializing MpvPlayer with software rendering");

        try
        {
            Player.InitializeWithSoftwareRendering();
            Player.PlayerSubName = "sw";
            Player.RequestRender += OnMpvRequestRender;
            _isInitialized = true;
            Debug.WriteLine("MpvPlayer initialized successfully with software rendering!");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to initialize MpvPlayer: {ex.Message}");
        }
    }

    private void OnMpvRequestRender()
    {
        // Request a redraw on the UI thread
        Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Background);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (!_isInitialized || Player == null || VisualRoot == null)
        {
            context.FillRectangle(Brushes.Black, new Rect(0, 0, Bounds.Width, Bounds.Height));
            return;
        }

        PixelSize bitmapSize = GetPixelSize();

        if (bitmapSize.Width <= 0 || bitmapSize.Height <= 0)
        {
            Debug.WriteLine("Skipping render - invalid size");
            return;
        }

        // Recreate bitmap if size changed
        if (_renderTarget == null ||
            _renderTarget.PixelSize.Width != bitmapSize.Width ||
            _renderTarget.PixelSize.Height != bitmapSize.Height)
        {
            _renderTarget?.Dispose();
            _renderTarget = new WriteableBitmap(
                bitmapSize,
                new Vector(96.0, 96.0),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);
        }

        // If no file is loaded, show black screen
        if (string.IsNullOrEmpty(Player.FileName))
        {
            context.FillRectangle(Brushes.Black, new Rect(0, 0, Bounds.Width, Bounds.Height));
            return;
        }

        try
        {
            using (ILockedFramebuffer lockedBitmap = _renderTarget.Lock())
            {
#if ANDROID
        var pixelFormat = "rgba";
#else
                string pixelFormat = "bgra";
#endif
                Player.SoftwareRender(
                    lockedBitmap.Size.Width,
                    lockedBitmap.Size.Height,
                    lockedBitmap.Address,
                    pixelFormat);
            }

            var destRect = new Rect(0, 0, Bounds.Width, Bounds.Height);
            context.DrawImage(_renderTarget, destRect);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Software render error: {ex.Message}");
            context.FillRectangle(Brushes.Black, new Rect(0, 0, Bounds.Width, Bounds.Height));
        }
    }

    private PixelSize GetPixelSize()
    {
        // Don't apply scaling - use bounds directly as pixel size
        // This matches the working LibMpv.Avalonia implementation
        return new PixelSize(
            (int)Bounds.Width,
            (int)Bounds.Height);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (Player != null)
        {
            Player.RequestRender -= OnMpvRequestRender;
            Player.Dispose();
            Player = null;
        }

        _renderTarget?.Dispose();
        _renderTarget = null;

        _isInitialized = false;
    }

    public void LoadFile(string path)
    {
        Player?.LoadFile(path);
        // Trigger initial render
        InvalidateVisual();
    }

    public void TogglePlayPause()
    {
        Player?.PlayOrPause();
    }

    public void Unload()
    {
        Player?.CloseFile();
    }
}