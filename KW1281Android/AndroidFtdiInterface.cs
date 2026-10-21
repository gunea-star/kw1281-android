using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Threading;
using Android.Hardware.Usb;
using BitFab.KW1281Test.Interface;

namespace KW1281Android;

/// <summary>
/// Implementarea IInterface din kw1281test pentru un cablu KKL cu cip FTDI conectat prin OTG.
/// Vorbeste direct cu cipul FTDI prin USB Host (fara driver D2XX), folosind comenzile
/// vendor documentate (aceleasi ca libftdi / usb-serial-for-android).
/// </summary>
internal sealed class AndroidFtdiInterface : IInterface
{
    // Cereri vendor FTDI
    private const int ReqOut = 0x40; // vendor | host->device | device
    private const int SioReset = 0;
    private const int SioModemCtrl = 1;
    private const int SioSetFlowCtrl = 2;
    private const int SioSetBaudRate = 3;
    private const int SioSetData = 4;
    private const int SioSetLatencyTimer = 9;

    private const int CtrlTimeoutMs = 1000;
    private const int MaxBulkChunk = 16384;

    private readonly UsbDeviceConnection _conn;
    private readonly UsbInterface _usbInterface;
    private readonly UsbEndpoint _epIn;
    private readonly UsbEndpoint _epOut;
    private readonly int _portIndex;      // 1 = interfata A
    private readonly bool _multiPort;     // FT2232/FT4232: index-ul de baud include portul
    private readonly int _packetSize;
    private readonly byte[] _rxRaw;
    private readonly byte[] _txChunk = new byte[MaxBulkChunk];
    private readonly Queue<byte> _rx = new();
    private readonly object _lock = new();

    private Parity _parity = Parity.None;
    private bool _break;
    private volatile bool _closed;

    // Setari alese in aplicatie
    public static int LatencyMs { get; set; } = 1;
    public static bool ForceRts { get; set; }
    public static bool DtrEnabled { get; set; } = true;

    // Contoare pentru becurile RX/TX si rata de transfer
    private static long _txBytes, _rxBytes;
    public static long TxBytes => Interlocked.Read(ref _txBytes);
    public static long RxBytes => Interlocked.Read(ref _rxBytes);

    public AndroidFtdiInterface(UsbDeviceConnection connection, UsbDevice device, int baudRate)
    {
        _conn = connection;
        _usbInterface = device.GetInterface(0);
        if (!_conn.ClaimInterface(_usbInterface, true))
        {
            _conn.Close();
            throw new InvalidOperationException("Nu pot revendica interfata USB a cablului.");
        }

        UsbEndpoint? epIn = null, epOut = null;
        for (var i = 0; i < _usbInterface.EndpointCount; i++)
        {
            var ep = _usbInterface.GetEndpoint(i)!;
            if (ep.Type != UsbAddressing.XferBulk) continue;
            if (ep.Direction == UsbAddressing.In) epIn = ep; else epOut = ep;
        }
        if (epIn == null || epOut == null)
        {
            Dispose();
            throw new InvalidOperationException("Cablul nu are endpoint-uri bulk (nu pare FTDI).");
        }
        _epIn = epIn;
        _epOut = epOut;
        _packetSize = Math.Max(_epIn.MaxPacketSize, 64);
        _rxRaw = new byte[_packetSize * 32];

        _multiPort = device.InterfaceCount > 1;
        _portIndex = 1;

        Control(SioReset, 0, _portIndex);               // reset
        Control(SioSetLatencyTimer, Math.Clamp(LatencyMs, 1, 255), _portIndex);
        Control(SioSetFlowCtrl, 0, _portIndex);         // fara flow control
        SetBaudRate(baudRate);
        ApplyLineSettings();
        SetDtr(true);
        SetRts(false);
        ClearReceiveBuffer();
    }

    public int ReadTimeout { get; set; } = 8000;
    public int WriteTimeout { get; set; } = 8000;

    public byte ReadByte()
    {
        lock (_lock)
        {
            if (_rx.Count > 0) return _rx.Dequeue();
        }

        var sw = Stopwatch.StartNew();
        while (true)
        {
            ThrowIfClosed();
            var timeout = ReadTimeout;
            int slice;
            if (timeout < 0)
            {
                slice = 100;
            }
            else
            {
                var remaining = timeout - (int)sw.ElapsedMilliseconds;
                if (remaining <= 0) throw new TimeoutException("Timeout la citirea de pe K-line.");
                slice = Math.Min(remaining, 100);
            }

            var n = _conn.BulkTransfer(_epIn, _rxRaw, _rxRaw.Length, slice);
            ThrowIfClosed();
            if (n > 2) Unpack(n);

            lock (_lock)
            {
                if (_rx.Count > 0) return _rx.Dequeue();
            }
        }
    }

