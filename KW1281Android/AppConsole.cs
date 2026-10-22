using System;
using System.Collections.Concurrent;
using System.Threading;

namespace BitFab.KW1281Test;

/// <summary>
/// [Android] Inlocuieste Console.ReadKey / KeyAvailable / SetCursorPosition din kw1281test.
/// Tastele vin din butoanele aplicatiei (N, Q, Y, sageti), iar "suprascrierea liniei"
/// folosita la Live Data este transmisa interfetei ca actualizare de panou.
/// </summary>
public static class AppConsole
{
    private static BlockingCollection<ConsoleKeyInfo> _keys = new();
    private static volatile bool _overlayNext;

    public static bool IsInputRedirected => false;

    public static bool KeyAvailable => _keys.Count > 0;

    public static ConsoleKeyInfo ReadKey() => ReadKey(false);

    public static ConsoleKeyInfo ReadKey(bool intercept) => _keys.Take();

    public static (int Left, int Top) GetCursorPosition() => (0, 0);

    public static void SetCursorPosition(int left, int top)
    {
        if (left == 0) _overlayNext = true;
    }

    /// <summary>True daca urmatorul text scris este o actualizare "pe loc" (Live Data).</summary>
    public static bool TakeOverlay()
    {
        if (!_overlayNext) return false;
        _overlayNext = false;
        return true;
    }

    /// <summary>Verifica fara sa consume semnalul de overlay.</summary>
    public static bool PeekOverlay() => _overlayNext;

    public static void PushKey(ConsoleKey key, char ch = '\0') =>
        _keys.Add(new ConsoleKeyInfo(ch, key, false, false, false));

    public static void Reset()
    {
        _keys = new BlockingCollection<ConsoleKeyInfo>();
        _overlayNext = false;
    }
}
