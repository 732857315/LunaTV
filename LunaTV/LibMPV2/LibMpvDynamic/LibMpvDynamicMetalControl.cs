using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using LunaTV.LibMpv2.LibMpvDynamic;

namespace LunaTV.LibMPV2.LibMpvDynamic;

/// <summary>
///     macOS Metal-backed libmpv video control for Avalonia 12.
///     Uses the mpv Metal render API (MPV_RENDER_API_TYPE_METAL) with a
///     <c>CAMetalLayer</c> attached to the underlying <c>NSView</c>.
///     mpv manages drawable acquisition and presentation internally once
///     the layer is passed in the init params, so no per-frame drawable
///     management is needed from this side.
/// </summary>
[SupportedOSPlatform("macos")]
public class LibMpvDynamicMetalControl : NativeControlHost
{
    // MTLPixelFormat.bgra8Unorm = 80
    private const nuint MtlPixelFormatBgra8Unorm = 80;
    private bool _isInitialized;
    private int _lastPixelHeight;
    private int _lastPixelWidth;
    private IntPtr _metalLayer = IntPtr.Zero;
    private IntPtr _mtlDevice = IntPtr.Zero;

    public LibMpvDynamicMetalControl(LibMpvDynamicPlayer mpvPlayer)
    {
        Player = mpvPlayer;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    // ── Public API ───────────────────────────────────────────────────────

    public LibMpvDynamicPlayer? Player { get; }

    // ── Objective-C runtime ──────────────────────────────────────────────

    [DllImport("libobjc.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("libobjc.dylib")]
    private static extern IntPtr objc_getClass(string name);

    // Returns id
    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr msg_id(IntPtr self, IntPtr op);


    // void, one IntPtr arg
    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void msg_void_id(IntPtr self, IntPtr op, IntPtr arg);

    // void, BOOL (byte) arg
    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void msg_void_bool(IntPtr self, IntPtr op, byte arg);

    // void, NSUInteger arg  (pixel format enum, etc.)
    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void msg_void_nuint(IntPtr self, IntPtr op, nuint arg);

    // void, one double arg  (contentsScale)
    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void msg_void_double(IntPtr self, IntPtr op, double value);

    // void, two double args  (CGSize mapped as two doubles: width, height)
    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern void msg_void_cgsize(IntPtr self, IntPtr op, double width, double height);

    // double return  (backingScaleFactor)
    [DllImport("libobjc.dylib", EntryPoint = "objc_msgSend")]
    private static extern double msg_double(IntPtr self, IntPtr op);

    // ── Metal framework ──────────────────────────────────────────────────

    [DllImport("/System/Library/Frameworks/Metal.framework/Metal")]
    private static extern IntPtr MTLCreateSystemDefaultDevice();

    // ── NativeControlHost overrides ──────────────────────────────────────

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        var handle = base.CreateNativeControlCore(parent);

        if (!_isInitialized && Player != null)
            try
            {
                InitializeMetalOnView(handle.Handle);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MetalControl] Init failed: {ex.Message}");
            }

        return handle;
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (Player != null) Player.RequestRender -= OnMpvRequestRender;

        _isInitialized = false;
        base.DestroyNativeControlCore(control);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // Keep the Metal layer's drawable size in sync with the control's layout size.
        if (_isInitialized && _metalLayer != IntPtr.Zero && change.Property.Name == nameof(Bounds))
            UpdateDrawableSize();
    }

    // ── Setup ────────────────────────────────────────────────────────────

    private void InitializeMetalOnView(IntPtr nsView)
    {
        // 1. Get the default Metal device.
        _mtlDevice = MTLCreateSystemDefaultDevice();
        if (_mtlDevice == IntPtr.Zero)
            throw new InvalidOperationException("[MetalControl] MTLCreateSystemDefaultDevice returned NULL");

        // 2. Allocate and initialise a CAMetalLayer.
        var cls = objc_getClass("CAMetalLayer");
        var layerAlloc = msg_id(cls, sel_registerName("alloc"));
        _metalLayer = msg_id(layerAlloc, sel_registerName("init"));
        if (_metalLayer == IntPtr.Zero)
            throw new InvalidOperationException("[MetalControl] CAMetalLayer alloc/init returned NULL");

        // 3. Configure the layer.
        msg_void_id(_metalLayer, sel_registerName("setDevice:"), _mtlDevice);
        msg_void_nuint(_metalLayer, sel_registerName("setPixelFormat:"), MtlPixelFormatBgra8Unorm);
        // framebufferOnly = NO – allows mpv to sample the texture if needed.
        msg_void_bool(_metalLayer, sel_registerName("setFramebufferOnly:"), 0);

        // Sync HiDPI scale with the host window (may be 1.0 before a window exists,
        // but gets corrected on the next bounds change via UpdateDrawableSize).
        var window = msg_id(nsView, sel_registerName("window"));
        var scale = window != IntPtr.Zero
            ? msg_double(window, sel_registerName("backingScaleFactor"))
            : 1.0;
        msg_void_double(_metalLayer, sel_registerName("setContentsScale:"), scale);

        // 4. Attach the CAMetalLayer to the NSView.
        msg_void_bool(nsView, sel_registerName("setWantsLayer:"), 1); // YES
        msg_void_id(nsView, sel_registerName("setLayer:"), _metalLayer);

        // 5. Initialise mpv with the Metal render API.
        //    Passing both device AND layer means mpv will handle drawable
        //    acquisition and presentation internally.
        if (Player != null)
        {
            Player.InitializeWithMetal(_mtlDevice, _metalLayer);
            Player.PlayerSubName = "Metal";
            Player.RequestRender += OnMpvRequestRender;
        }

        _isInitialized = true;
        Debug.WriteLine("[MetalControl] Initialized successfully");
    }

    /// <summary>
    ///     Updates <c>CAMetalLayer.drawableSize</c> to match the current control
    ///     bounds scaled by the display's backing-scale factor.
    /// </summary>
    private void UpdateDrawableSize()
    {
        if (_metalLayer == IntPtr.Zero) return;

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        var pixelWidth = (int)(Bounds.Width * scaling);
        var pixelHeight = (int)(Bounds.Height * scaling);

        if (pixelWidth <= 0 || pixelHeight <= 0) return;

        if (pixelWidth == _lastPixelWidth && pixelHeight == _lastPixelHeight) return;

        _lastPixelWidth = pixelWidth;
        _lastPixelHeight = pixelHeight;

        // setDrawableSize: takes a CGSize (two CGFloat / double values).
        msg_void_cgsize(_metalLayer, sel_registerName("setDrawableSize:"),
            pixelWidth, pixelHeight);

        Debug.WriteLine($"[MetalControl] Drawable size → {pixelWidth}×{pixelHeight}");
    }

    // ── Render callback ──────────────────────────────────────────────────

    private void OnMpvRequestRender()
    {
        Dispatcher.UIThread.Post(DoRender, DispatcherPriority.Render);
    }

    private void DoRender()
    {
        if (!_isInitialized || Player == null || _metalLayer == IntPtr.Zero) return;

        // Keep drawable size up to date (guards against the first render
        // arriving before the first Bounds change notification).
        UpdateDrawableSize();

        // Nothing to show until a file is loaded.
        if (string.IsNullOrEmpty(Player.FileName)) return;

        try
        {
            // Because the CAMetalLayer was supplied in the init params,
            // mpv acquires nextDrawable and presents it automatically.
            Player.RenderMetal();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MetalControl] Render error: {ex.Message}");
        }
    }

    // ── Public helpers (mirror the other control types) ──────────────────

    public void LoadFile(string path)
    {
        Player?.LoadFile(path);
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