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
using Android.Graphics.Drawables;
using Android.Hardware.Usb;
using Android.OS;
using Android.Text;
using Android.Text.Method;
using Android.Text.Style;
using Android.Views;
using Android.Widget;
using BitFab.KW1281Test;

namespace KW1281Android;

[Activity(Label = "STP Programming", MainLauncher = true, Exported = true,
    Theme = "@android:style/Theme.Material.NoActionBar",
    LaunchMode = LaunchMode.SingleTop, ScreenOrientation = ScreenOrientation.Portrait,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden)]
[IntentFilter(new[] { UsbManager.ActionUsbDeviceAttached })]
[MetaData(UsbManager.ActionUsbDeviceAttached, Resource = "@xml/device_filter")]
public class MainActivity : Activity
{
    private const string ActionUsbPermission = "ro.kw1281test.android.USB_PERMISSION";
    private const int ReqImport = 1001;
    private const int ReqExport = 1002;

    // ---------------------------------------------------------------- Tema
    private static readonly Color CBg = Color.ParseColor("#0A0C10");
    private static readonly Color CCard = Color.ParseColor("#141821");
    private static readonly Color CCard2 = Color.ParseColor("#1B2130");
    private static readonly Color CLine = Color.ParseColor("#262D3D");
    private static readonly Color CText = Color.ParseColor("#E8ECF3");
    private static readonly Color CMuted = Color.ParseColor("#8A93A6");
    private static readonly Color CAccent = Color.ParseColor("#FF6A00");
    private static readonly Color CAccent2 = Color.ParseColor("#FFB000");
    private static readonly Color CGreen = Color.ParseColor("#22E07A");
    private static readonly Color CRed = Color.ParseColor("#FF3B4E");
    private static readonly Color CCyan = Color.ParseColor("#2EC5FF");
    private static readonly Color CTerm = Color.ParseColor("#05070A");
    private static readonly Color CLedOff = Color.ParseColor("#2A303C");

    // ---------------------------------------------------------------- Date
    private static readonly (string Name, string Addr, string Icon)[] Modules =
    [
        ("Motor", "01", "⚙️"), ("Cutie viteze", "02", "🕹️"), ("ABS / ESP", "03", "🛞"),
        ("Clima", "08", "❄️"), ("Electr. centrală", "09", "💡"), ("Airbag", "15", "🛡️"),
        ("Volan", "16", "🎛️"), ("Bord", "17", "📟"), ("Imobilizator", "25", "🔑"),
        ("Confort", "46", "🚪"), ("Radio", "56", "📻"), ("Parcare", "76", "📡"),
    ];

    private static readonly (string Cmd, string Hint)[] AllCommands =
    [
        ("ReadIdent", "fără argumente"), ("ReadFaultCodes", "fără argumente"),
        ("ClearFaultCodes", "fără argumente"), ("ReadSoftwareVersion", "fără argumente"),
        ("GetSKC", "fără argumente (bord 17)"), ("GetClusterId", "fără argumente"),
        ("AutoScan", "fără argumente"), ("GroupRead", "GROUP [fastinit]"), ("BasicSetting", "GROUP"),
        ("ActuatorTest", "N = următorul, Q = stop"), ("AdaptationRead", "CHANNEL [LOGIN]"),
        ("AdaptationTest", "CHANNEL VALUE [LOGIN]"), ("AdaptationSave", "CHANNEL VALUE [LOGIN]"),
        ("SetSoftwareCoding", "CODING WORKSHOP"), ("FindLogins", "LOGIN"),
        ("DumpEeprom", "START LENGTH [FILE]  sau  FILE (bord/airbag)"), ("ReadEeprom", "ADDRESS"),
        ("WriteEeprom", "ADDRESS VALUE"), ("LoadEeprom", "START FILE"), ("MapEeprom", "[FILE]"),
        ("DumpMarelliMem", "START LENGTH [FILE]"), ("DumpRBxMem", "START LENGTH [FILE]"),
        ("DumpRBxMemOdd", "START LENGTH [FILE]"), ("DumpMem", "START LENGTH [FILE]"),
        ("DumpRam", "START LENGTH [FILE]"), ("DumpRom", "START LENGTH [FILE]"),
        ("ReadRAM", "ADDRESS"), ("ReadROM", "ADDRESS"), ("WriteRAM", "ADDRESS VALUE"),
        ("DumpCcmRom", "[FILE]"), ("DumpClusterNecRom", "[FILE]"), ("ToggleRB4Mode", "fără argumente"),
        ("Reset", "fără argumente"), ("ClarionVWPremium4SafeCode", "adresa 56"),
        ("DelcoVWPremium5SafeCode", "adresa 56"), ("DumpEdc15Eeprom", "[FILE]"),
        ("WriteEdc15Eeprom", "ADDR1 VAL1 [ADDR2 VAL2 ...]"), ("LoadEdc15Eeprom", "[START] FILE"),
        ("DumpEdc15Flash", "[Low|Medium|High] [FILE]"), ("LoadEdc15Flash", "[Low|Medium|High] [full] [noverify] FILE"),
        ("DumpEdc15FlashBoot", "[FILE] (boot mode)"), ("LoadEdc15FlashBoot", "FILE (boot mode)"),
        ("DumpEdc16Flash", "[Low|Medium|High] [FILE]"),
        ("LoadEdc16Flash", "[Low|Medium|High] [full] [unverified] [fastinit] FILE"),
    ];

    private static readonly string[] InterfaceProfiles =
    [
        "Auto-detect (FTDI)",
        "KKL VAG-COM 409.1 (FTDI)",
        "Clonă VCDS în mod KKL (FTDI)",
        "Personalizat (DTR / RTS manual)",
    ];

    // ---------------------------------------------------------------- Stare
    private UsbManager _usb = null!;
    private UsbDevice? _device;
    private string _workDir = "";
    private string? _pendingExport;
    private string _baud = "9600";
    private string _address = "01";
    private bool _connected;
    private bool _sawEcu, _sawError;
    private Action? _onFinish;
    private int _group = 1;
    private readonly List<string> _faults = new();
    private Spinner _mapEcu = null!;
    private EditText _mapFile = null!;
    private EditText _eeFile = null!, _eeStart = null!;
    private bool _dashActive;
    private TextView _fileList = null!, _verText2 = null!;
    private StreamWriter? _csv;
    private string? _csvName;
    private GraphView _graph = null!;
    private Button _recBtn = null!;
    private Switch _hex = null!;
    private static readonly System.Text.RegularExpressions.Regex NumRx = new(@"-?\d+(?:[.,]\d+)?");
    private static readonly System.Text.RegularExpressions.Regex FaultRx =
        new(@"^\s*(\d{5}) - (\d{2})-(\d{2})\s*$");

    private Thread? _worker;
    private AndroidFtdiInterface? _current;
    private readonly Handler _ui = new(Looper.MainLooper!);
    private readonly ConcurrentQueue<(string Text, Color Color)> _pending = new();
    private volatile string? _overlay;
    private ConsoleRouter? _router;
    private UsbPermissionReceiver? _permReceiver;
    private Updater.InstallReceiver? _installReceiver;
    private TextView _verText = null!;
    private bool _updating;
    private readonly SpannableStringBuilder _ssb = new();

    // ---------------------------------------------------------------- Vederi
    private View _cableDot = null!, _ledTx = null!, _ledRx = null!, _ecuDot = null!;
    private TextView _cableTitle = null!, _cableSub = null!, _rate = null!, _counters = null!;
    private TextView _ecuTitle = null!, _ecuInfo = null!, _log = null!, _hint = null!, _liveGroup = null!, _liveRaw = null!;
    private TextView _busy = null!;
    private Spinner _profile = null!, _command = null!;
    private Switch _dtr = null!, _rts = null!, _fastInit = null!;
    private EditText _customBaud = null!, _customAddr = null!, _args = null!, _input = null!;
    private readonly List<TextView> _baudChips = new(), _latChips = new(), _moduleChips = new();
    private readonly TextView[] _liveCells = new TextView[4];
    private readonly TextView[] _liveLabels = new TextView[4];
    private LinearLayout _liveCard = null!;
    private Button _connectBtn = null!;
    private readonly List<View> _actionTiles = new();

    private long _lastTx, _lastRx, _ledTxUntil, _ledRxUntil, _rateTick, _rateBase;

    // ================================================================ Ciclu de viata

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        Window?.SetStatusBarColor(CBg);
        Window?.SetNavigationBarColor(CBg);

        _usb = (UsbManager)GetSystemService(UsbService)!;
        _workDir = GetExternalFilesDir(null)?.AbsolutePath ?? FilesDir!.AbsolutePath;
        Directory.CreateDirectory(_workDir);

        BuildUi();

        _router = new ConsoleRouter(this);
        Console.SetOut(_router);
        Console.SetError(_router);

        BitFab.KW1281Test.Program.InterfaceFactory = (_, baud) =>
        {
            var dev = _device ?? throw new InvalidOperationException("Niciun cablu FTDI selectat.");
            var conn = _usb.OpenDevice(dev)
                       ?? throw new InvalidOperationException("Nu pot deschide cablul (lipsă permisiune?).");
            var iface = new AndroidFtdiInterface(conn, dev, baud);
            _current = iface;
            return iface;
        };

        _permReceiver = new UsbPermissionReceiver(this);
        var filter = new IntentFilter(ActionUsbPermission);
        filter.AddAction(UsbManager.ActionUsbDeviceDetached);
        filter.AddAction(UsbManager.ActionUsbDeviceAttached);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            RegisterReceiver(_permReceiver, filter, ReceiverFlags.NotExported);
        else
            RegisterReceiver(_permReceiver, filter);

        _installReceiver = new Updater.InstallReceiver(msg => _ui.Post(() =>
        {
            Emit(msg, msg.Contains('✓') ? CGreen : CRed);
            Toast.MakeText(this, msg, ToastLength.Long)!.Show();
            _updating = false;
        }));
        var instFilter = new IntentFilter(Updater.ActionInstallStatus);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
            RegisterReceiver(_installReceiver, instFilter, ReceiverFlags.NotExported);
        else
            RegisterReceiver(_installReceiver, instFilter);
        _ui.PostDelayed(() => CheckForUpdate(manual: false), 2500);

        Emit("STREET TUNING PARTS • PROGRAMMING", CAccent);
        Emit("Motor: kw1281test v1.0.4 (MIT, Greg Menounos)", CMuted);
        Emit($"Fișiere salvate în: {_workDir}", CMuted);
        Emit($"Bază coduri eroare: {DtcDatabase.Count} coduri", CMuted);
        Emit("", CMuted);

