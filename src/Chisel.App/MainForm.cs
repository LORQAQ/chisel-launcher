using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Chisel.Core;
using Chisel.Core.Models;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Chisel.App;

public sealed class MainForm : Form, IHostWindow
{
    private const int WmNcHitTest = 0x0084;
    private const int HtLeft = 10, HtRight = 11, HtTop = 12, HtTopLeft = 13, HtTopRight = 14;
    private const int HtBottom = 15, HtBottomLeft = 16, HtBottomRight = 17;
    private const int WmNclButtonDown = 0x00A1;
    private const int HtCaption = 2;
    private const int ResizeBand = 6;

    private readonly AppPaths _paths;
    private readonly WebView2 _webView = new();
    private Bridge? _bridge;
    private bool _webViewReady;
    private bool _initialized;
    private bool _closing;

    public MainForm(AppPaths paths)
    {
        _paths = paths;
        Text = "凿境";
        FormBorderStyle = FormBorderStyle.None;
        BackColor = Color.FromArgb(18, 19, 17);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        MinimumSize = ScaleForDpi(1100, 720);
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        KeyPreview = false;
        Padding = new Padding(ResizeBand);

        _bridge = new Bridge(paths, this);
        var settings = _bridge.SettingsSnapshot();
        var width = Math.Max(MinimumSize.Width, settings.WindowWidth);
        var height = Math.Max(MinimumSize.Height, settings.WindowHeight);
        var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1440, 900);
        Size = new Size(Math.Min(width, work.Width - 40), Math.Min(height, work.Height - 40));
        if (settings.WindowMaximized) WindowState = FormWindowState.Maximized;

        _webView.Dock = DockStyle.Fill;
        _webView.ZoomFactor = 1.0;
        _webView.DefaultBackgroundColor = Color.FromArgb(18, 19, 17);
        Controls.Add(_webView);

