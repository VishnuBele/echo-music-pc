using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace EchoMusic.UI;


/// <summary>Single-line title marquee modeled on the Android app's Compose basicMarquee.</summary>
public sealed class ScrollingTitleTextBlock : UserControl
{
    private static readonly List<WeakReference<ScrollingTitleTextBlock>> Live = new();
    private static bool _resourceSaver;
    private readonly StackPanel _track = new() { Orientation = Orientation.Horizontal, Spacing = 16 };
    private readonly TextBlock _first = new() { TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.None };
    private readonly TextBlock _repeat = new() { TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.None };
    public static readonly StyledProperty<string?> TextProperty = AvaloniaProperty.Register<ScrollingTitleTextBlock, string?>(nameof(Text));
    private bool _attached;
    private double _overflow;
    private double _cycleDistance;
    private double _position;
    public int InitialDelayMilliseconds { get; set; } = 1500;
    private DateTime _startAtUtc;
    private DateTime _holdUntilUtc;

    public static event Action? AnimationRequested;

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (_first is null) return;
        if (change.Property == TextProperty)
        {
            _first.Text = Text ?? "";
            _repeat.Text = Text ?? "";
            ToolTip.SetTip(this, Text);
            RecheckOverflow();
        }
        else if (change.Property == TemplatedControl.FontSizeProperty || change.Property == TemplatedControl.FontWeightProperty || change.Property == TemplatedControl.ForegroundProperty)
        {
            ApplyTextStyle();
            RecheckOverflow();
        }
        else if (change.Property == IsVisibleProperty) RecheckOverflow();
    }

    public ScrollingTitleTextBlock()
    {
        ClipToBounds = true;
        _repeat.Opacity = 1;
        _track.Children.Add(_first);
        _track.Children.Add(_repeat);
        Content = _track;
        RenderTransform = new TranslateTransform();
        AttachedToVisualTree += (_, _) => { _attached = true; RecheckOverflow(); };
        DetachedFromVisualTree += (_, _) =>
        {
            _attached = false;
            _position = 0;
            RenderTransform = new TranslateTransform();
        };
        SizeChanged += (_, _) => RecheckOverflow();
        lock (Live) Live.Add(new WeakReference<ScrollingTitleTextBlock>(this));
    }

    public static void SetPerformanceMode(string mode, bool resourceSaver)
    {
        lock (Live)
        {
            _resourceSaver = resourceSaver;
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                if (!Live[i].TryGetTarget(out var title)) { Live.RemoveAt(i); continue; }
                title.RecheckOverflow();
            }
        }
    }

    public static bool HasActiveMarquees
    {
        get
        {
            lock (Live)
                return Live.Any(w => w.TryGetTarget(out var title) && title.IsMarqueeActive);
        }
    }

    public static void AdvanceMarquees(double elapsedSeconds, DateTime nowUtc)
    {
        lock (Live)
        {
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                if (!Live[i].TryGetTarget(out var title)) { Live.RemoveAt(i); continue; }
                title.AdvanceMarquee(elapsedSeconds, nowUtc);
            }
        }
    }

    private bool IsMarqueeActive => _attached && IsVisible && !_resourceSaver && _overflow > 6 && _cycleDistance > 1;

    private void ApplyTextStyle()
    {
        _first.FontSize = _repeat.FontSize = FontSize;
        _first.FontWeight = _repeat.FontWeight = FontWeight;
        _first.Foreground = _repeat.Foreground = Foreground;
    }

    private void RecheckOverflow()
    {
        ToolTip.SetTip(this, Text);
        if (!_attached || !IsVisible || string.IsNullOrWhiteSpace(Text) || Bounds.Width <= 1)
        {
            _overflow = 0;
            _cycleDistance = 0;
            _repeat.IsVisible = false;
            _position = 0;
            _track.RenderTransform = new TranslateTransform();
            return;
        }

        string title = Text ?? "";
        double sourceWidth = MeasureText(title);
        _overflow = Math.Max(0, sourceWidth - Bounds.Width - 2);
        if (_overflow <= 6 || _resourceSaver)
        {
            _overflow = 0;
            _cycleDistance = 0;
            _repeat.IsVisible = false;
            _position = 0;
            _track.RenderTransform = new TranslateTransform();
            return;
        }

        const string separator = "   ·   ";
        _cycleDistance = MeasureText(title + separator);
        _repeat.IsVisible = true;
        _position = 0;
        _startAtUtc = DateTime.UtcNow.AddMilliseconds(Math.Clamp(InitialDelayMilliseconds, 0, 10_000));
        _holdUntilUtc = DateTime.MinValue;
        AnimationRequested?.Invoke();
    }

    private double MeasureText(string value)
    {
        var probe = new TextBlock
        {
            Text = value,
            FontSize = FontSize,
            FontWeight = FontWeight,
            FontFamily = _first.FontFamily,
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.None,
        };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return probe.DesiredSize.Width;
    }

    private void AdvanceMarquee(double elapsedSeconds, DateTime nowUtc)
    {
        if (!IsMarqueeActive || nowUtc < _startAtUtc || nowUtc < _holdUntilUtc) return;
        _position += 30.0 * Math.Clamp(elapsedSeconds, 0, 0.05); // mobile player marquee velocity
        if (_position >= _cycleDistance)
        {
            _position %= _cycleDistance; // repeated title makes the wrap seamless
            _holdUntilUtc = nowUtc.AddMilliseconds(450);
        }
        _track.RenderTransform = new TranslateTransform(-_position, 0);
    }
}

