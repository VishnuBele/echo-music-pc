using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace EchoMusic.UI;

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
/// GPU-accelerated Skia audio spectrum visualizer for Player Bar & Fullscreen Player.
/// </summary>
public sealed class SpectrumVisualizerControl : Control
{
    private readonly float[] _bars = new float[32];
    private DateTime _lastUpdateUtc;
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
    public bool UseScallopShape { get; set; } = false;
    public bool IsPlaying { get; set; }
    public bool IsLoading { get; set; }
    public Color FillColor { get; set; } = Colors.White;
    public Color GlyphColor { get; set; } = Colors.Black;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 6) return;
        double cx = Bounds.Width * 0.5;
        double cy = Bounds.Height * 0.5;
        double rOuter = size * 0.48;
        var brush = new SolidColorBrush(FillColor);

        if (UseScallopShape)
        {
            var geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                const int petals = 12;
                const int steps = 120;
                for (int i = 0; i <= steps; i++)
                {
                    double theta = (2.0 * Math.PI * i) / steps;
                    double wave = 0.905 + 0.095 * Math.Cos(petals * theta);
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
