using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Hardware.Usb;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace KW1281Android;

[Activity(Label = "KW1281 Android", MainLauncher = true, Exported = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden)]
[IntentFilter(new[] { UsbManager.ActionUsbDeviceAttached })]
[MetaData(UsbManager.ActionUsbDeviceAttached, Resource = "@xml/device_filter")]
public class MainActivity : Activity
{
    private const string ActionUsbPermission = "ro.kw1281test.android.USB_PERMISSION";
    private const int ReqImport = 1001;
    private const int ReqExport = 1002;
    private const int FtdiVendorId = 0x0403;

    // Comanda -> ce argumente suplimentare asteapta (din "Usage" kw1281test v1.0.4)
    private static readonly (string Cmd, string Hint)[] Commands =
    [
        ("ReadIdent", "fara argumente"),
        ("ReadFaultCodes", "fara argumente"),
        ("ClearFaultCodes", "fara argumente"),
        ("ReadSoftwareVersion", "fara argumente"),
        ("GetSKC", "fara argumente (PIN imobilizator din bord, adresa 17)"),
        ("GetClusterId", "fara argumente"),
        ("AutoScan", "fara argumente"),
        ("GroupRead", "GROUP [fastinit]"),
        ("BasicSetting", "GROUP"),
        ("ActuatorTest", "fara argumente (necesita tasta N/Q - limitat pe Android)"),
        ("AdaptationRead", "CHANNEL [LOGIN]"),
        ("AdaptationTest", "CHANNEL VALUE [LOGIN]"),
        ("AdaptationSave", "CHANNEL VALUE [LOGIN]"),
        ("SetSoftwareCoding", "CODING WORKSHOP"),
        ("FindLogins", "LOGIN"),
        ("DumpEeprom", "START LENGTH [FILENAME]  sau  FILENAME (bord/airbag)"),
        ("ReadEeprom", "ADDRESS"),
        ("WriteEeprom", "ADDRESS VALUE"),
        ("LoadEeprom", "START FILENAME"),
        ("MapEeprom", "[FILENAME]"),
        ("DumpMarelliMem", "START LENGTH [FILENAME]"),
        ("DumpRBxMem", "START LENGTH [FILENAME]"),
        ("DumpRBxMemOdd", "START LENGTH [FILENAME]"),
        ("DumpMem", "START LENGTH [FILENAME]"),
        ("DumpRam", "START LENGTH [FILENAME]"),
        ("DumpRom", "START LENGTH [FILENAME]"),
        ("ReadRAM", "ADDRESS"),
        ("ReadROM", "ADDRESS"),
        ("WriteRAM", "ADDRESS VALUE"),
        ("DumpCcmRom", "[FILENAME]"),
        ("DumpClusterNecRom", "[FILENAME]"),
        ("ToggleRB4Mode", "fara argumente"),
        ("Reset", "fara argumente"),
        ("ClarionVWPremium4SafeCode", "fara argumente (adresa 56)"),
        ("DelcoVWPremium5SafeCode", "fara argumente (adresa 56)"),
        ("DumpEdc15Eeprom", "[FILENAME]"),
        ("WriteEdc15Eeprom", "ADDR1 VAL1 [ADDR2 VAL2 ...]"),
        ("LoadEdc15Eeprom", "[START] FILENAME"),
        ("DumpEdc15Flash", "[Low|Medium|High] [FILENAME]"),
        ("LoadEdc15Flash", "[Low|Medium|High] [full] [noverify] FILENAME"),
        ("DumpEdc15FlashBoot", "[FILENAME]  (ECU in boot mode, 28800 baud)"),
        ("LoadEdc15FlashBoot", "FILENAME  (ECU in boot mode)"),
        ("DumpEdc16Flash", "[Low|Medium|High] [FILENAME]"),
        ("LoadEdc16Flash", "[Low|Medium|High] [full] [unverified] [fastinit] FILENAME"),
    ];

    private UsbManager _usb = null!;
    private UsbDevice? _device;
    private string _workDir = "";
    private string? _pendingExport;

    private TextView _status = null!;
    private EditText _baud = null!, _address = null!, _args = null!, _input = null!;
    private Spinner _command = null!;
    private TextView _hint = null!, _log = null!;
    private ScrollView _logScroll = null!;
    private Button _run = null!, _stop = null!;