/// <summary>
/// Renders the exact gradient-circle + audio-waveform vector logo:
///   <circle cx="12" cy="12" r="10.5" fill="url(#lg)" />
///   <path d="M7 12.6c1.2 0 1.2-3.2 2.4-3.2s1.3 5.6 2.5 5.6 1.3-7.2 2.6-7.2 1.2 4.8 2.5 4.8" />
/// </summary>
public sealed class EchoWaveLogoControl : Control
{
    private static readonly Geometry WavePath = StreamGeometry.Parse(
        "M7 12.6c1.2 0 1.2-3.2 2.4-3.2s1.3 5.6 2.5 5.6 1.3-7.2 2.6-7.2 1.2 4.8 2.5 4.8");

    public Color StartColor { get; set; } = Color.Parse("#1ED760");
    public Color EndColor { get; set; } = Color.Parse("#10B981");
    public Color WaveStroke { get; set; } = Color.Parse("#0B0B12");

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 4) return;

        double scale = size / 24.0;
        var grad = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new GradientStop(StartColor, 0),
                new GradientStop(EndColor, 1),
            }
        };

        using (context.PushTransform(Matrix.CreateScale(scale, scale)))
        {
            context.DrawEllipse(grad, null, new Point(12.0, 12.0), 10.5, 10.5);
            var pen = new Pen(new SolidColorBrush(WaveStroke), 1.75, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
            context.DrawGeometry(null, pen, WavePath);
        }
    }
}

/// <summary>
/// Minimal Spotify-style seek bar. Its progress line is always visible, while the circular
/// seek handle appears only on hover or during a drag. Drawing it directly avoids platform-
/// specific Fluent Slider thumb differences.
/// </summary>
public sealed class SeekProgressControl : Control
{
    public static readonly StyledProperty<double> MinimumProperty = AvaloniaProperty.Register<SeekProgressControl, double>(nameof(Minimum), 0);
    public static readonly StyledProperty<double> MaximumProperty = AvaloniaProperty.Register<SeekProgressControl, double>(nameof(Maximum), 100);
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<SeekProgressControl, double>(nameof(Value), 0);
    public static readonly StyledProperty<double> HandleScaleProperty = AvaloniaProperty.Register<SeekProgressControl, double>(nameof(HandleScale), 0);
    public static readonly StyledProperty<string> SliderStyleProperty = AvaloniaProperty.Register<SeekProgressControl, string>(nameof(SliderStyle), "Slim");
    public static readonly StyledProperty<bool> IsPlayingProperty = AvaloniaProperty.Register<SeekProgressControl, bool>(nameof(IsPlaying), false);
    public static readonly StyledProperty<bool> IsBufferingProperty = AvaloniaProperty.Register<SeekProgressControl, bool>(nameof(IsBuffering), false);
    public static readonly StyledProperty<IBrush?> TrackBrushProperty = AvaloniaProperty.Register<SeekProgressControl, IBrush?>(nameof(TrackBrush));
    public static readonly StyledProperty<IBrush?> ProgressBrushProperty = AvaloniaProperty.Register<SeekProgressControl, IBrush?>(nameof(ProgressBrush));
    public static readonly StyledProperty<IBrush?> HandleBrushProperty = AvaloniaProperty.Register<SeekProgressControl, IBrush?>(nameof(HandleBrush));

