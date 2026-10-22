using System;
using Android.Content;
using Android.Graphics;
using Android.Runtime;
using Android.Views;

namespace KW1281Android;

/// <summary>
/// Dashboard stil mașină de curse: turometru rotund mare în centru,
/// bandă LED shift deasupra și până la 7 gauge-uri dispuse pe margini.
/// Datele vin din Live Data (GroupRead) citind mai multe grupuri în buclă.
/// </summary>
internal sealed class DashboardView : View
{
    private static readonly Color CBg = Color.ParseColor("#05070A");
    private static readonly Color CRing = Color.ParseColor("#1B2130");
    private static readonly Color CAccent = Color.ParseColor("#FF6A00");
    private static readonly Color CGreen = Color.ParseColor("#22E07A");
    private static readonly Color CRed = Color.ParseColor("#FF3B4E");
    private static readonly Color CYellow = Color.ParseColor("#FFB000");
    private static readonly Color CBlue = Color.ParseColor("#2EC5FF");
    private static readonly Color CText = Color.ParseColor("#E8ECF3");
    private static readonly Color CMuted = Color.ParseColor("#8A93A6");

    public const int GaugeCount = 7;

    private readonly Paint _p = new() { AntiAlias = true };
    private readonly Paint _arc = new() { AntiAlias = true };
    private readonly Paint _txt = new() { AntiAlias = true, TextAlign = Paint.Align.Center };

    private float _rpm;
    private float _rpmMax = 7000f;
    private float _shiftFrom = 5800f;

    private readonly (string Label, float Value, string Unit, float Max)[] _g =
        new (string, float, string, float)[GaugeCount];

    public DashboardView(Context c) : base(c)
    {
        SetBackgroundColor(CBg);
        for (var i = 0; i < GaugeCount; i++) _g[i] = ("—", 0, "", 100);
    }

    public DashboardView(IntPtr h, JniHandleOwnership t) : base(h, t) { }

    public void SetRpm(float rpm, float max, float shiftFrom)
    {
        _rpm = rpm;
        if (max > 0) _rpmMax = max;
        _shiftFrom = shiftFrom > 0 ? shiftFrom : _rpmMax * 0.85f;
        Invalidate();
    }

    public void SetGauge(int i, string label, float value, string unit, float max)
    {
        if (i < 0 || i >= GaugeCount) return;
        _g[i] = (label, value, unit, max <= 0 ? 100 : max);
        Invalidate();
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        float w = Width, h = Height;
        if (w <= 0 || h <= 0) return;

        DrawShiftLeds(canvas, w, h * 0.07f);

        var cx = w / 2f;
        var cy = h * 0.46f;
        var radius = Math.Min(w, h) * 0.26f;
        DrawTach(canvas, cx, cy, radius);

        // 7 gauge-uri: 3 sus (stânga/centru-dreapta), 2 pe lateral, jos rândul cu restul
        var gr = Math.Min(w, h) * 0.11f;
        var lx = w * 0.15f;
        var rx = w * 0.85f;
        var row1 = h * 0.30f;   // sus stânga/dreapta
        var mid = h * 0.52f;    // lateral stânga/dreapta
        var row3 = h * 0.82f;   // jos

        // sus
        DrawGauge(canvas, 0, lx, row1, gr);
        DrawGauge(canvas, 1, rx, row1, gr);
        // lateral
        DrawGauge(canvas, 2, lx, mid, gr);
        DrawGauge(canvas, 3, rx, mid, gr);
        // jos (3 bucăți)
        DrawGauge(canvas, 4, w * 0.22f, row3, gr);
        DrawGauge(canvas, 5, w * 0.50f, row3, gr);
        DrawGauge(canvas, 6, w * 0.78f, row3, gr);
    }

