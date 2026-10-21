using System;
using System.Collections.Generic;
using Android.Content;
using Android.Graphics;
using Android.Runtime;
using Android.Views;

namespace KW1281Android;

/// <summary>Grafic live pentru cele 4 câmpuri din Live Data (fiecare scalat separat).</summary>
internal sealed class GraphView : View
{
    public static readonly Color[] SeriesColors =
    [
        Color.ParseColor("#22E07A"), Color.ParseColor("#2EC5FF"),
        Color.ParseColor("#FFB000"), Color.ParseColor("#FF4FD8"),
    ];

    private const int MaxSamples = 150;
    private readonly List<float?[]> _samples = new();
    private readonly Paint _line = new() { AntiAlias = true, StrokeWidth = 5f };
    private readonly Paint _grid = new() { AntiAlias = true, StrokeWidth = 1f };

    public GraphView(Context context) : base(context)
    {
        _line.SetStyle(Paint.Style.Stroke!);
        _line.StrokeJoin = Paint.Join.Round;
        _grid.Color = Color.ParseColor("#262D3D");
    }

    public GraphView(IntPtr handle, JniHandleOwnership transfer) : base(handle, transfer)
    {
    }

    public void Add(float?[] values)
    {
        _samples.Add(values);
        if (_samples.Count > MaxSamples) _samples.RemoveAt(0);
        Invalidate();
    }

    public void Clear()
    {
        _samples.Clear();
        Invalidate();
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        float w = Width, h = Height;
        if (w <= 0 || h <= 0) return;

        for (var i = 1; i < 4; i++)
        {
            var y = h * i / 4f;
            canvas.DrawLine(0, y, w, y, _grid);
        }

        if (_samples.Count < 2) return;
        var pad = h * 0.08f;

        for (var s = 0; s < 4; s++)
        {
            float min = float.MaxValue, max = float.MinValue;
            foreach (var row in _samples)
            {
                if (row.Length <= s || row[s] is not { } v) continue;
                min = Math.Min(min, v);
                max = Math.Max(max, v);
            }
            if (min == float.MaxValue) continue;
            if (Math.Abs(max - min) < 1e-6f) { max += 1; min -= 1; }

            _line.Color = SeriesColors[s];
            var path = new Path();
            var started = false;
            for (var i = 0; i < _samples.Count; i++)
            {
                var row = _samples[i];
                if (row.Length <= s || row[s] is not { } v) { started = false; continue; }
                var x = w * i / (MaxSamples - 1f);
                var y = h - pad - (v - min) / (max - min) * (h - 2 * pad);
                if (!started) { path.MoveTo(x, y); started = true; }
                else path.LineTo(x, y);
            }
            canvas.DrawPath(path, _line);
        }
    }
}