    private bool _pointerOver;
    private bool _dragging;

    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double HandleScale { get => GetValue(HandleScaleProperty); set => SetValue(HandleScaleProperty, value); }
    public event EventHandler? SeekCommitted;
    public string SliderStyle { get => GetValue(SliderStyleProperty); set => SetValue(SliderStyleProperty, value); }
    public bool IsPlaying { get => GetValue(IsPlayingProperty); set => SetValue(IsPlayingProperty, value); }
    public bool IsBuffering { get => GetValue(IsBufferingProperty); set => SetValue(IsBufferingProperty, value); }
    public IBrush? TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush? ProgressBrush { get => GetValue(ProgressBrushProperty); set => SetValue(ProgressBrushProperty, value); }
    public IBrush? HandleBrush { get => GetValue(HandleBrushProperty); set => SetValue(HandleBrushProperty, value); }

    public SeekProgressControl()
    {
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        MinHeight = 18;
        Transitions = new Avalonia.Animation.Transitions
        {
            new Avalonia.Animation.DoubleTransition
            {
                Property = HandleScaleProperty,
                Duration = TimeSpan.FromMilliseconds(130),
                Easing = new Avalonia.Animation.Easings.CubicEaseOut(),
            }
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ValueProperty || change.Property == MinimumProperty || change.Property == MaximumProperty ||
            change.Property == TrackBrushProperty || change.Property == ProgressBrushProperty || change.Property == HandleBrushProperty ||
            change.Property == HandleScaleProperty || change.Property == SliderStyleProperty || change.Property == IsPlayingProperty || change.Property == IsBufferingProperty)
            InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double width = Bounds.Width;
        double height = Bounds.Height;
        if (width <= 2 || height <= 2) return;

        const double sideInset = 6;
        double trackWidth = Math.Max(0, width - sideInset * 2);
        double centerY = height / 2;
        string style = SliderStyle is "Bar" or "Wavy" or "Slim" or "Squiggly" ? SliderStyle : "Slim";
        double trackHeight = style == "Bar" ? ((_pointerOver || _dragging) ? 7.0 : 5.5)
            : ((_pointerOver || _dragging) ? 4.0 : 3.0);
        double range = Math.Max(0.0001, Maximum - Minimum);
        double ratio = Math.Clamp((Value - Minimum) / range, 0, 1);
        double progressWidth = trackWidth * ratio;
        var trackBrush = TrackBrush ?? new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
        var progressBrush = ProgressBrush ?? Brushes.White;
        var handleBrush = HandleBrush ?? progressBrush;

        StreamGeometry? waveGeometry = null;
        double waveAmplitude = 0;
        double waveLength = 0;
        if (style is "Wavy" or "Squiggly")
        {
            double amplitude = style == "Squiggly" ? Math.Min(4.3, height * 0.24) : Math.Min(2.4, height * 0.14);
            double wavelength = style == "Squiggly" ? 28.0 : 52.0;
            waveAmplitude = amplitude;
            waveLength = wavelength;
            double phase = IsPlaying ? DateTime.UtcNow.TimeOfDay.TotalSeconds * 2.7 : 0;
            int steps = Math.Max(32, (int)Math.Ceiling(trackWidth / 2));
            var wave = new StreamGeometry();
            waveGeometry = wave;
            using (var path = wave.Open())
            {
                for (int i = 0; i <= steps; i++)
                {
                    double x = sideInset + trackWidth * i / steps;
                    double y = centerY + amplitude * Math.Sin((x / wavelength) * Math.PI * 2 - phase);
                    var point = new Point(x, y);
                    if (i == 0) path.BeginFigure(point, false);
                    else path.LineTo(point);
                }
                path.EndFigure(false);
            }
            var penWidth = style == "Squiggly" ? 3.6 : 3.2;
            using (context.PushClip(new Rect(sideInset, 0, trackWidth, height)))
            {
                context.DrawGeometry(null, new Pen(trackBrush, penWidth, lineCap: PenLineCap.Round), wave);
                if (progressWidth > 0.01)
                {
                    using (context.PushClip(new Rect(sideInset, 0, progressWidth, height)))
                        context.DrawGeometry(null, new Pen(progressBrush, penWidth, lineCap: PenLineCap.Round), wave);
                }
            }
        }
        else
        {
            context.DrawRectangle(trackBrush, null,
                new Rect(sideInset, centerY - trackHeight / 2, trackWidth, trackHeight), trackHeight / 2, trackHeight / 2);
            if (progressWidth > 0.01)
                context.DrawRectangle(progressBrush, null,
                    new Rect(sideInset, centerY - trackHeight / 2, progressWidth, trackHeight), trackHeight / 2, trackHeight / 2);
        }

        if (IsBuffering)
        {
            double segmentWidth = Math.Clamp(trackWidth * 0.20, 18, 46);
            double travel = trackWidth + segmentWidth;
            double phase = (DateTime.UtcNow.TimeOfDay.TotalSeconds * 86.0) % travel;
            double segmentX = sideInset + phase - segmentWidth;
            using (context.PushClip(new Rect(sideInset, 0, trackWidth, height)))
            using (context.PushClip(new Rect(segmentX, 0, segmentWidth, height)))
            {
                if (waveGeometry is not null)
                {
                    double lineWidth = style == "Squiggly" ? 3.6 : 3.2;
                    context.DrawGeometry(null, new Pen(progressBrush, lineWidth + 0.8, lineCap: PenLineCap.Round), waveGeometry);
                }
                else
                {
                    double loadingHeight = style == "Bar" ? trackHeight : Math.Max(3.2, trackHeight);
                    context.DrawRectangle(progressBrush, null,
                        new Rect(segmentX, centerY - loadingHeight / 2, segmentWidth, loadingHeight),
                        loadingHeight / 2, loadingHeight / 2);
                }
            }
        }

        if (HandleScale > 0.005)
        {
            double radius = style == "Bar" ? 5.2 * Math.Clamp(HandleScale, 0, 1) : 5.5 * Math.Clamp(HandleScale, 0, 1);
            double headX = sideInset + progressWidth;
            double headY = style is "Wavy" or "Squiggly"
                ? centerY + waveAmplitude
                    * Math.Sin((headX / waveLength) * Math.PI * 2 - (IsPlaying ? DateTime.UtcNow.TimeOfDay.TotalSeconds * 2.7 : 0))
                : centerY;
            context.DrawEllipse(handleBrush, null, new Point(headX, headY), radius, radius);
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        _pointerOver = true;
        HandleScale = 1;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _pointerOver = _dragging;
        if (!_dragging) HandleScale = 0;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!IsEnabled || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        _dragging = true;
        _pointerOver = true;
        HandleScale = 1;
        e.Pointer.Capture(this);
        SetValueFromPointer(e.GetPosition(this));
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging) SetValueFromPointer(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        SetValueFromPointer(e.GetPosition(this));
        _dragging = false;
        var localPosition = e.GetPosition(this);
        _pointerOver = localPosition.X >= 0 && localPosition.X <= Bounds.Width && localPosition.Y >= 0 && localPosition.Y <= Bounds.Height;
        HandleScale = _pointerOver ? 1 : 0;
        e.Pointer.Capture(null);
        e.Handled = true;
        InvalidateVisual();
        SeekCommitted?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        double step = Math.Max(0.5, (Maximum - Minimum) / 100.0);
        if (e.Key is Key.Left or Key.Down) Value = Math.Max(Minimum, Value - step);
        else if (e.Key is Key.Right or Key.Up) Value = Math.Min(Maximum, Value + step);
        else if (e.Key == Key.Home) Value = Minimum;
        else if (e.Key == Key.End) Value = Maximum;
        else return;
        e.Handled = true;
        SeekCommitted?.Invoke(this, EventArgs.Empty);
    }

    private void SetValueFromPointer(Point position)
    {
        double width = Math.Max(1, Bounds.Width - 12);
        double ratio = Math.Clamp((position.X - 6) / width, 0, 1);
        Value = Minimum + ratio * (Maximum - Minimum);
    }
}

/// <summary>
/// GPU-accelerated Skia audio spectrum visualizer for Player Bar & Fullscreen Player.
/// </summary>
public sealed class SpectrumVisualizerControl : Control
{
    private readonly float[] _bars = new float[32];
    private DateTime _lastUpdateUtc;
    public bool IsSettled => _bars.All(value => value <= 0.015f);
    public int BarCount { get; set; } = 12;
    public Color BarColor { get; set; } = Color.Parse("#1ED760");