    private void DrawShiftLeds(Canvas c, float w, float y)
    {
        const int n = 12;
        var gap = w * 0.02f;
        var total = w * 0.7f;
        var startX = (w - total) / 2f;
        var ledW = (total - gap * (n - 1)) / n;
        var frac = _rpmMax > 0 ? Math.Clamp((_rpm - _shiftFrom * 0.6f) / (_rpmMax - _shiftFrom * 0.6f), 0f, 1f) : 0f;
        var lit = (int)Math.Round(frac * n);
        var flash = _rpm >= _rpmMax && (DateTime.Now.Millisecond / 150) % 2 == 0;

        for (var i = 0; i < n; i++)
        {
            var col = i < 4 ? CGreen : i < 8 ? CRed : CBlue;
            _p.Color = (flash || i < lit) ? col : CRing;
            var x = startX + i * (ledW + gap);
            var r = new RectF(x, y - ledW * 0.4f, x + ledW, y + ledW * 0.4f);
            c.DrawRoundRect(r, 6, 6, _p);
        }
    }

    private void DrawTach(Canvas c, float cx, float cy, float radius)
    {
        const float startAngle = 135f, sweep = 270f;

        _arc.SetStyle(Paint.Style.Stroke!);
        _arc.StrokeWidth = radius * 0.14f;
        _arc.StrokeCap = Paint.Cap.Round;
        _arc.Color = CRing;
        var oval = new RectF(cx - radius, cy - radius, cx + radius, cy + radius);
        c.DrawArc(oval, startAngle, sweep, false, _arc);

        var frac = _rpmMax > 0 ? Math.Clamp(_rpm / _rpmMax, 0f, 1f) : 0f;
        _arc.Color = _rpm >= _shiftFrom ? CRed : (_rpm >= _shiftFrom * 0.85f ? CYellow : CAccent);
        c.DrawArc(oval, startAngle, sweep * frac, false, _arc);

        _p.SetStyle(Paint.Style.Stroke!);
        _p.StrokeWidth = 3;
        _p.Color = CMuted;
        var ticks = Math.Max(1, (int)(_rpmMax / 1000f));
        for (var i = 0; i <= ticks; i++)
        {
            var a = (startAngle + sweep * i / ticks) * (float)Math.PI / 180f;
            c.DrawLine(cx + (float)Math.Cos(a) * radius * 0.86f, cy + (float)Math.Sin(a) * radius * 0.86f,
                       cx + (float)Math.Cos(a) * radius * 1.02f, cy + (float)Math.Sin(a) * radius * 1.02f, _p);
        }

        _txt.Color = CText;
        _txt.TextSize = radius * 0.42f;
        _txt.FakeBoldText = true;
        c.DrawText(((int)Math.Round(_rpm)).ToString(), cx, cy + radius * 0.10f, _txt);

        _txt.Color = CMuted;
        _txt.TextSize = radius * 0.16f;
        _txt.FakeBoldText = false;
        c.DrawText("RPM", cx, cy + radius * 0.40f, _txt);
    }

    private void DrawGauge(Canvas c, int i, float cx, float cy, float radius)
    {
        var g = _g[i];
        const float startAngle = 135f, sweep = 270f;

        _arc.SetStyle(Paint.Style.Stroke!);
        _arc.StrokeWidth = radius * 0.18f;
        _arc.StrokeCap = Paint.Cap.Round;
        _arc.Color = CRing;
        var oval = new RectF(cx - radius, cy - radius, cx + radius, cy + radius);
        c.DrawArc(oval, startAngle, sweep, false, _arc);

        var frac = g.Max > 0 ? Math.Clamp(g.Value / g.Max, 0f, 1f) : 0f;
        _arc.Color = frac > 0.85f ? CRed : (frac > 0.65f ? CYellow : CGreen);
        c.DrawArc(oval, startAngle, sweep * frac, false, _arc);

        _txt.Color = CText;
        _txt.TextSize = radius * 0.46f;
        _txt.FakeBoldText = true;
        var val = Math.Abs(g.Value) >= 100 ? g.Value.ToString("0") : g.Value.ToString("0.0");
        c.DrawText(val, cx, cy + radius * 0.12f, _txt);

        _txt.Color = CMuted;
        _txt.TextSize = radius * 0.26f;
        _txt.FakeBoldText = false;
        c.DrawText(g.Unit, cx, cy + radius * 0.45f, _txt);

        _txt.Color = CAccent;
        _txt.TextSize = radius * 0.28f;
        _txt.FakeBoldText = true;
        c.DrawText(g.Label.Length <= 12 ? g.Label : g.Label[..12], cx, cy + radius * 1.45f, _txt);
    }
}
