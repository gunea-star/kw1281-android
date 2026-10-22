using System;
using Android.Content;
using Android.Graphics;
using Android.Runtime;
using Android.Util;
using Android.Views;

namespace KW1281Android;

/// <summary>
/// Dashboard stil mașină de curse: turometru rotund mare în centru,
/// bandă LED shift deasupra și 4 gauge-uri mici în colțuri.
/// Datele vin din Live Data (GroupRead), deci ritmul e cel al K-line.
/// </summary>
internal sealed class DashboardView : View
{
    private static readonly Color CBg = Color.ParseColor("#05070A");
    private static readonly Color CRing = Color.ParseColor("#1B2130");
    private static readonly Color CAccent = Color.ParseColor("#FF6A00");
    private static readonly Color CGreen = Color.ParseColor("#22E07A");
    private static readonly Color CRed = Color.ParseColor("#FF3B4E");
    private static readonly Color CYellow = Color.ParseColor("#FFB000");
    private static readonly Color CText = Color.ParseColor("#E8ECF3");
    private static readonly Color CMuted = Color.ParseColor("#8A93A6");

    private readonly Paint _p = new() { AntiAlias = true };
    private readonly Paint _arc = new() { AntiAlias = true };
    private readonly Paint _txt = new() { AntiAlias = true, TextAlign = Paint.Align.Center };

    private float _rpm;
    private float _rpmMax = 7000f;
    private float _shiftFrom = 5800f;

    // 4 gauge-uri: etichetă, valoare, unitate, max
    private readonly (string Label, float Value, string Unit, float Max)[] _gauges =
    {
        ("—", 0, "", 100), ("—", 0, "", 100), ("—", 0, "", 100), ("—", 0, "", 100),
    };

    public DashboardView(Context c) : base(c) => SetBackgroundColor(CBg);
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
        if (i < 0 || i >= 4) return;
        _gauges[i] = (label, value, unit, max <= 0 ? 100 : max);
        Invalidate();
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        float w = Width, h = Height;
        if (w <= 0 || h <= 0) return;

        // Banda LED shift sus
        DrawShiftLeds(canvas, w, h * 0.10f);

        // Turometru central
        var cx = w / 2f;
        var cy = h * 0.52f;
        var radius = Math.Min(w, h) * 0.30f;
        DrawTach(canvas, cx, cy, radius);

        // 4 gauge-uri în colțuri
        var gr = Math.Min(w, h) * 0.13f;
        var mx = w * 0.17f;
        var topY = h * 0.34f;
        var botY = h * 0.78f;
        DrawGauge(canvas, 0, mx, topY, gr);
        DrawGauge(canvas, 1, w - mx, topY, gr);
        DrawGauge(canvas, 2, mx, botY, gr);
        DrawGauge(canvas, 3, w - mx, botY, gr);
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
            Color col;
            if (i < 4) col = CGreen;
            else if (i < 8) col = CRed;
            else col = Color.ParseColor("#2EC5FF");
            var on = flash || i < lit;
            _p.Color = on ? col : CRing;
            var x = startX + i * (ledW + gap);
            var r = new RectF(x, y - ledW * 0.4f, x + ledW, y + ledW * 0.4f);
            c.DrawRoundRect(r, 6, 6, _p);
        }
    }

    private void DrawTach(Canvas c, float cx, float cy, float radius)
    {
        const float startAngle = 135f;
        const float sweep = 270f;

        // inel de fundal
        _arc.SetStyle(Paint.Style.Stroke!);
        _arc.StrokeWidth = radius * 0.14f;
        _arc.StrokeCap = Paint.Cap.Round;
        _arc.Color = CRing;
        var oval = new RectF(cx - radius, cy - radius, cx + radius, cy + radius);
        c.DrawArc(oval, startAngle, sweep, false, _arc);

        // arc activ
        var frac = _rpmMax > 0 ? Math.Clamp(_rpm / _rpmMax, 0f, 1f) : 0f;
        _arc.Color = _rpm >= _shiftFrom ? CRed : (_rpm >= _shiftFrom * 0.85f ? CYellow : CAccent);
        c.DrawArc(oval, startAngle, sweep * frac, false, _arc);

        // repere (0..rpmMax la fiecare 1000)
        _p.SetStyle(Paint.Style.Stroke!);
        _p.StrokeWidth = 3;
        _p.Color = CMuted;
        var ticks = (int)(_rpmMax / 1000f);
        for (var i = 0; i <= ticks; i++)
        {
            var a = (startAngle + sweep * i / ticks) * (float)Math.PI / 180f;
            var r1 = radius * 1.02f;
            var r2 = radius * 0.86f;
            c.DrawLine(cx + (float)Math.Cos(a) * r2, cy + (float)Math.Sin(a) * r2,
                       cx + (float)Math.Cos(a) * r1, cy + (float)Math.Sin(a) * r1, _p);
        }

        // RPM mare în centru — valoarea întreagă, ex: 2000
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
        var g = _gauges[i];
        const float startAngle = 135f;
        const float sweep = 270f;

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
        _txt.TextSize = radius * 0.42f;
        _txt.FakeBoldText = true;
        var val = Math.Abs(g.Value) >= 100 ? g.Value.ToString("0") : g.Value.ToString("0.0");
        c.DrawText(val, cx, cy + radius * 0.10f, _txt);

        _txt.Color = CMuted;
        _txt.TextSize = radius * 0.24f;
        _txt.FakeBoldText = false;
        c.DrawText(g.Unit, cx, cy + radius * 0.42f, _txt);

        // eticheta sub gauge
        _txt.Color = CAccent;
        _txt.TextSize = radius * 0.26f;
        _txt.FakeBoldText = true;
        c.DrawText(Trunc(g.Label, 14), cx, cy + radius * 1.4f, _txt);
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..n];
}