    public void UpdateBands(ReadOnlySpan<float> bands32, bool isPlaying)
    {
        var now = DateTime.UtcNow;
        double dt = _lastUpdateUtc == default ? 0.05 : Math.Clamp((now - _lastUpdateUtc).TotalSeconds, 0.01, 0.25);
        _lastUpdateUtc = now;
        int n = Math.Min(_bars.Length, bands32.Length);
        for (int i = 0; i < n; i++)
        {
            float target = isPlaying ? Math.Clamp(bands32[i], 0f, 1f) : 0f;
            double tau = target > _bars[i] ? 0.075 : 0.24;
            float blend = (float)(1.0 - Math.Exp(-dt / tau));
            _bars[i] += (target - _bars[i]) * blend;
        }
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double w = Bounds.Width;
        double h = Bounds.Height;
        if (w <= 4 || h <= 4) return;

        int count = Math.Clamp(BarCount, 6, 32);
        double gap = 2.2;
        double barW = Math.Max(2.0, (w - gap * (count - 1)) / count);
        var activeBrush = new SolidColorBrush(BarColor);
        var idleBrush = new SolidColorBrush(Color.FromArgb(55, BarColor.R, BarColor.G, BarColor.B));

        for (int i = 0; i < count; i++)
        {
            int srcIdx = i * 28 / count;
            float val = Math.Clamp(_bars[srcIdx], 0f, 1f);
            double bh = Math.Max(3.0, val * (h - 2));
            double x = i * (barW + gap);
            double y = (h - bh) * 0.5;
            var rect = new Rect(x, y, barW, bh);
            context.DrawRectangle(val > 0.04f ? activeBrush : idleBrush, null, rect, barW * 0.5, barW * 0.5);
        }
    }
}

/// <summary>
/// Renders Spotify Desktop's crisp solid circular Play/Pause button (or optional 12-petal scalloped badge).
/// </summary>
public sealed class ScallopBadgeControl : Control
{
    private double _wavyProgress;
    private double _wavyStartProgress;
    private double _wavyTargetProgress;
    private DateTime _wavyTransitionStartedUtc;
    private bool _wavyTargetInitialized;