    public void WriteByteRaw(byte b) => WriteBytesRaw([b]);

    public void WriteBytesRaw(byte[] bytes)
    {
        var offset = 0;
        while (offset < bytes.Length)
        {
            ThrowIfClosed();
            var len = Math.Min(MaxBulkChunk, bytes.Length - offset);
            Buffer.BlockCopy(bytes, offset, _txChunk, 0, len);
            var written = _conn.BulkTransfer(_epOut, _txChunk, len, WriteTimeout);
            if (written < 0) throw new TimeoutException("Timeout la scrierea pe K-line.");
            offset += written;
            Interlocked.Add(ref _txBytes, written);
        }
    }

    public void SetBreak(bool on)
    {
        _break = on;
        ApplyLineSettings();
    }

    public void ClearReceiveBuffer()
    {
        Control(SioReset, 1, _portIndex); // purge RX
        lock (_lock) _rx.Clear();
    }

    public void SetBaudRate(int baudRate)
    {
        var (value, index) = CalcDivisor(baudRate);
        if (_multiPort) index = (index << 8) | _portIndex;
        Control(SioSetBaudRate, value, index);
    }

    public void SetParity(Parity parity)
    {
        _parity = parity;
        ApplyLineSettings();
    }

    public void SetDtr(bool on) => Control(SioModemCtrl, 0x0100 | (on && DtrEnabled ? 0x01 : 0), _portIndex);

    public void SetRts(bool on) => Control(SioModemCtrl, 0x0200 | (on || ForceRts ? 0x02 : 0), _portIndex);

    public void Dispose()
    {
        if (_closed) return;
        try
        {
            SetDtr(false);
            SetRts(false);
        }
        catch
        {
            // ignoram la inchidere
        }
        _closed = true;
        try { _conn.ReleaseInterface(_usbInterface); } catch { }
        try { _conn.Close(); } catch { }
    }

    /// <summary>Folosit de butonul Stop: inchide conexiunea ca operatia curenta sa se opreasca.</summary>
    public void Abort() => Dispose();

    // ---------------------------------------------------------------------

    private void ApplyLineSettings()
    {
        var parityCode = _parity switch
        {
            Parity.Odd => 1,
            Parity.Even => 2,
            Parity.Mark => 3,
            Parity.Space => 4,
            _ => 0,
        };
        var value = 8 | (parityCode << 8) | (0 << 11) | (_break ? 1 << 14 : 0);
        Control(SioSetData, value, _portIndex);
    }

    private void Unpack(int n)
    {
        lock (_lock)
        {
            // Fiecare pachet USB de la FTDI incepe cu 2 octeti de status care trebuie eliminati.
            for (var off = 0; off < n; off += _packetSize)
            {
                var len = Math.Min(_packetSize, n - off);
                for (var i = 2; i < len; i++) _rx.Enqueue(_rxRaw[off + i]);
                if (len > 2) Interlocked.Add(ref _rxBytes, len - 2);
            }
        }
    }

    private void Control(int request, int value, int index)
    {
        ThrowIfClosed();
        var r = _conn.ControlTransfer((UsbAddressing)ReqOut, request, value, index, null, 0, CtrlTimeoutMs);
        if (r < 0)
        {
            throw new InvalidOperationException(
                $"Comanda USB FTDI {request} a esuat (cablul a fost deconectat?).");
        }
    }

    private void ThrowIfClosed()
    {
        if (_closed) throw new OperationCanceledException("Conexiunea USB a fost inchisa (Stop).");
    }

    /// <summary>Calculul divizorului de baud pentru FT232R/FT232BM/FT230X (ceas 48 MHz / 16).</summary>
    internal static (int value, int index) CalcDivisor(int baud)
    {
        const int clk = 48_000_000;
        const int clkDiv = 16;
        int[] frac = [0, 3, 2, 4, 1, 5, 6, 7];

        long encoded;
        if (baud >= clk / clkDiv) encoded = 0;
        else if (baud >= clk / (clkDiv + clkDiv / 2)) encoded = 1;
        else if (baud >= clk / (2 * clkDiv)) encoded = 2;
        else
        {
            var divisor = clk / baud; // = clk*16/clkDiv/baud
            var best = (divisor & 1) != 0 ? divisor / 2 + 1 : divisor / 2;
            if (best > 0x20000) best = 0x1FFFF;
            encoded = (long)(best >> 3) | ((long)frac[best & 7] << 14);
        }
        return ((int)(encoded & 0xFFFF), (int)(encoded >> 16));
    }
}