        FindCable();
        _ui.Post(Tick);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        FindCable();
    }

    protected override void OnDestroy()
    {
        if (_permReceiver != null) UnregisterReceiver(_permReceiver);
        if (_installReceiver != null) UnregisterReceiver(_installReceiver);
        try { _csv?.Dispose(); } catch { }
        base.OnDestroy();
    }

    // ================================================================ Construire UI
    //
    // Trei ecrane: START (siglă + interfață + Conectează) -> MENIU (funcții) ->
    // FUNCȚIE (ecran plin pe o singură funcție, cu baud/modul reglabile + consolă jos).

    private FrameLayout _root = null!;
    private View _screenStart = null!, _screenMenu = null!;
    private FrameLayout _funcHost = null!;
    private TextView _funcTitle = null!;
    private enum Screen { Start, Menu, Func }
    private Screen _screen = Screen.Start;

    // Meniul principal: fiecare intrare construiește conținutul ecranului ei de funcție.
    private (string Icon, string Title, string Sub, Func<View> Build)[] _menu = Array.Empty<(string, string, string, Func<View>)>();

    private void BuildUi()
    {
        _menu = new (string, string, string, Func<View>)[]
        {
            ("💾", "EEPROM", "citire și scriere EEPROM", BuildEepromScreen),
            ("🧠", "FLASH", "citire și scriere flash ECU", BuildFlashScreen),
            ("🏁", "Dashboard", "turometru + gauge-uri live", BuildDashboardScreen),
            ("🩺", "Diagnoză", "identificare, erori, adaptări", BuildDiagScreen),
            ("📈", "Live Data", "valori + grafic + CSV", BuildLiveCard),
            ("📁", "Fișiere", "dump-uri, import / export", BuildFilesCard),
            ("🛠️", "Mod avansat", "toate comenzile kw1281test", BuildAdvancedCard),
            ("⚙️", "Setări", "baud, actualizări, HEX", BuildSettingsCard),
        };

        _root = new FrameLayout(this);
        _root.SetBackgroundColor(CBg);
        _root.SetFitsSystemWindows(true);

        _screenStart = BuildStartScreen();
        _screenMenu = BuildMenuScreen();
        _funcHost = BuildFuncShell();

        _root.AddView(_screenStart);
        _root.AddView(_screenMenu);
        _root.AddView(_funcHost);

        // Consola ca strat peste tot, deschisă din butonul plutitor de oriunde
        _root.AddView(BuildConsoleOverlay());
        _root.AddView(BuildConsoleFab());

        SetContentView(_root);
        GoStart();
        SelectBaud("9600");
        SelectLatency(1);
        SelectModule("01");
    }

    // ---- Navigare ----

    private void GoStart() { _screen = Screen.Start; Sync(); }
    private void GoMenu() { _screen = Screen.Menu; Sync(); }

    private void OpenFunc(string title, View content)
    {
        _funcTitle.Text = title;
        _funcContent.RemoveAllViews();
        _funcContent.AddView(content);
        _screen = Screen.Func;
        Sync();
    }

    private void Sync()
    {
        _screenStart.Visibility = _screen == Screen.Start ? ViewStates.Visible : ViewStates.Gone;
        _screenMenu.Visibility = _screen == Screen.Menu ? ViewStates.Visible : ViewStates.Gone;
        _funcHost.Visibility = _screen == Screen.Func ? ViewStates.Visible : ViewStates.Gone;
    }

    public override void OnBackPressed()
    {
        if (_dashFullOverlay != null)
        {
            CloseDashFullscreen();
            return;
        }
        if (_consoleOverlay != null && _consoleOverlay.Visibility == ViewStates.Visible)
        {
            ToggleConsole();
            return;
        }
        switch (_screen)
        {
            case Screen.Func: GoMenu(); break;
            case Screen.Menu: GoStart(); break;
            default: base.OnBackPressed(); break;
        }
    }

    // ---- Ecran START: siglă + nume + interfață + Conectează ----

    private View BuildStartScreen()
    {
        var scroll = new ScrollView(this) { FillViewport = true };
        var col = new LinearLayout(this) { Orientation = Orientation.Vertical };
        col.SetPadding(Dp(16), Dp(16), Dp(16), Dp(24));
        col.SetGravity(GravityFlags.CenterHorizontal);

        col.AddView(BuildHeader());

        var spacer = new View(this);
        col.AddView(spacer, new LinearLayout.LayoutParams(-1, Dp(10)));

        col.AddView(BuildInterfaceCard());
        col.AddView(BuildCommCard());
        col.AddView(BuildEcuCard());

        var enter = BigButton("☰  MENIU FUNCȚII", CCard2);
        enter.Click += (_, _) => GoMenu();
        col.AddView(enter, MatchWrap(top: 10));

        var foot = Txt("STP Programming • bazat pe kw1281test (MIT)", 11, CMuted);
        foot.Gravity = GravityFlags.Center;
        foot.SetPadding(0, Dp(18), 0, 0);
        col.AddView(foot);

        scroll.AddView(col);
        return scroll;
    }

    // ---- Ecran MENIU ----

    private View BuildMenuScreen()
    {
        var outer = new LinearLayout(this) { Orientation = Orientation.Vertical };

        outer.AddView(BuildTopBar("Meniu principal", GoStart));

        var scroll = new ScrollView(this) { FillViewport = true };
        var grid = new LinearLayout(this) { Orientation = Orientation.Vertical };
        grid.SetPadding(Dp(14), Dp(12), Dp(14), Dp(20));

        for (var i = 0; i < _menu.Length; i += 2)
        {
            var row = HRow();
            for (var j = i; j < Math.Min(i + 2, _menu.Length); j++)
            {
                var m = _menu[j];
                var tile = new LinearLayout(this) { Orientation = Orientation.Vertical, Clickable = true };
                tile.Background = Rounded(CCard, 18, CLine);
                tile.SetPadding(Dp(16), Dp(18), Dp(16), Dp(18));
                tile.AddView(Txt(m.Icon, 28, CText));
                var tt = Txt(m.Title, 16, CText, bold: true);
                tt.SetPadding(0, Dp(6), 0, 0);
                tile.AddView(tt);
                tile.AddView(Txt(m.Sub, 11, CMuted));
                tile.Click += (_, _) => OpenFunc(m.Title, m.Build());
                var lp = new LinearLayout.LayoutParams(0, -2, 1f);
                lp.SetMargins(Dp(5), Dp(5), Dp(5), Dp(5));
                row.AddView(tile, lp);
            }
            grid.AddView(row);
        }
        scroll.AddView(grid);
        outer.AddView(scroll, new LinearLayout.LayoutParams(-1, 0, 1f));
        return outer;
    }

    // ---- Shell FUNCȚIE: bară sus (← + titlu), bară baud/modul, conținut, consolă jos ----

    private LinearLayout _funcContent = null!;

    private FrameLayout BuildFuncShell()
    {
        var host = new FrameLayout(this);
        var outer = new LinearLayout(this) { Orientation = Orientation.Vertical };

        var bar = BuildTopBarRef(out _funcTitle, GoMenu);
        outer.AddView(bar);

        // Bară compactă baud + modul, reglabilă în orice funcție
        outer.AddView(BuildQuickBar());

        var scroll = new ScrollView(this) { FillViewport = true };
        _funcContent = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _funcContent.SetPadding(Dp(14), Dp(10), Dp(14), Dp(12));
        scroll.AddView(_funcContent);
        outer.AddView(scroll, new LinearLayout.LayoutParams(-1, 0, 1f));

        host.AddView(outer);
        return host;
    }

    // ---- Consolă globală: overlay peste orice ecran + buton plutitor ----
    private FrameLayout _consoleOverlay = null!;

    private View BuildConsoleOverlay()
    {
        _consoleOverlay = new FrameLayout(this);
        _consoleOverlay.SetBackgroundColor(Color.Argb(210, 0, 0, 0));
        _consoleOverlay.Visibility = ViewStates.Gone;
        _consoleOverlay.Clickable = true;

        var panel = new LinearLayout(this) { Orientation = Orientation.Vertical };
        panel.SetPadding(Dp(14), Dp(14), Dp(14), Dp(14));

        var head = HRow();
        head.AddView(Txt("🖥️  Consolă", 16, CText, bold: true), new LinearLayout.LayoutParams(0, -2, 1f));
        var close = SmallButton("✕ Închide");
        close.Click += (_, _) => ToggleConsole();
        head.AddView(close);
        panel.AddView(head, MatchWrap(top: 0));

        panel.AddView(BuildConsoleCard(), MatchWrap(top: 10));

        var lp = new FrameLayout.LayoutParams(-1, -2) { Gravity = GravityFlags.Bottom };
        _consoleOverlay.AddView(panel, lp);
        return _consoleOverlay;
    }

    private View BuildConsoleFab()
    {
        var fab = Txt("🖥️", 22, Color.White);
        fab.Gravity = GravityFlags.Center;
        fab.Background = Oval(CAccent);
        fab.Clickable = true;
        fab.Click += (_, _) => ToggleConsole();
        var lp = new FrameLayout.LayoutParams(Dp(54), Dp(54)) { Gravity = GravityFlags.Bottom | GravityFlags.End };
        lp.SetMargins(0, 0, Dp(16), Dp(20));
        fab.LayoutParameters = lp;
        return fab;
    }

    private void ToggleConsole()
    {
        if (_consoleOverlay == null) return;
        _consoleOverlay.Visibility = _consoleOverlay.Visibility == ViewStates.Visible
            ? ViewStates.Gone : ViewStates.Visible;
    }

    private View BuildTopBar(string title, Action back)
    {
        return BuildTopBarRef(out var _, back, title);
    }

    private View BuildTopBarRef(out TextView titleView, Action back, string title = "", bool showConsoleBtn = false)
    {
        var bar = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        bar.SetGravity(GravityFlags.CenterVertical);
        bar.SetBackgroundColor(CCard);
        bar.SetPadding(Dp(10), Dp(10), Dp(14), Dp(10));

        var backBtn = Txt("←", 22, CText, bold: true);
        backBtn.SetPadding(Dp(12), Dp(4), Dp(14), Dp(4));
        backBtn.Clickable = true;
        backBtn.Background = Rounded(CCard2, 12, CLine);
        backBtn.Click += (_, _) => back();
        bar.AddView(backBtn);

        titleView = Txt("  " + title, 17, CText, bold: true);
        bar.AddView(titleView, new LinearLayout.LayoutParams(0, -2, 1f));

        // mic indicator cablu
        _barCable = Dot(CRed, 10);
        bar.AddView(_barCable);
        bar.AddView(Txt("  ", 10, CMuted));

        // buton consolă (doar în ecranele de funcție)
        if (showConsoleBtn)
        {
            var conBtn = Txt("🖥️", 18, CText);
            conBtn.SetPadding(Dp(10), Dp(4), Dp(10), Dp(4));
            conBtn.Clickable = true;
            conBtn.Background = Rounded(CCard2, 12, CLine);
            conBtn.Click += (_, _) => ToggleConsole();
            bar.AddView(conBtn);
        }
        return bar;
    }

    private View _barCable = null!;

    // Bară rapidă: baud + modul, setabile în orice funcție
    private EditText _quickBaud = null!, _quickAddr = null!;

    private View BuildQuickBar()
    {
        var bar = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        bar.SetGravity(GravityFlags.CenterVertical);
        bar.SetBackgroundColor(CBg);
        bar.SetPadding(Dp(14), Dp(8), Dp(14), Dp(8));

        bar.AddView(Txt("Baud ", 12, CMuted, bold: true));
        _quickBaud = Field("10400", numeric: true);
        _quickBaud.Text = _baud;
        _quickBaud.TextChanged += (_, _) => { var t = _quickBaud.Text?.Trim() ?? ""; if (t.Length > 0) _baud = t; };
        bar.AddView(_quickBaud, new LinearLayout.LayoutParams(0, -2, 1.2f));

        bar.AddView(Txt("  Modul ", 12, CMuted, bold: true));
        _quickAddr = Field("17");
        _quickAddr.Text = _address;
        _quickAddr.TextChanged += (_, _) => { var t = _quickAddr.Text?.Trim() ?? ""; if (t.Length > 0) _address = t; };
        bar.AddView(_quickAddr, new LinearLayout.LayoutParams(0, -2, 1f));

        var con = SmallButton("⚡");
        con.Click += (_, _) => Connect();
        bar.AddView(con);
        return bar;
    }

    // ---- Ecran Diagnoză (grupează acțiunile rapide de diag) ----
    private View BuildDiagScreen() => BuildActionsCard();

    private View BuildHeader()
    {
        var box = new LinearLayout(this) { Orientation = Orientation.Vertical };
        box.SetPadding(Dp(18), Dp(18), Dp(18), Dp(16));
        var g = new GradientDrawable(GradientDrawable.Orientation.TlBr!,
            new int[] { Color.ParseColor("#1C1208").ToArgb(), Color.ParseColor("#121621").ToArgb(), Color.ParseColor("#0D1018").ToArgb() });
        g.SetCornerRadius(Dp(20));
        g.SetStroke(Dp(1), Color.ParseColor("#3A2410"));
        box.Background = g;

        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);

        var logo = Txt("STP", 18, Color.White, bold: true);
        logo.Gravity = GravityFlags.Center;
        logo.Background = Rounded(CAccent, 14);
        row.AddView(logo, new LinearLayout.LayoutParams(Dp(54), Dp(54)));

        var titles = new LinearLayout(this) { Orientation = Orientation.Vertical };
        titles.SetPadding(Dp(14), 0, 0, 0);
        var brand = new SpannableString("STREET TUNING PARTS");
        brand.SetSpan(new ForegroundColorSpan(CText), 0, 14, SpanTypes.ExclusiveExclusive);
        brand.SetSpan(new ForegroundColorSpan(CAccent), 14, brand.Length(), SpanTypes.ExclusiveExclusive);
        var t1 = Txt("", 19, CText, bold: true);
        t1.TextFormatted = brand;
        t1.LetterSpacing = 0.04f;
        titles.AddView(t1);
        var t2 = Txt("P R O G R A M M I N G", 11, CAccent2, bold: true);
        t2.LetterSpacing = 0.12f;
        titles.AddView(t2);
        row.AddView(titles, new LinearLayout.LayoutParams(0, -2, 1f));
        box.AddView(row);

        var sub = Txt("Diagnoză K-Line VAG  •  KW1281 / KWP2000  •  EEPROM & Flash", 12, CMuted);
        sub.SetPadding(0, Dp(12), 0, 0);
        box.AddView(sub);

        var vr = HRow();
        _verText = Txt($"v{Updater.CurrentName(this)}  (build {Updater.CurrentVersion(this)})", 12, CAccent2, bold: true, mono: true);
        vr.AddView(_verText, new LinearLayout.LayoutParams(0, -2, 1f));
        var upd = SmallButton("🔄 Actualizări");
        upd.Click += (_, _) => CheckForUpdate(manual: true);
        vr.AddView(upd);
        box.AddView(vr, MatchWrap(top: 10));

        var lp = new LinearLayout.LayoutParams(-1, -2);
        lp.SetMargins(0, 0, 0, Dp(12));
        box.LayoutParameters = lp;
        return box;
    }

    private View BuildInterfaceCard()
    {
        var card = Card("INTERFAȚĂ", "🔌");

        var row = HRow();
        _cableDot = Dot(CRed, 12);
        row.AddView(_cableDot);
        var col = new LinearLayout(this) { Orientation = Orientation.Vertical };
        col.SetPadding(Dp(12), 0, 0, 0);
        _cableTitle = Txt("Caut cablul…", 16, CText, bold: true);
        _cableSub = Txt("Conectează cablul KKL prin OTG", 12, CMuted);
        col.AddView(_cableTitle);
        col.AddView(_cableSub);
        row.AddView(col, new LinearLayout.LayoutParams(0, -2, 1f));
        var scan = SmallButton("Scanează");
        scan.Click += (_, _) => FindCable();
        row.AddView(scan);
        card.AddView(row);

        card.AddView(Label("Tip interfață"));
        _profile = new Spinner(this);
        _profile.Adapter = DarkAdapter(InterfaceProfiles);
        _profile.Background = Rounded(CCard2, 12, CLine);
        _profile.SetPadding(Dp(8), Dp(6), Dp(8), Dp(6));
        _profile.ItemSelected += (_, e) => ApplyProfile(e.Position);
        card.AddView(_profile, MatchWrap(top: 4));

        var sw = HRow();
        _dtr = MakeSwitch("DTR activ", true);
        _rts = MakeSwitch("RTS forțat", false);
        sw.AddView(_dtr, new LinearLayout.LayoutParams(0, -2, 1f));
        sw.AddView(_rts, new LinearLayout.LayoutParams(0, -2, 1f));
        _dtr.CheckedChange += (_, e) => AndroidFtdiInterface.DtrEnabled = e.IsChecked;
        _rts.CheckedChange += (_, e) => AndroidFtdiInterface.ForceRts = e.IsChecked;
        card.AddView(sw, MatchWrap(top: 8));

        // Becuri TX / RX
        var leds = HRow();
        leds.Background = Rounded(CTerm, 12, CLine);
        leds.SetPadding(Dp(14), Dp(12), Dp(14), Dp(12));
        _ledTx = Dot(CLedOff, 14);
        _ledRx = Dot(CLedOff, 14);
        leds.AddView(_ledTx);
        leds.AddView(Txt("  TX", 13, CText, bold: true, mono: true));
        var sp = new View(this);
        leds.AddView(sp, new LinearLayout.LayoutParams(Dp(22), 1));
        leds.AddView(_ledRx);
        leds.AddView(Txt("  RX", 13, CText, bold: true, mono: true));
        var right = new LinearLayout(this) { Orientation = Orientation.Vertical };
        right.SetGravity(GravityFlags.Right);
        _rate = Txt("0 B/s", 14, CAccent2, bold: true, mono: true);
        _rate.Gravity = GravityFlags.Right;
        _counters = Txt("TX 0  •  RX 0", 11, CMuted, mono: true);
        _counters.Gravity = GravityFlags.Right;
        right.AddView(_rate);
        right.AddView(_counters);
        leds.AddView(right, new LinearLayout.LayoutParams(0, -2, 1f));
        card.AddView(leds, MatchWrap(top: 12));
        return _lastWrap;
    }

    private View BuildCommCard()
    {
        var card = Card("SETĂRI COMUNICARE", "📶");

        card.AddView(Label("Baud rate"));
        var bauds = ChipRow(new[] { "9600", "10400", "4800", "1200", "28800" }, _baudChips, SelectBaud);
        card.AddView(bauds);
        _customBaud = Field("Altă valoare (ex: 10400)", numeric: true);
        _customBaud.TextChanged += (_, _) =>
        {
            var t = _customBaud.Text?.Trim() ?? "";
            if (t.Length > 0) { _baud = t; foreach (var c in _baudChips) StyleChip(c, false); }
        };
        card.AddView(_customBaud, MatchWrap(top: 8));

        card.AddView(Label("Latență USB (FTDI)"));
        card.AddView(ChipRow(new[] { "1 ms", "4 ms", "16 ms" }, _latChips,
            s => SelectLatency(int.Parse(s.Split(' ')[0]))));

        _fastInit = MakeSwitch("Fast init (ISO 14230) pentru Live Data", false);
        card.AddView(_fastInit, MatchWrap(top: 10));

        var note = Txt("10400 = majoritatea modulelor VAG • 9600 = module vechi • 28800 = EDC15 boot mode", 11, CMuted);
        note.SetPadding(0, Dp(8), 0, 0);
        card.AddView(note);
        return _lastWrap;
    }

    private View BuildEcuCard()
    {
        var card = Card("MODUL / ECU", "🚗");

        for (var i = 0; i < Modules.Length; i += 3)
        {
            var r = HRow();
            for (var j = i; j < Math.Min(i + 3, Modules.Length); j++)
            {
                var m = Modules[j];
                var chip = Txt($"{m.Icon}\n{m.Name}\n{m.Addr}", 12, CText, bold: true);
                chip.Gravity = GravityFlags.Center;
                chip.SetPadding(Dp(4), Dp(10), Dp(4), Dp(10));
                chip.Tag = m.Addr;
                chip.Clickable = true;
                chip.Click += (_, _) => SelectModule(m.Addr);
                _moduleChips.Add(chip);
                var lp = new LinearLayout.LayoutParams(0, -2, 1f);
                lp.SetMargins(Dp(3), Dp(3), Dp(3), Dp(3));
                r.AddView(chip, lp);
            }
            card.AddView(r);
        }

        _customAddr = Field("Altă adresă (hex, ex: 37)");
        _customAddr.TextChanged += (_, _) =>
        {
            var t = _customAddr.Text?.Trim() ?? "";
            if (t.Length > 0) { _address = t; foreach (var c in _moduleChips) StyleModule(c, false); SetDisconnected(); }
        };
        card.AddView(_customAddr, MatchWrap(top: 8));

        _connectBtn = BigButton("⚡  CONECTEAZĂ");
        _connectBtn.Click += (_, _) => Connect();
        card.AddView(_connectBtn, MatchWrap(top: 12));

        var st = new LinearLayout(this) { Orientation = Orientation.Vertical };
        st.Background = Rounded(CCard2, 14, CLine);
        st.SetPadding(Dp(14), Dp(12), Dp(14), Dp(12));
        var sr = HRow();
        _ecuDot = Dot(CLedOff, 12);
        sr.AddView(_ecuDot);
        _ecuTitle = Txt("  Neconectat", 15, CMuted, bold: true);
        sr.AddView(_ecuTitle);
        st.AddView(sr);
        _ecuInfo = Txt("Alege modulul și apasă Conectează.", 12, CMuted, mono: true);
        _ecuInfo.SetPadding(0, Dp(6), 0, 0);
        st.AddView(_ecuInfo);
        card.AddView(st, MatchWrap(top: 12));

        _busy = Txt("", 12, CAccent2, bold: true);
        _busy.SetPadding(0, Dp(6), 0, 0);
        card.AddView(_busy);
        return _lastWrap;
    }

    private View BuildLiveCard()
    {
        var card = Card("LIVE DATA", "📈");
        _liveCard = _lastWrap;

        var gr = HRow();
        gr.SetGravity(GravityFlags.CenterVertical);
        var minus = SmallButton("◀");
        minus.Click += (_, _) => { AppConsole.PushKey(ConsoleKey.DownArrow); if (_group > 1) _group--; UpdateGroupLabel(); };
        var plus = SmallButton("▶");
        plus.Click += (_, _) => { AppConsole.PushKey(ConsoleKey.UpArrow); if (_group < 255) _group++; UpdateGroupLabel(); };
        _liveGroup = Txt("Grup 001", 20, CText, bold: true, mono: true);
        _liveGroup.Gravity = GravityFlags.Center;
        gr.AddView(minus);
        gr.AddView(_liveGroup, new LinearLayout.LayoutParams(0, -2, 1f));
        gr.AddView(plus);
        card.AddView(gr);

        for (var r = 0; r < 2; r++)
        {
            var row = HRow();
            for (var c = 0; c < 2; c++)
            {
                var i = r * 2 + c;
                var cell = new LinearLayout(this) { Orientation = Orientation.Vertical };
                cell.Background = Rounded(CTerm, 14, CLine);
                cell.SetPadding(Dp(12), Dp(12), Dp(12), Dp(12));
                _liveLabels[i] = Txt($"Câmp {i + 1}", 11, CMuted, bold: true);
                _liveCells[i] = Txt("—", 20, GraphView.SeriesColors[i], bold: true, mono: true);
                cell.AddView(_liveLabels[i]);
                cell.AddView(_liveCells[i]);
                var lp = new LinearLayout.LayoutParams(0, -2, 1f);
                lp.SetMargins(Dp(4), Dp(8), Dp(4), 0);
                row.AddView(cell, lp);
            }
            card.AddView(row);
        }

        _graph = new GraphView(this);
        _graph.Background = Rounded(CTerm, 14, CLine);
        card.AddView(_graph, new LinearLayout.LayoutParams(-1, Dp(170)) { TopMargin = Dp(10) });

        _recBtn = SmallButton("⏺  Înregistrează CSV");
        _recBtn.Click += (_, _) => ToggleRecording();
        card.AddView(_recBtn, MatchWrap(top: 8));

        _liveRaw = Txt("", 11, CMuted, mono: true);
        _liveRaw.SetPadding(0, Dp(8), 0, 0);
        card.AddView(_liveRaw);

        var btns = HRow();
        var start = BigButton("▶  PORNEȘTE");
        start.Click += (_, _) => { _dashActive = false; StartLive(); };
        var stop = BigButton("■  STOP", CRed);
        stop.Click += (_, _) => StopAll();
        btns.AddView(start, new LinearLayout.LayoutParams(0, -2, 1f) { RightMargin = Dp(6) });
        btns.AddView(stop, new LinearLayout.LayoutParams(0, -2, 1f) { LeftMargin = Dp(6) });
        card.AddView(btns, MatchWrap(top: 12));
        return _liveCard;
    }

    private View BuildActionsCard()
    {
        var card = Card("ACȚIUNI", "🧰");
        (string Icon, string Title, string Sub, Action Run)[] tiles =
        [
            ("🔎", "Identificare", "ReadIdent", () => Execute("ReadIdent")),
            ("⚠️", "Coduri eroare", "cu descriere RO", ReadFaults),
            ("🧹", "Șterge erori", "ClearFaultCodes", () => Confirm("Ștergi codurile de eroare din modulul " + _address + "?", () => Execute("ClearFaultCodes"))),
            ("🛰️", "Auto Scan", "toate modulele", () => Execute("AutoScan")),
            ("🧠", "Versiune soft", "ReadSoftwareVersion", () => Execute("ReadSoftwareVersion")),
            ("📈", "Live Data", "GroupRead", StartLive),
        ];
        for (var i = 0; i < tiles.Length; i += 2)
        {
            var row = HRow();
            for (var j = i; j < Math.Min(i + 2, tiles.Length); j++)
            {
                var t = tiles[j];
                var tile = new LinearLayout(this) { Orientation = Orientation.Vertical, Clickable = true };
                tile.Background = Rounded(CCard2, 16, CLine);
                tile.SetPadding(Dp(14), Dp(14), Dp(14), Dp(14));
                tile.AddView(Txt(t.Icon, 22, CText));
                tile.AddView(Txt(t.Title, 15, CText, bold: true));
                tile.AddView(Txt(t.Sub, 11, CMuted));
                tile.Click += (_, _) => t.Run();
                _actionTiles.Add(tile);
                var lp = new LinearLayout.LayoutParams(0, -2, 1f);
                lp.SetMargins(Dp(4), Dp(4), Dp(4), Dp(4));
                row.AddView(tile, lp);
            }
            card.AddView(row);
        }
        return _lastWrap;
    }

    private View BuildAdvancedCard()
    {
        var card = Card("MOD AVANSAT • TOATE COMENZILE", "🛠️");
        _command = new Spinner(this);
        _command.Adapter = DarkAdapter(AllCommands.Select(c => c.Cmd).ToArray());
        _command.Background = Rounded(CCard2, 12, CLine);
        _command.SetPadding(Dp(8), Dp(6), Dp(8), Dp(6));
        _command.ItemSelected += (_, e) => _hint.Text = "Argumente: " + AllCommands[e.Position].Hint;
        card.AddView(_command, MatchWrap());
        _hint = Txt("", 12, CMuted);
        _hint.SetPadding(0, Dp(6), 0, 0);
        card.AddView(_hint);
        _args = Field("Argumente (ex: 0 2048 bord.bin)");
        card.AddView(_args, MatchWrap(top: 6));

        var row = HRow();
        var run = BigButton("▶  RULEAZĂ");
        run.Click += (_, _) =>
        {
            var cmd = AllCommands[_command.SelectedItemPosition].Cmd;
            var extra = (_args.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (IsWrite(cmd))
                Confirm($"{cmd} SCRIE în modulul {_address}.\n\nAi backup? Bateria e pe redresor? Nu deconecta cablul până la final.",
                    () => Execute(cmd, extra));
            else
                Execute(cmd, extra);
        };
        row.AddView(run, new LinearLayout.LayoutParams(0, -2, 1f) { RightMargin = Dp(6) });
        var imp = SmallButton("📥 Importă");
        imp.Click += (_, _) => ImportFile();
        var exp = SmallButton("📤 Exportă");
        exp.Click += (_, _) => ExportFile();
        row.AddView(imp);
        row.AddView(exp);
        card.AddView(row, MatchWrap(top: 10));
        return _lastWrap;
    }

    // Un rand de 1-2 "dale" mari de actiune (icon + titlu + subtitlu)
    private void AddTiles(LinearLayout card, (string Icon, string Title, string Sub, Action Run)[] tiles)
    {
        for (var i = 0; i < tiles.Length; i += 2)
        {
            var row = HRow();
            for (var j = i; j < Math.Min(i + 2, tiles.Length); j++)
            {
                var t = tiles[j];
                var tile = new LinearLayout(this) { Orientation = Orientation.Vertical, Clickable = true };
                tile.Background = Rounded(CCard2, 16, CLine);
                tile.SetPadding(Dp(14), Dp(14), Dp(14), Dp(14));
                tile.AddView(Txt(t.Icon, 22, CText));
                tile.AddView(Txt(t.Title, 15, CText, bold: true));
                tile.AddView(Txt(t.Sub, 11, CMuted));
                tile.Click += (_, _) => t.Run();
                _actionTiles.Add(tile);
                var lp = new LinearLayout.LayoutParams(0, -2, 1f);
                lp.SetMargins(Dp(4), Dp(4), Dp(4), Dp(4));
                row.AddView(tile, lp);
            }
            card.AddView(row);
        }
    }

    // ---- Dashboard mașină de curse ----
    private DashboardView _dash = null!;
    private EditText _dashGroup = null!, _dashRpmMax = null!;
    private readonly System.Collections.Generic.Dictionary<string, (float Val, string Raw)> _chan = new();
    private DashboardView? _dashFull;
    private FrameLayout? _dashFullOverlay;

    // Canalele cerute: "grup.poziție", etichetă, unitate afișată, maxim scală. Primul = RPM (centru).
    private static readonly (string Ch, string Label, string Unit, float Max)[] DashMap =
    {
        ("2.1", "RPM",        "rpm", 7000),   // centru
        ("1.2", "Inj time",   "ms",  6),
        ("1.4", "Inj angle",  "°",   30),
        ("2.4", "Air mass",   "mg",  1200),
        ("3.2", "Battery",    "V",   16),
        ("3.4", "Intake T",   "°C",  100),
        ("21.3","Coolant T",  "°C",  130),
        ("1.3", "Pedal",      "%",   100),
    };
    // Grupurile care trebuie citite, în ordine
    private static readonly int[] DashGroups = { 1, 2, 3, 21 };
    private int _dashGroupIdx;
    private int _dashSettle;

    private void RefreshDashboard()
    {
        var rpmMax = float.TryParse(_dashRpmMax?.Text?.Trim(), out var rm) && rm > 0 ? rm : 7000f;
        var rpm = _chan.TryGetValue(DashMap[0].Ch, out var rv) ? rv.Val : 0;

        foreach (var view in new[] { _dash, _dashFull })
        {
            if (view == null) continue;
            view.SetRpm(rpm, rpmMax, rpmMax * 0.85f);
            for (var i = 1; i < DashMap.Length; i++)
            {
                var d = DashMap[i];
                var val = _chan.TryGetValue(d.Ch, out var cv) ? cv.Val : 0;
                view.SetGauge(i - 1, d.Label, val, d.Unit, d.Max);
            }
        }
    }

    private void OpenDashFullscreen()
    {
        _dashFullOverlay = new FrameLayout(this);
        _dashFullOverlay.SetBackgroundColor(Color.ParseColor("#05070A"));

        _dashFull = new DashboardView(this) { ShowWatermark = true };
        _dashFullOverlay.AddView(_dashFull, new FrameLayout.LayoutParams(-1, -1));

        var close = Txt("✕", 24, Color.White, bold: true);
        close.Gravity = GravityFlags.Center;
        close.Background = Oval(CCard2);
        close.Clickable = true;
        close.Click += (_, _) => CloseDashFullscreen();
        var lp = new FrameLayout.LayoutParams(Dp(48), Dp(48)) { Gravity = GravityFlags.Top | GravityFlags.End };
        lp.SetMargins(0, Dp(16), Dp(16), 0);
        _dashFullOverlay.AddView(close, lp);

        _root.AddView(_dashFullOverlay);

        // landscape + immersive
        RequestedOrientation = Android.Content.PM.ScreenOrientation.Landscape;
        if (Window != null)
        {
            Window.DecorView.SystemUiVisibility = (StatusBarVisibility)(
                SystemUiFlags.Immersive | SystemUiFlags.HideNavigation | SystemUiFlags.Fullscreen |
                SystemUiFlags.LayoutStable | SystemUiFlags.LayoutHideNavigation | SystemUiFlags.LayoutFullscreen);
        }
        RefreshDashboard();
    }

    private void CloseDashFullscreen()
    {
        if (_dashFullOverlay != null) _root.RemoveView(_dashFullOverlay);
        _dashFullOverlay = null;
        _dashFull = null;
        RequestedOrientation = Android.Content.PM.ScreenOrientation.Portrait;
        if (Window != null) Window.DecorView.SystemUiVisibility = (StatusBarVisibility)SystemUiFlags.Visible;
    }

    private int _dashNextGroup = 1;

    // Pornește citirea unui singur grup (țintă). Când se termină, dacă dashboard-ul e
    // încă activ, pornește automat grupul următor. Fără săgeți, fără scăpări de grup.
    private void RunDashGroup()
    {
        if (!_dashActive) return;
        _group = _dashNextGroup;
        var args = new System.Collections.Generic.List<string> { _group.ToString() };
        if (_fastInit != null && _fastInit.Checked) args.Add("fastinit");
        Execute("GroupRead", args.ToArray(), onFinish: () =>
        {
            if (_dashActive) _ui.PostDelayed(RunDashGroup, 150);
        });
    }

    private View BuildDashboardScreen()
    {
        var card = Card("DASHBOARD", "🏁");

        _dash = new DashboardView(this);
        card.AddView(_dash, new LinearLayout.LayoutParams(-1, Dp(360)));

        var cfg = HRow();
        cfg.AddView(Txt("RPM max ", 12, CMuted, bold: true));
        _dashRpmMax = Field("7000", numeric: true);
        _dashRpmMax.Text = "7000";
        cfg.AddView(_dashRpmMax, new LinearLayout.LayoutParams(0, -2, 1f));
        card.AddView(cfg, MatchWrap(top: 10));

        var btns = HRow();
        var start = BigButton("▶  PORNEȘTE");
        start.Click += (_, _) =>
        {
            _chan.Clear();
            _dashGroupIdx = 0;
            _dashNextGroup = DashGroups[0];
            _dashActive = true;
            RunDashGroup();
        };
        var stop = BigButton("■  STOP", CRed);
        stop.Click += (_, _) => { _dashActive = false; StopAll(); };
        btns.AddView(start, new LinearLayout.LayoutParams(0, -2, 1f) { RightMargin = Dp(6) });
        btns.AddView(stop, new LinearLayout.LayoutParams(0, -2, 1f) { LeftMargin = Dp(6) });
        card.AddView(btns, MatchWrap(top: 10));

        var full = BigButton("⛶  FULL SCREEN", CCard2);
        full.Click += (_, _) => OpenDashFullscreen();
        card.AddView(full, MatchWrap(top: 10));

        var note = Txt("Citește automat grupurile 1, 2, 3 și 21. Canale: 2-1 RPM, 1-2 timp inj, 1-4 unghi inj, " +
                       "2-4 masă aer, 3-2 baterie, 3-4 temp aer, 21-3 temp apă, 1-3 pedală. " +
                       "Pe K-line valorile se reîmprospătează la câteva secunde, nu instant.", 11, CMuted);
        note.SetPadding(0, Dp(10), 0, 0);
        card.AddView(note);
        return _lastWrap;
    }

    // ---- EEPROM: citire si scriere, prioritar ----
    private View BuildEepromScreen()
    {
        var card = Card("EEPROM • CITIRE / SCRIERE", "💾");

        card.AddView(Label("Citire"));
        AddTiles(card, new (string, string, string, Action)[]
        {
            ("⬇️", "CITEȘTE EEPROM", "backup complet → fișier", () => Execute("DumpEeprom", $"eeprom_{_address}_{DateTime.Now:yyyyMMdd_HHmmss}.bin")),
            ("🔑", "PIN / SKC", "GetSKC (bord 17)", () => Execute("GetSKC")),
        });

        card.AddView(Label("Scriere"));
        _eeFile = Field("Fișier EEPROM (ex: eeprom_17.bin) — Importă din Fișiere");
        card.AddView(_eeFile, MatchWrap(top: 4));
        var start = Field("Adresa de start (de obicei 0)", numeric: true);
        start.Text = "0";
        _eeStart = start;
        card.AddView(start, MatchWrap(top: 6));
        var write = BigButton("⚡  SCRIE EEPROM ÎN MODUL", CRed);
        write.Click += (_, _) =>
        {
            var f = (_eeFile.Text ?? "").Trim();
            if (f.Length == 0) { Toast.MakeText(this, "Alege fișierul EEPROM (Importă din Fișiere).", ToastLength.Long)!.Show(); return; }
            var st = (_eeStart.Text ?? "0").Trim();
            Confirm($"Scrii EEPROM-ul „{f}” în modulul {_address}?\n\nAi backup? Bateria pe redresor, nu deconecta cablul.",
                () => Execute("LoadEeprom", new[] { st, f }));
        };
        card.AddView(write, MatchWrap(top: 10));

        var note = Txt("Fă întâi CITEȘTE (backup) înainte de orice scriere. Fișierele intră în lista de la Fișiere.", 11, CMuted);
        note.SetPadding(0, Dp(10), 0, 0);
        card.AddView(note);
        return _lastWrap;
    }

    // ---- FLASH: citire si scriere, prioritar ----
    private View BuildFlashScreen()
    {
        var card = Card("FLASH ECU • CITIRE / SCRIERE", "🧠");

        card.AddView(Label("Tip ECU"));
        var ecuSpin = new Spinner(this);
        ecuSpin.Adapter = DarkAdapter(new[] { "EDC15 (Low)", "EDC15 (Medium)", "EDC15 (High)", "EDC16 (Low)", "EDC16 (Medium)", "EDC16 (High)" });
        ecuSpin.Background = Rounded(CCard2, 12, CLine);
        ecuSpin.SetPadding(Dp(8), Dp(6), Dp(8), Dp(6));
        card.AddView(ecuSpin, MatchWrap(top: 4));
        _mapEcu = ecuSpin;

        card.AddView(Label("Citire"));
        AddTiles(card, new (string, string, string, Action)[]
        {
            ("⬇️", "CITEȘTE FLASH", "backup → fișier", RunMapDump),
            ("💾", "Fișiere flash", "ce ai salvat", ListMaps),
        });

        card.AddView(Label("Scriere"));
        _mapFile = Field("Fișier flash (ex: stage1.bin) — Importă din Fișiere");
        card.AddView(_mapFile, MatchWrap(top: 4));
        var write = BigButton("⚡  SCRIE FLASH ÎN ECU", CRed);
        write.Click += (_, _) => RunMapWrite();
        card.AddView(write, MatchWrap(top: 10));

        var warn = Txt("Scrierea flash e ireversibilă dacă se întrerupe. Citește întâi (backup). " +
                       "Bateria pe redresor, nu deconecta cablul, nu bloca telefonul.",
            11, CAccent2);
        warn.SetPadding(0, Dp(10), 0, 0);
        card.AddView(warn);
        return _lastWrap;
    }

    private View BuildFilesCard()
    {
        var card = Card("FIȘIERE", "📁");
        var row = HRow();
        var imp = BigButton("📥  IMPORTĂ");
        imp.Click += (_, _) => ImportFile();
        var exp = BigButton("📤  EXPORTĂ");
        exp.Click += (_, _) => ExportFile();
        row.AddView(imp, new LinearLayout.LayoutParams(0, -2, 1f) { RightMargin = Dp(6) });
        row.AddView(exp, new LinearLayout.LayoutParams(0, -2, 1f) { LeftMargin = Dp(6) });
        card.AddView(row);

        AddTiles(card, new (string, string, string, Action)[]
        {
            ("💽", "Dump EDC15 EEPROM", "", () => Execute("DumpEdc15Eeprom", $"edc15_eeprom_{DateTime.Now:yyyyMMdd_HHmmss}.bin")),
            ("💽", "Dump EDC16 flash", "Low/Med/High", () => Execute("DumpEdc16Flash")),
            ("🧰", "ME7 info / EEPROM", "DumpMem", () => AskArgs("ME7 Dump", "START LENGTH [FILE]", a => Execute("DumpMem", a))),
            ("📄", "Listă fișiere", "ce e salvat pe telefon", ListFiles),
        });

        _fileList = Txt("", 12, CMuted, mono: true);
        _fileList.SetPadding(0, Dp(8), 0, 0);
        card.AddView(_fileList);
        ListFiles();
        return _lastWrap;
    }

    private View BuildSettingsCard()
    {
        var card = Card("SETĂRI", "⚙️");

        _verText2 = Txt($"Versiune: v{Updater.CurrentName(this)}  (build {Updater.CurrentVersion(this)})", 13, CAccent2, bold: true, mono: true);
        card.AddView(_verText2);
        var upd = BigButton("🔄  CAUTĂ ACTUALIZĂRI");
        upd.Click += (_, _) => CheckForUpdate(manual: true);
        card.AddView(upd, MatchWrap(top: 10));

        var hexWrap = MakeSwitch("Monitor HEX (octeți TX / RX bruți)", false);
        hexWrap.CheckedChange += (_, e) => { AndroidFtdiInterface.Sniff = e.IsChecked; while (AndroidFtdiInterface.Traffic.TryDequeue(out var __)) { } };
        card.AddView(hexWrap, MatchWrap(top: 12));

        var about = Txt("STP Programming • Street Tuning Parts\nMotor: kw1281test v1.0.4 (MIT, Greg Menounos)\n" +
                        "Diagnoză K-Line VAG: KW1281 și KWP2000.", 12, CMuted);
        about.SetPadding(0, Dp(14), 0, 0);
        card.AddView(about);
        return _lastWrap;
    }

    private View BuildConsoleCard()
    {
        var card = Card("CONSOLĂ", "🖥️");

        _log = new TextView(this) { Typeface = Typeface.Monospace };
        _log.SetTextSize(Android.Util.ComplexUnitType.Sp, 11);
        _log.SetTextColor(CText);
        _log.Background = Rounded(CTerm, 14, CLine);
        _log.SetPadding(Dp(12), Dp(10), Dp(12), Dp(10));
        _log.VerticalScrollBarEnabled = true;
        _log.MovementMethod = new ScrollingMovementMethod();
        _log.Touch += (_, e) =>
        {
            _log.Parent?.RequestDisallowInterceptTouchEvent(true);
            e.Handled = false;
        };
        card.AddView(_log, new LinearLayout.LayoutParams(-1, Dp(170)));

        var row = HRow();
        var clear = SmallButton("🗑  Curăță");
        clear.Click += (_, _) => { _ssb.Clear(); _log.TextFormatted = _ssb; };
        row.AddView(clear);
        var stop = BigButton("■  STOP OPERAȚIE", CRed);
        stop.Click += (_, _) => StopAll();
        row.AddView(stop, new LinearLayout.LayoutParams(0, -2, 1f) { LeftMargin = Dp(8) });
        card.AddView(row, MatchWrap(top: 8));
        return _lastWrap;
    }

    // ================================================================ Componente

    private LinearLayout _lastWrap = null!;

    private LinearLayout Card(string title, string icon)
    {
        var wrap = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _lastWrap = wrap;
        wrap.Background = Rounded(CCard, 20, CLine);
        wrap.SetPadding(Dp(16), Dp(14), Dp(16), Dp(16));
        var lp = new LinearLayout.LayoutParams(-1, -2);
        lp.SetMargins(0, 0, 0, Dp(12));
        wrap.LayoutParameters = lp;

        var h = HRow();
        var bar = new View(this) { Background = Rounded(CAccent, 3) };
        h.AddView(bar, new LinearLayout.LayoutParams(Dp(4), Dp(18)));
        var t = Txt($"  {icon}  {title}", 13, CText, bold: true);
        t.LetterSpacing = 0.08f;
        h.AddView(t);
        wrap.AddView(h);

        var body = new LinearLayout(this) { Orientation = Orientation.Vertical };
        body.SetPadding(0, Dp(10), 0, 0);
        wrap.AddView(body);
        return body;
    }

    private TextView Txt(string text, float sp, Color color, bool bold = false, bool mono = false)
    {
        var t = new TextView(this) { Text = text };
        t.SetTextSize(Android.Util.ComplexUnitType.Sp, sp);
        t.SetTextColor(color);
        if (mono) t.Typeface = Typeface.Create(Typeface.Monospace, bold ? TypefaceStyle.Bold : TypefaceStyle.Normal);
        else if (bold) t.Typeface = Typeface.Create("sans-serif-medium", TypefaceStyle.Bold);
        return t;
    }

    private TextView Label(string text)
    {
        var t = Txt(text.ToUpperInvariant(), 11, CMuted, bold: true);
        t.LetterSpacing = 0.08f;
        t.SetPadding(0, Dp(12), 0, Dp(4));
        return t;
    }

    private LinearLayout HRow()
    {
        var r = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        r.SetGravity(GravityFlags.CenterVertical);
        return r;
    }

    private View Dot(Color c, int sizeDp)
    {
        var v = new View(this);
        v.Background = Oval(c);
        v.LayoutParameters = new LinearLayout.LayoutParams(Dp(sizeDp), Dp(sizeDp));
        return v;
    }

    private Button BigButton(string text, Color? color = null)
    {
        var b = new Button(this) { Text = text, Elevation = 0 };
        b.SetTextColor(Color.White);
        b.SetTextSize(Android.Util.ComplexUnitType.Sp, 15);
        b.Typeface = Typeface.Create("sans-serif-medium", TypefaceStyle.Bold);
        b.LetterSpacing = 0.06f;
        b.SetAllCaps(false);
        var c = color ?? CAccent;
        var g = new GradientDrawable(GradientDrawable.Orientation.LeftRight!,
            new int[] { c.ToArgb(), Shade(c).ToArgb() });
        g.SetCornerRadius(Dp(14));
        b.Background = g;
        b.SetPadding(Dp(12), Dp(14), Dp(12), Dp(14));
        b.StateListAnimator = null;
        return b;
    }

    private Button SmallButton(string text)
    {
        var b = new Button(this) { Text = text };
        b.SetTextColor(CText);
        b.SetTextSize(Android.Util.ComplexUnitType.Sp, 13);
        b.SetAllCaps(false);
        b.Background = Rounded(CCard2, 12, CLine);
        b.SetPadding(Dp(12), Dp(8), Dp(12), Dp(8));
        b.StateListAnimator = null;
        b.SetMinWidth(0);
        b.SetMinimumWidth(0);
        var lp = new LinearLayout.LayoutParams(-2, -2);
        lp.SetMargins(Dp(3), 0, Dp(3), 0);
        b.LayoutParameters = lp;
        return b;
    }

    private EditText Field(string hint, bool numeric = false)
    {
        var e = new EditText(this) { Hint = hint };
        e.SetTextColor(CText);
        e.SetHintTextColor(CMuted);
        e.SetTextSize(Android.Util.ComplexUnitType.Sp, 14);
        e.Background = Rounded(CCard2, 12, CLine);
        e.SetPadding(Dp(12), Dp(10), Dp(12), Dp(10));
        e.SetSingleLine(true);
        if (numeric) e.InputType = InputTypes.ClassNumber;
        return e;
    }

    private Switch MakeSwitch(string text, bool on)
    {
        var s = new Switch(this) { Text = text, Checked = on };
        s.SetTextColor(CText);
        s.SetTextSize(Android.Util.ComplexUnitType.Sp, 13);
        return s;
    }

    private LinearLayout ChipRow(string[] items, List<TextView> store, Action<string> onClick)
    {
        var hs = HRow();
        foreach (var it in items)
        {
            var c = Txt(it, 13, CText, bold: true, mono: true);
            c.Gravity = GravityFlags.Center;
            c.SetPadding(Dp(8), Dp(9), Dp(8), Dp(9));
            c.Clickable = true;
            c.Tag = it;
            c.Click += (_, _) => onClick(it);
            store.Add(c);
            var lp = new LinearLayout.LayoutParams(0, -2, 1f);
            lp.SetMargins(Dp(3), 0, Dp(3), 0);
            hs.AddView(c, lp);
            StyleChip(c, false);
        }
        return hs;
    }

    private void StyleChip(TextView c, bool on)
    {
        c.Background = on ? Rounded(CAccent, 12) : Rounded(CCard2, 12, CLine);
        c.SetTextColor(on ? Color.White : CText);
    }

    private void StyleModule(TextView c, bool on)
    {
        c.Background = on ? Rounded(Color.ParseColor("#2A1A0C"), 14, CAccent) : Rounded(CCard2, 14, CLine);
        c.SetTextColor(on ? CAccent2 : CText);
    }

    private ArrayAdapter<string> DarkAdapter(string[] items)
    {
        var a = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem, items);
        a.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
        return a;
    }

    private LinearLayout.LayoutParams MatchWrap(int top = 0)
    {
        var lp = new LinearLayout.LayoutParams(-1, -2);
        lp.SetMargins(0, Dp(top), 0, 0);
        return lp;
    }

    private GradientDrawable Rounded(Color c, int radiusDp, Color? stroke = null)
    {
        var g = new GradientDrawable();
        g.SetColor(c.ToArgb());
        g.SetCornerRadius(Dp(radiusDp));
        if (stroke is { } s) g.SetStroke(Dp(1), s);
        return g;
    }

    private static GradientDrawable Oval(Color c)
    {
        var g = new GradientDrawable();
        g.SetShape(ShapeType.Oval);
        g.SetColor(c.ToArgb());
        return g;
    }

    private static Color Shade(Color c) =>
        Color.Argb(255, (int)(c.R * 0.75), (int)(c.G * 0.6), (int)(c.B * 0.6));

    private int Dp(int dp) => (int)(dp * (Resources?.DisplayMetrics?.Density ?? 1f));

    // ================================================================ Selectii

    private void SelectBaud(string b)
    {
        _baud = b;
        foreach (var c in _baudChips) StyleChip(c, (string?)c.Tag?.ToString() == b);
        if (_customBaud != null && (_customBaud.Text ?? "").Length > 0) _customBaud.Text = "";
    }

    private void SelectLatency(int ms)
    {
        AndroidFtdiInterface.LatencyMs = ms;
        foreach (var c in _latChips) StyleChip(c, c.Tag?.ToString() == $"{ms} ms");
    }

    private void SelectModule(string addr)
    {
        _address = addr;
        foreach (var c in _moduleChips) StyleModule(c, c.Tag?.ToString() == addr);
        if (_customAddr != null && (_customAddr.Text ?? "").Length > 0) _customAddr.Text = "";
        SetDisconnected();
    }

    private void ApplyProfile(int index)
    {
        var manual = index == 3;
        _dtr.Enabled = manual;
        _rts.Enabled = manual;
        if (!manual)
        {
            _dtr.Checked = true;
            _rts.Checked = false;
        }
    }

    private void SetDisconnected()
    {
        _connected = false;
        if (_ecuDot == null) return;
        _ecuDot.Background = Oval(CLedOff);
        var name = Modules.FirstOrDefault(m => m.Addr == _address).Name ?? "Modul";
        _ecuTitle.Text = $"  {name} ({_address}) • neconectat";
        _ecuTitle.SetTextColor(CMuted);
        _ecuInfo.Text = "Apasă CONECTEAZĂ pentru verificare.";
    }

    private void UpdateGroupLabel() => _liveGroup.Text = $"Grup {_group:D3}";

    // ================================================================ USB

    private void FindCable()
    {
        var devices = (_usb.DeviceList?.Values ?? Enumerable.Empty<UsbDevice>()).ToList();
        _device = devices.FirstOrDefault(d => d.VendorId == 0x0403);

        if (_device == null)
        {
            var other = devices.FirstOrDefault();
            _cableDot.Background = Oval(CRed);
            if (other == null)
            {
                _cableTitle.Text = "Niciun cablu conectat";
                _cableSub.Text = "Conectează cablul KKL prin adaptor OTG";
            }
            else
            {
                _cableTitle.Text = $"{ChipName(other)} — nesuportat";
                _cableSub.Text = $"VID:PID {other.VendorId:X4}:{other.ProductId:X4} • suportat doar FTDI";
            }
            return;
        }

        var desc = $"VID:PID {_device.VendorId:X4}:{_device.ProductId:X4}";
        try { if (!string.IsNullOrEmpty(_device.ProductName)) desc += " • " + _device.ProductName; } catch { }

        if (_usb.HasPermission(_device))
        {
            _cableDot.Background = Oval(CGreen);
            _cableTitle.Text = $"{ChipName(_device)} • pregătit";
            _cableSub.Text = desc;
            return;
        }

        _cableDot.Background = Oval(CAccent2);
        _cableTitle.Text = $"{ChipName(_device)} • aștept permisiunea";
        _cableSub.Text = desc;
        var intent = new Intent(ActionUsbPermission);
        intent.SetPackage(PackageName);
        var flags = Build.VERSION.SdkInt >= BuildVersionCodes.S ? PendingIntentFlags.Mutable : (PendingIntentFlags)0;
        _usb.RequestPermission(_device, PendingIntent.GetBroadcast(this, 0, intent, flags));
    }

    private static string ChipName(UsbDevice d) => (d.VendorId, d.ProductId) switch
    {
        (0x0403, 0x6001) => "FTDI FT232R / FT232BM",
        (0x0403, 0x6015) => "FTDI FT230X / FT231X",
        (0x0403, 0x6014) => "FTDI FT232H",
        (0x0403, 0x6010) => "FTDI FT2232",
        (0x0403, 0x6011) => "FTDI FT4232",
        (0x0403, _) => "FTDI",
        (0x1A86, _) => "CH340",
        (0x067B, _) => "Prolific PL2303",
        (0x10C4, _) => "Silabs CP210x",
        _ => "Dispozitiv USB",
    };

    private class UsbPermissionReceiver(MainActivity a) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            switch (intent?.Action)
            {
                case UsbManager.ActionUsbDeviceDetached:
                    a._current?.Abort();
                    a._device = null;
                    a.FindCable();
                    a.SetDisconnected();
                    a.Emit("Cablul a fost deconectat.", CRed);
                    break;
                case UsbManager.ActionUsbDeviceAttached:
                    a.FindCable();
                    break;
                default:
                    if (intent?.GetBooleanExtra(UsbManager.ExtraPermissionGranted, false) == true) a.FindCable();
                    else a._cableTitle.Text = "Permisiune USB refuzată";
                    break;
            }
        }
    }

    // ================================================================ Rulare

    private static bool IsWrite(string cmd) =>
        cmd.StartsWith("Load", StringComparison.OrdinalIgnoreCase) ||
        cmd.StartsWith("Write", StringComparison.OrdinalIgnoreCase) ||
        cmd is "SetSoftwareCoding" or "AdaptationSave" or "ClearFaultCodes";

    private void Confirm(string message, Action yes)
    {
        new AlertDialog.Builder(this)
            .SetTitle("⚠️  Confirmare")
            .SetMessage(message)
            .SetPositiveButton("Continuă", (_, _) => yes())
            .SetNegativeButton("Renunță", (_, _) => { })
            .Show();
    }

    private void Connect()
    {
        _sawEcu = false;
        _sawError = false;
        if (_ecuDot != null)
        {
            _ecuDot.Background = Oval(CAccent2);
            _ecuTitle.Text = $"  Se conectează la {_address}…";
            _ecuTitle.SetTextColor(CAccent2);
            _ecuInfo.Text = "Trezire modul (5 baud) • citire identificare…";
        }
        Execute("ReadIdent", Array.Empty<string>(), onFinish: () =>
        {
            _connected = _sawEcu || !_sawError;
            if (_sawError && !_sawEcu) _connected = false;
            if (_ecuDot != null)
            {
                _ecuDot.Background = Oval(_connected ? CGreen : CRed);
                _ecuTitle.Text = _connected ? $"  Conectat ✓  modul {_address}" : $"  Fără răspuns ✗  modul {_address}";
                _ecuTitle.SetTextColor(_connected ? CGreen : CRed);
                if (!_connected)
                    _ecuInfo.Text = "Verifică: contact pus, adresa modulului, baud rate, cablul.";
            }
            if (_connected)
            {
                Toast.MakeText(this, "Conectat ✓ — alege o funcție din meniu", ToastLength.Short)!.Show();
                if (_screen == Screen.Start) GoMenu();
            }
        });
    }

    private void ReadFaults()
    {
        lock (_faults) _faults.Clear();
        Execute("ReadFaultCodes", Array.Empty<string>(), onFinish: () =>
        {
            List<string> list;
            lock (_faults) list = _faults.ToList();
            var msg = list.Count == 0
                ? "Nu s-au primit coduri de eroare (verifică consola)."
                : string.Join("\n\n", list);
            new AlertDialog.Builder(this)
                .SetTitle($"⚠️  Coduri eroare • modul {_address}")
                .SetMessage(msg)
                .SetPositiveButton("OK", (_, _) => { })
                .SetNeutralButton("Șterge erorile", (_, _) =>
                    Confirm("Ștergi codurile de eroare din modulul " + _address + "?", () => Execute("ClearFaultCodes")))
                .Show();
        });
    }

    private static string DescribeFault(int code, int status, bool intermittent)
    {
        if (code == 65535 && status == 8)
            return "✓ Nicio eroare memorată";
        var head = $"{code:D5}";
        var d = DtcDatabase.Find(code);
        if (d is { } v && v.Obd.Length > 0) head += $" ({v.Obd})";
        var desc = d?.Text ?? "Cod necunoscut în baza de date";
        var tag = intermittent ? "intermitent" : "permanent";
        return $"{head}: {desc}\nTip {status:D2} • {tag}";
    }

    private void ToggleRecording()
    {
        if (_csv == null)
        {
            _csvName = $"live_{_address}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            _csv = new StreamWriter(System.IO.Path.Combine(_workDir, _csvName), false, new UTF8Encoding(true)) { AutoFlush = true };
            _csv.WriteLine($"# STP Programming - Live Data modul {_address}");
            _csv.WriteLine("Ora;Grup;Camp 1;Camp 2;Camp 3;Camp 4");
            _recBtn.Text = "⏹  Oprește înregistrarea";
            _recBtn.SetTextColor(CRed);
            Emit($"Înregistrare pornită: {_csvName}", CRed);
        }
        else
        {
            try { _csv.Dispose(); } catch { }
            _csv = null;
            _recBtn.Text = "⏺  Înregistrează CSV";
            _recBtn.SetTextColor(CText);
            Emit($"Salvat: {_csvName} (îl găsești la Exportă)", CGreen);
        }
    }

    private void DrainHex()
    {
        if (!AndroidFtdiInterface.Sniff) return;
        var sb = new StringBuilder();
        bool? dir = null;
        var n = 0;
        void Flush()
        {
            if (sb.Length == 0 || dir == null) return;
            Emit((dir.Value ? "TX ▶ " : "RX ◀ ") + sb.ToString().TrimEnd(), dir.Value ? CCyan : CGreen);
            sb.Clear();
            n = 0;
        }
        var guard = 0;
        while (guard++ < 400 && AndroidFtdiInterface.Traffic.TryDequeue(out var t))
        {
            if (dir != t.Tx) { Flush(); dir = t.Tx; }
            foreach (var b in t.Data)
            {
                sb.Append(b.ToString("X2")).Append(' ');
                if (++n == 16) Flush();
            }
        }
        Flush();
    }

    // ================================================================ Actualizare OTA

    private async void CheckForUpdate(bool manual)
    {
        if (_updating) return;
        try
        {
            if (manual) Emit("Verific actualizări…", CMuted);
            var rel = await Updater.CheckAsync();
            var current = Updater.CurrentVersion(this);
            if (rel == null || rel.Version <= current)
            {
                if (manual)
                {
                    Emit($"Ai ultima versiune (build {current}).", CGreen);
                    Toast.MakeText(this, "Ai ultima versiune ✓", ToastLength.Short)!.Show();
                }
                return;
            }

            var notes = string.IsNullOrWhiteSpace(rel.Notes) ? "" : "\n\n" + rel.Notes.Trim();
            var size = rel.Size > 0 ? $" • {rel.Size / 1048576.0:0.0} MB" : "";
            new AlertDialog.Builder(this)
                .SetTitle("🔄  Versiune nouă disponibilă")
                .SetMessage($"{rel.Tag} (build {rel.Version}){size}\nAi acum build {current}.{notes}")
                .SetPositiveButton("Actualizează", (_, _) => DownloadAndInstall(rel))
                .SetNegativeButton("Mai târziu", (_, _) => { })
                .Show();
        }
        catch (Exception ex)
        {
            if (manual)
            {
                Emit($"Nu pot verifica actualizările: {ex.Message}", CRed);
                Emit("Verifică internetul și dacă repository-ul GitHub e public.", CMuted);
            }
        }
    }

    private async void DownloadAndInstall(Updater.Release rel)
    {
        if (_worker is { IsAlive: true })
        {
            Toast.MakeText(this, "Așteaptă să se termine operația curentă.", ToastLength.Long)!.Show();
            return;
        }
        if (!Updater.EnsureInstallPermission(this))
        {
            Toast.MakeText(this, "Permite instalarea din această aplicație, apoi apasă din nou Actualizări.", ToastLength.Long)!.Show();
            return;
        }
        _updating = true;
        try
        {
            var dir = System.IO.Path.Combine(CacheDir!.AbsolutePath, "update");
            Directory.CreateDirectory(dir);
            var apk = System.IO.Path.Combine(dir, "STP-Programming.apk");
            Emit($"Descarc {rel.Tag}…", CAccent2);
            await Updater.DownloadAsync(rel.ApkUrl, apk, pct => _ui.Post(() => _verText.Text = $"Descărcare {pct}%"));
            _verText.Text = "Instalare…";
            Emit("Descărcat. Confirmă instalarea în fereastra Android.", CAccent2);
            Updater.Install(this, apk);
        }
        catch (Exception ex)
        {
            _updating = false;
            _verText.Text = $"v{Updater.CurrentName(this)}  (build {Updater.CurrentVersion(this)})";
            Emit($"Actualizare eșuată: {ex.Message}", CRed);
        }
    }

    // ---- Helperi pentru taburile noi ----

    private void AskArgs(string title, string hint, Action<string[]> run)
    {
        var input = Field(hint);
        var pad = Dp(16);
        var box = new FrameLayout(this);
        box.SetPadding(pad, pad / 2, pad, 0);
        box.AddView(input);
        new AlertDialog.Builder(this)
            .SetTitle(title)
            .SetView(box)
            .SetPositiveButton("OK", (_, _) =>
                run((input.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)))
            .SetNegativeButton("Renunță", (_, _) => { })
            .Show();
    }

    private (string Cmd, string Level) MapTarget()
    {
        var sel = _mapEcu.SelectedItem?.ToString() ?? "EDC15 (Low)";
        var level = sel.Contains("High") ? "High" : sel.Contains("Medium") ? "Medium" : "Low";
        var cmd = sel.StartsWith("EDC16") ? "DumpEdc16Flash" : "DumpEdc15Flash";
        return (cmd, level);
    }

    private void RunMapDump()
    {
        var (cmd, level) = MapTarget();
        var file = $"flash_{level}_{DateTime.Now:yyyyMMdd_HHmmss}.bin";
        Execute(cmd, new[] { level, file });
    }

    private void RunMapWrite()
    {
        var file = (_mapFile.Text ?? "").Trim();
        if (file.Length == 0)
        {
            Toast.MakeText(this, "Alege întâi fișierul hărții (Importă din fila Fișiere).", ToastLength.Long)!.Show();
            return;
        }
        var sel = _mapEcu.SelectedItem?.ToString() ?? "EDC15 (Low)";
        var level = sel.Contains("High") ? "High" : sel.Contains("Medium") ? "Medium" : "Low";
        var cmd = sel.StartsWith("EDC16") ? "LoadEdc16Flash" : "LoadEdc15Flash";
        Confirm($"{cmd} SCRIE harta „{file}” în ECU.\n\nAi făcut backup (Citește flash)? " +
                "Bateria pe redresor, nu deconecta cablul, nu bloca telefonul până la final.",
            () => Execute(cmd, new[] { level, file }));
    }

    private void ListMaps() => ListFiles("flash_");

    private void ListFiles() => ListFiles(null);

    private void ListFiles(string? prefix)
    {
        try
        {
            var files = Directory.GetFiles(_workDir)
                .Select(f => System.IO.Path.GetFileName(f)).OfType<string>()
                .Where(f => prefix == null || f.StartsWith(prefix))
                .OrderByDescending(f => File.GetLastWriteTime(System.IO.Path.Combine(_workDir, f)))
                .Take(30).ToArray();
            if (_fileList == null) return;
            _fileList.Text = files.Length == 0
                ? "Niciun fișier încă."
                : string.Join("\n", files.Select(f =>
                {
                    var len = new FileInfo(System.IO.Path.Combine(_workDir, f)).Length;
                    var size = len >= 1024 ? $"{len / 1024.0:0.0} KB" : $"{len} B";
                    return $"• {f}  ({size})";
                }));
        }
        catch
        {
            // ignoram
        }
    }

    private void StartLive()
    {
        _dashActive = false; // implicit NU dashboard; ecranul Dashboard îl reactivează după
        if (_liveCard != null)
        {
            _liveCard.Visibility = ViewStates.Visible;
            for (var i = 0; i < 4; i++) { _liveCells[i].Text = "—"; _liveLabels[i].Text = $"Câmp {i + 1}"; }
            _liveRaw.Text = "Pornire…";
        }
        _graph?.Clear();
        var args = new List<string> { _group.ToString() };
        if (_fastInit != null && _fastInit.Checked) args.Add("fastinit");
        Execute("GroupRead", args.ToArray());
    }

    private void StopAll()
    {
        if (_worker is not { IsAlive: true }) return;
        Emit("[STOP cerut]", CRed);
        AppConsole.PushKey(ConsoleKey.Q, 'q');
        _ui.PostDelayed(() => { if (_worker is { IsAlive: true }) _current?.Abort(); }, 1500);
    }

    private void Execute(string cmd, params string[] extra) => Execute(cmd, extra, null);

    private void Execute(string cmd, string[] extra, Action? onFinish)
    {
        if (_worker is { IsAlive: true })
        {
            Toast.MakeText(this, "O operație rulează deja. Apasă STOP.", ToastLength.Short)!.Show();
            return;
        }
        if (_device == null || !_usb.HasPermission(_device))
        {
            FindCable();
            Toast.MakeText(this, "Conectează cablul FTDI și acceptă permisiunea USB.", ToastLength.Long)!.Show();
            return;
        }

        var baud = _baud.Trim();
        if (!int.TryParse(baud, out var b) || b < 300 || b > 250000)
        {
            Toast.MakeText(this, "Baud rate invalid.", ToastLength.Short)!.Show();
            return;
        }

        var args = new List<string> { "USB", baud, _address.Trim(), cmd };
        args.AddRange(extra);

        AppConsole.Reset();
        _onFinish = onFinish;
        _sawEcu = false;
        _sawError = false;
        Emit("", CMuted);
        Emit($"══ {cmd}  •  modul {_address}  •  {baud} baud ══", CAccent);
        SetBusy(true, cmd);

        _worker = new Thread(() =>
        {
            try
            {
                Android.OS.Process.SetThreadPriority(Android.OS.ThreadPriority.UrgentAudio);
                Directory.SetCurrentDirectory(_workDir);
                BitFab.KW1281Test.Program.Main(args.ToArray());
            }
            catch (Exception ex)
            {
                _sawError = true;
                Emit($"Eroare: {ex.Message}", CRed);
            }
            finally
            {
                _current = null;
                _router?.Flush();
                Emit("══ Gata ══", CMuted);
                _ui.Post(() =>
                {
                    SetBusy(false, cmd);
                    var f = _onFinish;
                    _onFinish = null;
                    f?.Invoke();
                });
            }
        }, 16 * 1024 * 1024) { IsBackground = true, Name = "kw1281" };
        _worker.Start();
    }

    private void SetBusy(bool busy, string cmd)
    {
        if (_busy != null) _busy.Text = busy ? $"⏳  Rulează: {cmd}…" : "";
        if (_connectBtn != null)
        {
            _connectBtn.Enabled = !busy;
            _connectBtn.Alpha = busy ? 0.5f : 1f;
        }
        foreach (var t in _actionTiles) t.Alpha = busy ? 0.5f : 1f;
    }

    // ================================================================ Consola si actualizare UI

    internal void Emit(string text, Color color) => _pending.Enqueue((text, color));

    internal void OnLine(string line)
    {
        var l = line.TrimEnd();

        // Orice linie "Group NNN: ..." alimentează Live Data / Dashboard,
        // indiferent dacă a venit ca overlay sau ca linie normală.
        var gl = l.TrimStart();
        if (gl.StartsWith("Group ", StringComparison.Ordinal) && gl.Contains(':'))
        {
            _overlay = gl;
            return;
        }

        Color col = CText;
        var fm = FaultRx.Match(l);
        if (fm.Success)
        {
            var text = DescribeFault(int.Parse(fm.Groups[1].Value), int.Parse(fm.Groups[2].Value), fm.Groups[3].Value == "10");
            lock (_faults) _faults.Add(text);
            Emit(l, CRed);
            Emit("      → " + text.Replace("\n", "\n        "), CAccent2);
            return;
        }
        if (l.StartsWith("ECU:", StringComparison.Ordinal))
        {
            _sawEcu = true;
            col = CAccent2;
            var info = l[4..].Trim();
            _ui.Post(() => { if (_ecuInfo != null) _ecuInfo.Text = info; });
        }
        else if (l.StartsWith("SEND", StringComparison.Ordinal) || l.StartsWith("Sent", StringComparison.Ordinal) || l.StartsWith("TX", StringComparison.Ordinal))
            col = CCyan;
        else if (l.StartsWith("Received", StringComparison.Ordinal) || l.StartsWith("RX", StringComparison.Ordinal) || l.StartsWith("RECV", StringComparison.Ordinal))
            col = CGreen;
        else if (l.Contains("Caught:") || l.Contains("Unhandled") || l.Contains("Unable to") ||
                 l.Contains("Exception") || l.Contains("Timeout") || l.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            _sawError = true;
            col = CRed;
        }
        else if (l.StartsWith("Version ") || l.StartsWith("Command Line") || l.StartsWith("OSVersion") ||
                 l.StartsWith(".NET") || l.StartsWith("Culture") || l.StartsWith("Opening"))
            col = CMuted;
        else if (l.Contains("Coding") || l.Contains("WSC") || l.Contains("Fault") || l.Contains("SKC"))
            col = CAccent2;
        Emit(l, col);
    }

    internal void OnOverlay(string text) => _overlay = text;

    private void Tick()
    {
        try
        {
            DrainHex();
            // Consola
            var changed = false;
            while (_pending.TryDequeue(out var item))
            {
                var start = _ssb.Length();
                _ssb.Append(new Java.Lang.String(item.Text + "\n"));
                _ssb.SetSpan(new ForegroundColorSpan(item.Color), start, _ssb.Length(), SpanTypes.ExclusiveExclusive);
                changed = true;
            }
            if (changed)
            {
                if (_ssb.Length() > 120_000) _ssb.Delete(0, _ssb.Length() - 80_000);
                _log.TextFormatted = _ssb;
                _log.Post(() =>
                {
                    var layout = _log.Layout;
                    if (layout == null) return;
                    var y = layout.GetLineTop(_log.LineCount) - _log.Height + _log.PaddingBottom + _log.PaddingTop;
                    if (y > 0) _log.ScrollTo(0, y);
                });
            }

            // Live Data
            var ov = _overlay;
            if (ov != null)
            {
                _overlay = null;
                ShowOverlay(ov);
            }

            // Becuri RX / TX + rata
            var now = SystemClock.UptimeMillis();
            var tx = AndroidFtdiInterface.TxBytes;
            var rx = AndroidFtdiInterface.RxBytes;
            if (tx != _lastTx) { _ledTxUntil = now + 90; _lastTx = tx; }
            if (rx != _lastRx) { _ledRxUntil = now + 90; _lastRx = rx; }
            _ledTx.Background = Oval(now < _ledTxUntil ? CAccent : CLedOff);
            _ledRx.Background = Oval(now < _ledRxUntil ? CGreen : CLedOff);
            if (_barCable != null)
            {
                var cableReady = _device != null && _usb.HasPermission(_device);
                _barCable.Background = Oval(cableReady ? CGreen : CRed);
            }
            if (now - _rateTick >= 1000)
            {
                var total = tx + rx;
                var rate = (total - _rateBase) * 1000.0 / Math.Max(1, now - _rateTick);
                _rateBase = total;
                _rateTick = now;
                _rate.Text = rate >= 1024 ? $"{rate / 1024:0.0} KB/s" : $"{rate:0} B/s";
                _counters.Text = $"TX {Fmt(tx)}  •  RX {Fmt(rx)}";
            }
        }
        catch
        {
            // UI-ul nu trebuie sa cada din cauza unui log
        }
        _ui.PostDelayed(Tick, 50);
    }

    private static string Fmt(long b) => b >= 1_048_576 ? $"{b / 1048576.0:0.0}M" : b >= 1024 ? $"{b / 1024.0:0.0}K" : b.ToString();

    private void ShowOverlay(string text)
    {
        text = text?.Trim() ?? "";
        // uneori overlay-ul vine cu text de ștergere înainte; ia doar de la "Group"
        var gpos = text.IndexOf("Group ", StringComparison.Ordinal);
        if (gpos > 0) text = text[gpos..];
        if (_liveCard != null) { _liveCard.Visibility = ViewStates.Visible; _liveRaw.Text = text; }
        var idx = text.IndexOf(':');
        if (!text.StartsWith("Group ") || idx <= 0) return;

        if (int.TryParse(text[6..idx].Trim(), out var g)) { _group = g; UpdateGroupLabel(); }
        var body = text[(idx + 1)..].Trim();
        var parts = body.Split(" | ");
        var nums = new float?[4];
        for (var i = 0; i < Math.Min(4, parts.Length); i++)
        {
            var m = NumRx.Match(parts[i]);
            if (m.Success && float.TryParse(m.Value.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var f)) nums[i] = f;
        }

        _graph?.Add(nums);

        // Dashboard racing: reține fiecare poziție din grupul citit și trece la următorul grup
        // prin RELANSAREA comenzii GroupRead (fără săgeți care scapă de sub control).
        if (_dashActive && _dash != null)
        {
            for (var i = 0; i < parts.Length; i++)
                _chan[$"{_group}.{i + 1}"] = (nums[i] ?? 0, parts[i].Trim());
            RefreshDashboard();

            if (_group == DashGroups[_dashGroupIdx])
            {
                // am citit grupul țintă: oprește rularea curentă și treci la următorul grup
                _dashGroupIdx = (_dashGroupIdx + 1) % DashGroups.Length;
                _dashNextGroup = DashGroups[_dashGroupIdx];
                AppConsole.PushKey(ConsoleKey.Q, 'q'); // iese din GroupRead curent
            }
        }

        if (_csv != null)
        {
            try
            {
                _csv.WriteLine($"{DateTime.Now:HH:mm:ss.fff};{_group};" +
                    string.Join(';', Enumerable.Range(0, 4).Select(i => i < parts.Length ? parts[i].Trim().Replace(';', ',') : "")));
            }
            catch { }
        }

        if (_liveCells[0] != null)
        {
            for (var i = 0; i < 4; i++)
            {
                if (i < parts.Length)
                {
                    var p = parts[i].Trim();
                    _liveCells[i].Text = p.Length > 0 ? p : "—";
                    _liveLabels[i].Text = $"Câmp {i + 1}";
                }
                else _liveCells[i].Text = "—";
            }
        }
    }

    // Deduce unitatea și o scară de maxim rezonabilă din textul câmpului (ex: "90.5 °C")
    private static string UnitOf(string field, out string unit, out float max)
    {
        var f = field.Trim();
        unit = "";
        max = 100;
        var sp = f.LastIndexOf(' ');
        if (sp > 0 && sp < f.Length - 1) unit = f[(sp + 1)..].Trim();
        var u = unit.ToLowerInvariant();
        if (u.Contains("°c") || u.Contains("c")) max = 150;
        else if (u.Contains("bar")) max = 25;
        else if (u.Contains("mbar")) max = 2500;
        else if (u.Contains("v")) max = 16;
        else if (u.Contains("%")) max = 100;
        else if (u.Contains("km/h")) max = 260;
        else if (u.Contains("mg")) max = 80;
        else if (u.Contains("/min") || u.Contains("rpm")) max = 7000;
        return f.Length > 0 ? f : "—";
    }

    // ================================================================ Fisiere

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
            Toast.MakeText(this, "Nu există fișiere încă.", ToastLength.Short)!.Show();
            return;
        }
        new AlertDialog.Builder(this)
            .SetTitle("Alege fișierul de exportat")
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
                    input.CopyTo(output);
                _args.Text = ((_args.Text ?? "") + " " + name).Trim();
                Emit($"Importat: {name}", CGreen);
            }
            else if (requestCode == ReqExport && _pendingExport != null)
            {
                using (var input = File.OpenRead(System.IO.Path.Combine(_workDir, _pendingExport)))
                using (var output = ContentResolver!.OpenOutputStream(uri)!)
                    input.CopyTo(output);
                Emit($"Exportat: {_pendingExport}", CGreen);
            }
        }
        catch (Exception ex)
        {
            Emit($"Eroare fișier: {ex.Message}", CRed);
        }
    }

    private string? QueryName(Android.Net.Uri uri)
    {
        using var cursor = ContentResolver!.Query(uri, null, null, null, null);
        if (cursor == null || !cursor.MoveToFirst()) return null;
        var idx = cursor.GetColumnIndex("_display_name");
        return idx >= 0 ? cursor.GetString(idx) : null;
    }

    // ================================================================ Router consola

    /// <summary>Primeste tot ce scrie kw1281test: linii -> consola colorata, suprascrieri -> Live Data.</summary>
    private sealed class ConsoleRouter(MainActivity a) : TextWriter
    {
        private readonly StringBuilder _line = new();

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            lock (_line)
            {
                if (value == '\n') { a.OnLine(_line.ToString()); _line.Clear(); }
                else if (value != '\r') _line.Append(value);
            }
        }

        public override void Write(string? value)
        {
            if (string.IsNullOrEmpty(value)) return;

            var trimmed = value.Trim();

            // Rândul de ștergere (doar spații) care precede overlay-ul: îl ignorăm,
            // DAR nu consumăm semnalul de overlay — îl păstrăm pentru textul real care urmează.
            if (trimmed.Length == 0 && AppConsole.PeekOverlay())
                return;

            // Orice text cu "Group " e o valoare de măsurare (Live Data / Dashboard),
            // indiferent dacă semnalul de overlay a fost sau nu consumat între timp.
            if (trimmed.Contains("Group ") && trimmed.Contains(':'))
            {
                AppConsole.TakeOverlay(); // consumă semnalul dacă există
                a.OnOverlay(trimmed);
                return;
            }

            if (AppConsole.TakeOverlay())
            {
                if (trimmed.Length > 0) a.OnOverlay(trimmed);
                return;
            }
            lock (_line)
            {
                foreach (var c in value) Write(c);
                // Intrebari de tip "[y/N] " fara linie noua: afiseaza-le imediat
                var s = _line.ToString();
                if (s.EndsWith("] ") || s.EndsWith("? ") || s.EndsWith(": "))
                {
                    a.OnLine(s);
                    _line.Clear();
                }
            }
        }

        public override void WriteLine(string? value)
        {
            lock (_line)
            {
                if (value != null) foreach (var c in value) Write(c);
                Write('\n');
            }
        }

        public override void WriteLine() => Write('\n');

        public override void Flush()
        {
            lock (_line)
            {
                if (_line.Length > 0) { a.OnLine(_line.ToString()); _line.Clear(); }
            }
        }
    }
}