    public bool UseScallopShape { get; set; } = false;
    public bool UseWavyShape { get; set; } = true;
    public bool IsPlaying { get; set; }
    public bool IsLoading { get; set; }
    public Color FillColor { get; set; } = Colors.White;
    public Color GlyphColor { get; set; } = Colors.Black;

    /// <summary>Updates the petal-to-circle transition and rotation at display-frame cadence.</summary>
    public bool AdvanceAnimation(DateTime nowUtc)
    {
        double target = UseWavyShape && IsPlaying && !IsLoading ? 1.0 : 0.0;
        if (!_wavyTargetInitialized || Math.Abs(target - _wavyTargetProgress) > 0.001)
        {
            _wavyTargetInitialized = true;
            _wavyStartProgress = _wavyProgress;
            _wavyTargetProgress = target;
            _wavyTransitionStartedUtc = nowUtc;
        }

        bool transitioning = Math.Abs(_wavyProgress - _wavyTargetProgress) > 0.001;
        if (transitioning)
        {
            double t = Math.Clamp((nowUtc - _wavyTransitionStartedUtc).TotalMilliseconds / 280.0, 0, 1);
            double eased = t * t * (3 - 2 * t);
            _wavyProgress = _wavyStartProgress + (_wavyTargetProgress - _wavyStartProgress) * eased;
            if (t >= 1) _wavyProgress = _wavyTargetProgress;
        }

        bool animating = transitioning || (UseWavyShape && IsPlaying && !IsLoading);
        if (animating) InvalidateVisual();
        return animating;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 6) return;
        double cx = Bounds.Width * 0.5;
        double cy = Bounds.Height * 0.5;
        double rOuter = size * 0.48;
        var brush = new SolidColorBrush(FillColor);

