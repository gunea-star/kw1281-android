using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KW1281Android;

/// <summary>Descrieri pentru codurile de eroare VAG: întâi româna, apoi lista în engleză.</summary>
internal static class DtcDatabase
{
    private static Dictionary<int, (string Text, string Obd, bool Ro)>? _db;

    public static (string Text, string Obd, bool Ro)? Find(int code)
    {
        _db ??= Load();
        return _db.TryGetValue(code, out var v) ? v : null;
    }

    public static int Count => (_db ??= Load()).Count;

    private static Dictionary<int, (string, string, bool)> Load()
    {
        var db = new Dictionary<int, (string, string, bool)>();
        // engleza mai intai, apoi romana o suprascrie unde exista
        Read("STP.dtc_en.csv", line =>
        {
            var i = line.IndexOf(';');
            if (i != 5 || !int.TryParse(line[..5], out var code)) return;
            db[code] = (line[(i + 1)..].Trim(), "", false);
        });
        Read("STP.dtc_ro.csv", line =>
        {
            var p = line.Split(';', 3);
            if (p.Length < 3 || !int.TryParse(p[0], out var code)) return;
            db[code] = (p[2].Trim(), p[1].Trim(), true);
        });
        return db;
    }

    private static void Read(string resource, Action<string> onLine)
    {
        try
        {
            using var s = typeof(DtcDatabase).Assembly.GetManifestResourceStream(resource);
            if (s == null) return;
            using var r = new StreamReader(s, Encoding.UTF8);
            string? line;
            while ((line = r.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;
                onLine(line);
            }
        }
        catch
        {
            // fara baza de date aplicatia merge mai departe
        }
    }
}