    private Thread? _worker;
    private AndroidFtdiInterface? _current;
    private readonly Handler _ui = new(Looper.MainLooper!);
    private UiWriter? _writer;
    private BlockingLineReader? _reader;
    private UsbPermissionReceiver? _permReceiver;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);

        _usb = (UsbManager)GetSystemService(UsbService)!;
        _workDir = GetExternalFilesDir(null)?.AbsolutePath ?? FilesDir!.AbsolutePath;
        Directory.CreateDirectory(_workDir);

        BuildUi();

        // Iesirea consolei kw1281test -> ecran
        _writer = new UiWriter(AppendLog);
        Console.SetOut(_writer);
        Console.SetError(_writer);
        _reader = new BlockingLineReader();
        Console.SetIn(_reader);

        // kw1281test va deschide cablul prin aceasta fabrica in loc de COM
        BitFab.KW1281Test.Program.InterfaceFactory = (_, baud) =>
        {
            var dev = _device ?? throw new InvalidOperationException("Niciun cablu FTDI selectat.");
            var conn = _usb.OpenDevice(dev)
                       ?? throw new InvalidOperationException("Nu pot deschide cablul (lipsa permisiune?).");
            var iface = new AndroidFtdiInterface(conn, dev, baud);
            _current = iface;
            return iface;
        };

        _permReceiver = new UsbPermissionReceiver(this);
        var filter = new IntentFilter(ActionUsbPermission);
        filter.AddAction(UsbManager.ActionUsbDeviceDetached);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            RegisterReceiver(_permReceiver, filter, ReceiverFlags.NotExported);
        else
            RegisterReceiver(_permReceiver, filter);

        AppendLog($"Fisierele (dump-uri, log) se salveaza in:\n{_workDir}\n\n");
        FindCable();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        FindCable(); // cablu conectat cat timp aplicatia era deschisa
    }

    protected override void OnDestroy()
    {
        if (_permReceiver != null) UnregisterReceiver(_permReceiver);
        base.OnDestroy();
    }

    // ------------------------------------------------------------------ UI

    private void BuildUi()
    {
        var pad = Dp(8);
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetPadding(pad, pad, pad, pad);

        _status = new TextView(this) { Text = "Caut cablul..." };
        _status.SetTypeface(null, TypefaceStyle.Bold);
        var findBtn = new Button(this) { Text = "Cauta cablu USB" };
        findBtn.Click += (_, _) => FindCable();
        root.AddView(Row(_status, 1f, findBtn, 0f));

        _baud = new EditText(this) { Text = "10400", Hint = "Baud", InputType = Android.Text.InputTypes.ClassNumber };
        _address = new EditText(this) { Text = "17", Hint = "Adresa (hex)" };
        root.AddView(Row(Label("Baud:"), 0f, _baud, 1f, Label(" Adresa:"), 0f, _address, 1f));

        _command = new Spinner(this);
        var adapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem,
            Commands.Select(c => c.Cmd).ToList());
        adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
        _command.Adapter = adapter;
        _command.ItemSelected += (_, e) => _hint!.Text = "Argumente: " + Commands[e.Position].Hint;
        root.AddView(_command);

        _hint = new TextView(this) { Text = "" };
        _hint.SetTextSize(Android.Util.ComplexUnitType.Sp, 12);
        root.AddView(_hint);

        _args = new EditText(this) { Hint = "Argumente suplimentare (ex: 0 2048 bord.bin)" };
        _args.SetSingleLine(true);
        root.AddView(_args);

        _run = new Button(this) { Text = "Ruleaza" };
        _run.Click += (_, _) => RunCommand();
        _stop = new Button(this) { Text = "Stop", Enabled = false };
        _stop.Click += (_, _) => { AppendLog("\n[Stop cerut]\n"); _current?.Abort(); _reader?.Cancel(); };
        var importBtn = new Button(this) { Text = "Importa" };
        importBtn.Click += (_, _) => ImportFile();
        var exportBtn = new Button(this) { Text = "Exporta" };
        exportBtn.Click += (_, _) => ExportFile();
        root.AddView(Row(_run, 1f, _stop, 1f, importBtn, 1f, exportBtn, 1f));

        _logScroll = new ScrollView(this);
        _log = new TextView(this) { Typeface = Typeface.Monospace };
        _log.SetTextSize(Android.Util.ComplexUnitType.Sp, 11);
        _log.SetTextIsSelectable(true);
        _logScroll.AddView(_log);
        root.AddView(_logScroll, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));

        _input = new EditText(this) { Hint = "Raspuns pentru program (ex: N / Q / y)" };
        _input.SetSingleLine(true);
        var sendBtn = new Button(this) { Text = "Trimite" };
        sendBtn.Click += (_, _) =>
        {
            var line = _input.Text ?? "";
            AppendLog("> " + line + "\n");
            _reader?.Push(line);
            _input.Text = "";
        };
        root.AddView(Row(_input, 1f, sendBtn, 0f));

        SetContentView(root);
    }

    private TextView Label(string text) => new(this) { Text = text };

    private LinearLayout Row(params object[] viewsAndWeights)
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        for (var i = 0; i < viewsAndWeights.Length; i += 2)
        {
            var v = (View)viewsAndWeights[i];
            var w = (float)viewsAndWeights[i + 1];
            var lp = w > 0
                ? new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, w)
                : new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);
            row.AddView(v, lp);
        }
        return row;
    }

    private int Dp(int dp) => (int)(dp * (Resources?.DisplayMetrics?.Density ?? 1f));

    private void AppendLog(string text)
    {
        _ui.Post(() =>
        {
            _log.Append(text);
            if (_log.Length() > 300_000)
            {
                var s = _log.Text ?? "";
                _log.Text = s[^200_000..];
            }
            _logScroll.Post(() => _logScroll.FullScroll(FocusSearchDirection.Down));
        });
    }

    // ------------------------------------------------------------------ USB

    private void FindCable()
    {
        var devices = _usb.DeviceList?.Values ?? Enumerable.Empty<UsbDevice>();
        _device = devices.FirstOrDefault(d => d.VendorId == FtdiVendorId);
        if (_device == null)
        {
            var other = devices.FirstOrDefault();
            _status.Text = other == null
                ? "Niciun cablu USB. Conecteaza cablul KKL prin OTG."
                : $"Cablu {other.VendorId:X4}:{other.ProductId:X4} nesuportat (doar FTDI).";
            return;
        }

        if (_usb.HasPermission(_device))
        {
            _status.Text = $"Cablu FTDI {_device.ProductId:X4} pregatit";
            return;
        }

        _status.Text = "Astept permisiunea USB...";
        var intent = new Intent(ActionUsbPermission);
        intent.SetPackage(PackageName);
        var flags = Build.VERSION.SdkInt >= BuildVersionCodes.S ? PendingIntentFlags.Mutable : (PendingIntentFlags)0;
        var pi = PendingIntent.GetBroadcast(this, 0, intent, flags);
        _usb.RequestPermission(_device, pi);
    }

    private class UsbPermissionReceiver(MainActivity activity) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action == UsbManager.ActionUsbDeviceDetached)
            {
                activity._current?.Abort();
                activity._device = null;
                activity._status.Text = "Cablul a fost deconectat.";
                return;
            }
            var granted = intent?.GetBooleanExtra(UsbManager.ExtraPermissionGranted, false) ?? false;
            if (granted) activity.FindCable();
            else activity._status.Text = "Permisiunea USB a fost refuzata.";
        }
    }

    // ------------------------------------------------------------------ Rulare

    private void RunCommand()
    {
        if (_worker is { IsAlive: true }) return;
        if (_device == null || !_usb.HasPermission(_device))
        {
            FindCable();
            Toast.MakeText(this, "Conecteaza cablul FTDI si accepta permisiunea.", ToastLength.Short)!.Show();
            return;
        }

        var cmd = Commands[_command.SelectedItemPosition].Cmd;
        var extra = (_args.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var args = new List<string> { "USB", (_baud.Text ?? "10400").Trim(), (_address.Text ?? "17").Trim(), cmd };
        args.AddRange(extra);

        if (cmd.StartsWith("Load", StringComparison.OrdinalIgnoreCase) ||
            cmd.StartsWith("Write", StringComparison.OrdinalIgnoreCase) ||
            cmd == "SetSoftwareCoding" || cmd == "AdaptationSave")
        {
            new AlertDialog.Builder(this)
                .SetTitle("Atentie - scriere in modul")
                .SetMessage($"{cmd} SCRIE in calculator.\n\nAi facut backup (Dump)? Bateria e pe redresor? " +
                            "Nu deconecta cablul si nu bloca telefonul pana la final.")
                .SetPositiveButton("Continua", (_, _) => StartWorker(args.ToArray()))
                .SetNegativeButton("Renunta", (_, _) => { })
                .Show();
            return;
        }

        StartWorker(args.ToArray());
    }

    private void StartWorker(string[] args)
    {
        _reader?.Reset();
        AppendLog($"\n=== kw1281test {string.Join(' ', args)} ===\n");
        SetRunning(true);

        _worker = new Thread(() =>
        {
            try
            {
                Android.OS.Process.SetThreadPriority(Android.OS.ThreadPriority.UrgentAudio);
                Directory.SetCurrentDirectory(_workDir);
                BitFab.KW1281Test.Program.Main(args);
            }
            catch (Exception ex)
            {
                AppendLog($"\nEroare: {ex.Message}\n");
            }
            finally
            {
                _current = null;
                _writer?.Flush();
                AppendLog("=== Gata ===\n");
                _ui.Post(() => SetRunning(false));
            }
        }, 16 * 1024 * 1024) { IsBackground = true, Name = "kw1281" };
        _worker.Start();
    }

    private void SetRunning(bool running)
    {
        _run.Enabled = !running;
        _stop.Enabled = running;
    }

    // ------------------------------------------------------------------ Fisiere

    private void ImportFile()
    {
        var i = new Intent(Intent.ActionOpenDocument);
        i.AddCategory(Intent.CategoryOpenable);
        i.SetType("*/*");
        StartActivityForResult(i, ReqImport);
    }

    private void ExportFile()
    {
        var files = Directory.GetFiles(_workDir).Select(f => System.IO.Path.GetFileName(f)).OfType<string>()
            .OrderByDescending(f => File.GetLastWriteTime(System.IO.Path.Combine(_workDir, f))).ToArray();
        if (files.Length == 0)
        {
            Toast.MakeText(this, "Nu exista fisiere inca.", ToastLength.Short)!.Show();
            return;
        }
        new AlertDialog.Builder(this)
            .SetTitle("Alege fisierul de exportat")
            .SetItems(files, (_, e) =>
            {
                _pendingExport = files[e.Which];
                var i = new Intent(Intent.ActionCreateDocument);
                i.AddCategory(Intent.CategoryOpenable);
                i.SetType("application/octet-stream");
                i.PutExtra(Intent.ExtraTitle, _pendingExport);
                StartActivityForResult(i, ReqExport);
            })
            .Show();
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (resultCode != Result.Ok || data?.Data == null) return;
        var uri = data.Data;

        try
        {
            if (requestCode == ReqImport)
            {
                var name = QueryName(uri) ?? $"import_{DateTime.Now:yyyyMMdd_HHmmss}.bin";
                using (var input = ContentResolver!.OpenInputStream(uri)!)
                using (var output = File.Create(System.IO.Path.Combine(_workDir, name)))
                {
                    input.CopyTo(output);
                }
                _args.Text = ((_args.Text ?? "") + " " + name).Trim();
                AppendLog($"Importat: {name}\n");
            }
            else if (requestCode == ReqExport && _pendingExport != null)
            {
                using (var input = File.OpenRead(System.IO.Path.Combine(_workDir, _pendingExport)))
                using (var output = ContentResolver!.OpenOutputStream(uri)!)
                {
                    input.CopyTo(output);
                }
                AppendLog($"Exportat: {_pendingExport}\n");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Eroare fisier: {ex.Message}\n");
        }
    }

    private string? QueryName(Android.Net.Uri uri)
    {
        using var cursor = ContentResolver!.Query(uri, null, null, null, null);
        if (cursor == null || !cursor.MoveToFirst()) return null;
        var idx = cursor.GetColumnIndex("_display_name");
        return idx >= 0 ? cursor.GetString(idx) : null;
    }

    // ------------------------------------------------------------------ Consola

    /// <summary>Trimite tot ce scrie kw1281test in consola catre ecran.</summary>
    private sealed class UiWriter(Action<string> sink) : TextWriter
    {
        private readonly StringBuilder _sb = new();
        private long _lastFlush;

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            lock (_sb)
            {
                _sb.Append(value);
                if (value == '\n' || _sb.Length > 512 || Environment.TickCount64 - _lastFlush > 100)
                    FlushLocked();
            }
        }

        public override void Write(string? value)
        {
            if (value == null) return;
            lock (_sb)
            {
                _sb.Append(value);
                FlushLocked();
            }
        }

        public override void Flush()
        {
            lock (_sb) FlushLocked();
        }

        private void FlushLocked()
        {
            if (_sb.Length == 0) return;
            sink(_sb.ToString());
            _sb.Clear();
            _lastFlush = Environment.TickCount64;
        }
    }

    /// <summary>Console.In pentru comenzile care asteapta un raspuns text.</summary>
    private sealed class BlockingLineReader : TextReader
    {
        private BlockingCollection<string?> _lines = new();

        public void Push(string line) => _lines.Add(line);
        public void Cancel() => _lines.Add(null);
        public void Reset() => _lines = new BlockingCollection<string?>();

        public override string? ReadLine() => _lines.Take();

        public override int Read()
        {
            var line = ReadLine();
            return string.IsNullOrEmpty(line) ? -1 : line[0];
        }
    }
}