        double wavyProgress = UseWavyShape ? Math.Clamp(_wavyProgress, 0, 1) : 0;
        bool animatedWavy = wavyProgress > 0.001;
        if (animatedWavy || UseScallopShape)
        {
            var geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                int petals = animatedWavy ? 9 : 12;
                int steps = 144;
                double rotation = animatedWavy ? DateTime.UtcNow.TimeOfDay.TotalSeconds * (2.0 * Math.PI / 8.0) * wavyProgress : 0;
                double indent = animatedWavy ? 0.08 * wavyProgress : 0.095;
                double baseRadius = animatedWavy ? 1.0 - indent : 0.905;
                for (int i = 0; i <= steps; i++)
                {
                    double theta = (2.0 * Math.PI * i) / steps;
                    double wave = animatedWavy
                        ? baseRadius + indent * Math.Cos(petals * (theta - rotation))
                        : baseRadius + indent * Math.Cos(petals * theta);
                    double r = rOuter * wave;
                    var pt = new Point(cx + r * Math.Cos(theta), cy + r * Math.Sin(theta));
                    if (i == 0) ctx.BeginFigure(pt, true);
                    else ctx.LineTo(pt);
                }
                ctx.EndFigure(true);
            }
            context.DrawGeometry(brush, null, geom);
        }
        else
        {
            context.DrawEllipse(brush, null, new Point(cx, cy), rOuter, rOuter);
        }

        var fg = new SolidColorBrush(GlyphColor);
        if (IsLoading)
        {
            var pen = new Pen(fg, Math.Max(2.0, size * 0.065), lineCap: PenLineCap.Round);
            double rr = size * 0.21;
            context.DrawEllipse(null, pen, new Point(cx, cy), rr, rr);
            return;
        }

        if (IsPlaying)
        {
            double bw = size * 0.095;
            double bh = size * 0.31;
            double gap = size * 0.062;
            context.DrawRectangle(fg, null, new Rect(cx - gap - bw, cy - bh * 0.5, bw, bh), 1.5, 1.5);
            context.DrawRectangle(fg, null, new Rect(cx + gap, cy - bh * 0.5, bw, bh), 1.5, 1.5);
        }
        else
        {
            var tri = new StreamGeometry();
            double s = size * 0.19;
            using (var g = tri.Open())
            {
                g.BeginFigure(new Point(cx - s * 0.52, cy - s * 0.92), true);
                g.LineTo(new Point(cx + s * 1.05, cy));
                g.LineTo(new Point(cx - s * 0.52, cy + s * 0.92));
                g.EndFigure(true);
            }
            context.DrawGeometry(fg, null, tri);
        }
    }
}