        Load += (_, _) => ApplyWindowTheme();
        Shown += async (_, _) => await InitializeWebViewAsync();
        Resize += (_, _) =>
        {
            Padding = WindowState == FormWindowState.Maximized ? Padding.Empty : new Padding(ResizeBand);
            _bridge?.EmitEvent("window.state", new { maximized = WindowState == FormWindowState.Maximized });
            ApplyMemoryTarget();
        };
        FormClosing += (_, _) => OnClosingCleanup();
    }

    public bool IsMaximized => WindowState == FormWindowState.Maximized;

    public AppPaths Paths => _paths;

    private Size ScaleForDpi(int width, int height)
    {
        var scale = Math.Max(1.0, DeviceDpi / 96.0);
        return new Size((int)Math.Round(width * scale), (int)Math.Round(height * scale));
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        MinimumSize = ScaleForDpi(1100, 720);
    }

    private bool IsDev
    {
        get
        {
            var url = Environment.GetEnvironmentVariable("CHISEL_DEV_URL");
            return !string.IsNullOrWhiteSpace(url);
        }
    }

    private async Task InitializeWebViewAsync()
    {
        if (_initialized) return;
        _initialized = true;
        try
        {
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _paths.WebViewDir);
            await _webView.EnsureCoreWebView2Async(environment);
            var core = _webView.CoreWebView2;
            var settings = core.Settings;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = IsDev || Environment.GetEnvironmentVariable("CHISEL_DEVTOOLS") == "1";
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            settings.IsPinchZoomEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = IsDev;
            settings.IsPasswordAutosaveEnabled = false;
            settings.IsGeneralAutofillEnabled = false;

            core.WebMessageReceived += OnWebMessage;
            core.NewWindowRequested += OnNewWindow;
            core.NavigationStarting += OnNavigationStarting;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.ProcessFailed += (_, e) =>
            {
                if (_closing) return;
                _bridge?.EmitEvent("webview.failed", new { reason = e.ProcessFailedKind.ToString() });
                try { core.Reload(); } catch { }
            };

            var devUrl = Environment.GetEnvironmentVariable("CHISEL_DEV_URL");
            if (!string.IsNullOrWhiteSpace(devUrl) &&
                Uri.TryCreate(devUrl, UriKind.Absolute, out var uri) &&
                uri.Host is "localhost" or "127.0.0.1")
            {
                core.Navigate(devUrl);
            }
            else
            {
                var root = FindWebRoot();
                if (root == null)
                {
                    core.NavigateToString(FallbackPage("找不到界面文件。请先构建 web 目录，或设置 CHISEL_DEV_URL。"));
                    return;
                }
                core.SetVirtualHostNameToFolderMapping("chisel.local", root, CoreWebView2HostResourceAccessKind.Allow);
                core.Navigate("https://chisel.local/index.html");
            }
            _webViewReady = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "无法初始化内嵌浏览器。请确认已安装 Microsoft Edge WebView2 Runtime。\n\n" + ex.Message,
                "凿境", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    private string? FindWebRoot()
    {
        var candidates = new List<string> { Path.Combine(AppContext.BaseDirectory, "wwwroot") };
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            candidates.Add(Path.Combine(dir.FullName, "web", "dist"));
        return candidates.FirstOrDefault(c => File.Exists(Path.Combine(c, "index.html")));
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var bridge = _bridge;
        if (bridge == null) return;
        _ = bridge.HandleAsync(e.WebMessageAsJson);
    }

    private void OnNewWindow(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            OpenExternal(uri.ToString());
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Uri) || e.Uri == "about:blank") return;
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)) { e.Cancel = true; return; }
        if (uri.Host.Equals("chisel.local", StringComparison.OrdinalIgnoreCase)) return;
        if (uri.Host is "localhost" or "127.0.0.1") return;
        e.Cancel = true;
        if (uri.Scheme == Uri.UriSchemeHttps) OpenExternal(uri.ToString());
    }

    private static string FallbackPage(string message) => $$"""
        <!doctype html><html lang="zh-CN"><meta charset="utf-8">
        <style>
        body{margin:0;background:#121311;color:#e8e3d7;font:16px/1.7 "Microsoft YaHei UI",sans-serif;
        display:grid;place-items:center;height:100vh}
        .box{max-width:460px;padding:40px;border:1px solid #35362f;background:#191a17}
        h1{font:500 28px/1.3 "Noto Serif SC",serif;margin:0 0 12px;letter-spacing:.08em}
        p{color:#a8a496;white-space:pre-wrap}
        </style><div class="box"><h1>凿境</h1><p>{{System.Net.WebUtility.HtmlEncode(message)}}</p></div></html>
        """;

    public AppSettings SettingsSnapshot() => _bridge?.SettingsSnapshot() ?? new AppSettings();

    public void EmitEvent(string name, object? data) => _bridge?.EmitEvent(name, data);

    public void PostEvent(string name, object? data)
    {
        var json = JsonSerializer.Serialize(new
        {
            type = "event",
            @event = name,
            data
        }, Bridge.JsonOptions);
        PostJson(json);
    }

    public bool CopyText(string text)
    {
        if (InvokeRequired)
        {
            try { return (bool)Invoke(new Func<bool>(() => CopyText(text))); }
            catch { return false; }
        }
        try
        {
            Clipboard.SetText(text ?? "");
            return true;
        }
        catch { return false; }
    }

    public void PostJson(string json)
    {
        if (_closing || IsDisposed) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(() => PostJson(json)); } catch { }
            return;
        }
        if (!_webViewReady) return;
        try { _webView.CoreWebView2?.PostWebMessageAsJson(json); }
        catch { }
    }

    public void SaveWindowState()
    {
        if (_bridge == null) return;
        var maximized = WindowState == FormWindowState.Maximized;
        var bounds = maximized ? RestoreBounds : Bounds;
        if (bounds.Width < 800 || bounds.Height < 600) return;
        _bridge.SaveWindowState(bounds.Width, bounds.Height, maximized);
    }

    private void OnClosingCleanup()
    {
        if (_closing) return;
        _closing = true;
        SaveWindowState();
        _bridge?.Shutdown();
    }

    private void ApplyMemoryTarget()
    {
        try
        {
            var core = _webView.CoreWebView2;
            if (core == null) return;
            core.MemoryUsageTargetLevel = WindowState == FormWindowState.Minimized
                ? CoreWebView2MemoryUsageTargetLevel.Low
                : CoreWebView2MemoryUsageTargetLevel.Normal;
        }
        catch { }
    }

    public void Minimize()
    {
        if (InvokeRequired) { BeginInvoke(Minimize); return; }
        WindowState = FormWindowState.Minimized;
        ApplyMemoryTarget();
    }

    public void ToggleMaximize()
    {
        if (InvokeRequired) { BeginInvoke(ToggleMaximize); return; }
        WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
    }

    public void CloseWindow()
    {
        if (InvokeRequired) { BeginInvoke(CloseWindow); return; }
        Close();
    }

    public void DragWindow()
    {
        if (InvokeRequired) { BeginInvoke(DragWindow); return; }
        ReleaseCapture();
        SendMessage(Handle, WmNclButtonDown, HtCaption, 0);
    }

    public Task<string?> PickJavaAsync()
    {
        if (InvokeRequired)
        {
            var tcs = new TaskCompletionSource<string?>();
            BeginInvoke(async () => tcs.TrySetResult(await PickJavaAsync()));
            return tcs.Task;
        }
        using var dialog = new OpenFileDialog
        {
            Title = "选择 java.exe",
            Filter = "Java 可执行文件|java.exe|所有文件|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        return Task.FromResult(dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null);
    }

    public Task<string?> PickImageAsync()
    {
        if (InvokeRequired)
        {
            var tcs = new TaskCompletionSource<string?>();
            BeginInvoke(async () => tcs.TrySetResult(await PickImageAsync()));
            return tcs.Task;
        }
        using var dialog = new OpenFileDialog
        {
            Title = "选择实例图标",
            Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp|所有文件|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        return Task.FromResult(dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null);
    }

    public Task<string?> PickModpackAsync()
    {
        if (InvokeRequired)
        {
            var tcs = new TaskCompletionSource<string?>();
            BeginInvoke(async () => tcs.TrySetResult(await PickModpackAsync()));
            return tcs.Task;
        }
        using var dialog = new OpenFileDialog
        {
            Title = "选择整合包",
            Filter = "整合包|*.mrpack;*.zip|Modrinth 整合包|*.mrpack|CurseForge 整合包|*.zip",
            CheckFileExists = true,
            Multiselect = false
        };
        return Task.FromResult(dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null);
    }

    public void RevealInExplorer(string path)
    {
        if (InvokeRequired) { BeginInvoke(() => RevealInExplorer(path)); return; }
        try
        {
            if (File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            else if (Directory.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch { }
    }

    public void OpenExternal(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return;
        try { Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true }); }
        catch { }
    }

    private void ApplyWindowTheme()
    {
        try
        {
            var round = 2;
            DwmSetWindowAttribute(Handle, 33, ref round, sizeof(int));
            var dark = 1;
            DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
        }
        catch { }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmNcHitTest && WindowState == FormWindowState.Normal)
        {
            base.WndProc(ref m);
            if ((int)m.Result == 1)
            {
                var point = PointToClient(Cursor.Position);
                var w = ClientSize.Width;
                var h = ClientSize.Height;
                var left = point.X <= ResizeBand;
                var right = point.X >= w - ResizeBand;
                var top = point.Y <= ResizeBand;
                var bottom = point.Y >= h - ResizeBand;
                if (top && left) m.Result = HtTopLeft;
                else if (top && right) m.Result = HtTopRight;
                else if (bottom && left) m.Result = HtBottomLeft;
                else if (bottom && right) m.Result = HtBottomRight;
                else if (left) m.Result = HtLeft;
                else if (right) m.Result = HtRight;
                else if (top) m.Result = HtTop;
                else if (bottom) m.Result = HtBottom;
            }
            return;
        }
        base.WndProc(ref m);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
}
