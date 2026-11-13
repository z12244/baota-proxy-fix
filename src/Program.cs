using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

// 宝塔网络代理修复
// 作用：
//   1) 梯子（系统代理）开启时，把宝塔面板地址自动加入 Windows 代理例外（ProxyOverride），
//      客户端升级或梯子开关把例外清空后能自动补回来。
//   2) 新增：检测"系统代理开着、但代理端口没有任何程序监听"的死代理状态，
//      持续 grace_seconds 秒后自动关闭系统代理并刷新 DNS，避免重启后全网打不开。
internal static class Program
{
    private const string RegPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    private static string _baseDir;
    private static string _logPath;

    private static string[] _panelHosts = new string[0];
    private static bool _keepLocal = true;
    private static bool _repairDead = true;
    private static int _graceSeconds = 10;
    private static int _pollMs = 2000;

    private static string _lastStateKey = null;
    private static DateTime _deadSince = DateTime.MinValue;
    private static bool _deadPending = false;

    // 单实例：用文件锁而不是全局互斥体。进程异常退出时 OS 一定会释放文件句柄，
    // 不会像命名互斥体那样残留僵尸句柄导致新实例起不来。
    private static FileStream _instanceLock;

    private static void Main()
    {
        _baseDir = AppDomain.CurrentDomain.BaseDirectory;
        _logPath = Path.Combine(_baseDir, "run.log");
        LoadConfig();

        try
        {
            _instanceLock = new FileStream(
                Path.Combine(_baseDir, "run.lock"),
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
        }
        catch (IOException)
        {
            return; // 已有实例在跑
        }

        Log("start, pid=" + Process.GetCurrentProcess().Id);
        Run();
    }

    private static void Run()
    {
        while (true)
        {
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                Log("error: " + ex.GetType().Name + " " + ex.Message);
            }
            Thread.Sleep(_pollMs);
        }
    }

    private static void Tick()
    {
        object enableRaw = ReadValue("ProxyEnable");
        string server = ReadString("ProxyServer");
        bool enabled = enableRaw != null && Convert.ToInt32(enableRaw) == 1;

        if (!enabled)
        {
            _deadPending = false;
            LogStateChange("proxy_off", server);
            return;
        }

        int port;
        bool loopback;
        bool alive = IsProxyAlive(server, out port, out loopback);

        if (alive)
        {
            _deadPending = false;
            LogStateChange("ready", server);
            EnsureBypass();
            return;
        }

        if (!_repairDead || !loopback)
        {
            // 远端代理不归本工具管；也不做端口探测判定
            _deadPending = false;
            LogStateChange("ready", server);
            return;
        }

        if (!_deadPending)
        {
            _deadPending = true;
            _deadSince = DateTime.UtcNow;
            Log("state: proxy_dead|" + server + " (no listener on 127.0.0.1:" + port + ", waiting " + _graceSeconds + "s)");
            return;
        }

        if ((DateTime.UtcNow - _deadSince).TotalSeconds < _graceSeconds)
            return;

        RepairDeadProxy(server, port);
        _deadPending = false;
    }

    private static void RepairDeadProxy(string server, int port)
    {
        WriteDword("ProxyEnable", 0);
        WriteString("ProxyServer", "");
        NotifySettingsChanged();
        FlushDns();
        Log("state: proxy_dead_repaired|" + server + " -> ProxyEnable=0, ProxyServer cleared, dns flushed (port " + port + " had no listener)");
        _lastStateKey = null;
        LogStateChange("proxy_off", "");
    }

    private static void EnsureBypass()
    {
        if (_panelHosts.Length == 0 && !_keepLocal) return;

        string current = ReadString("ProxyOverride");
        List<string> entries = new List<string>();
        if (!string.IsNullOrEmpty(current))
        {
            foreach (string part in current.Split(';'))
            {
                string t = part.Trim();
                if (t.Length > 0) entries.Add(t);
            }
        }

        List<string> desired = new List<string>();
        foreach (string h in _panelHosts)
            if (h.Length > 0) desired.Add(h);
        if (_keepLocal) desired.Add("<local>");

        bool changed = false;
        int insertAt = 0;
        foreach (string d in desired)
        {
            if (!ContainsIgnoreCase(entries, d))
            {
                entries.Insert(insertAt, d);
                insertAt++;
                changed = true;
            }
        }

        if (!changed) return;

        string joined = string.Join(";", entries.ToArray());
        WriteString("ProxyOverride", joined);
        NotifySettingsChanged();
        Log("bypass updated (change) -> " + joined);
    }

    private static bool ContainsIgnoreCase(List<string> list, string value)
    {
        foreach (string s in list)
            if (string.Equals(s, value, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool IsProxyAlive(string server, out int port, out bool loopback)
    {
        port = 0;
        loopback = false;
        if (string.IsNullOrEmpty(server)) return false;

        string s = server.Trim();
        int scheme = s.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) s = s.Substring(scheme + 3);
        int semi = s.IndexOf(';');
        if (semi >= 0) s = s.Substring(0, semi);
        int slash = s.IndexOf('/');
        if (slash >= 0) s = s.Substring(0, slash);

        string host = s;
        int colon = s.LastIndexOf(':');
        if (colon > 0)
        {
            host = s.Substring(0, colon);
            int parsed;
            if (int.TryParse(s.Substring(colon + 1), out parsed)) port = parsed;
        }
        if (port <= 0) port = 80;

        loopback = host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                   || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                   || host.Equals("::1", StringComparison.Ordinal)
                   || host.Equals("[::1]", StringComparison.Ordinal);

        if (!loopback) return true; // 远端代理不做探测，视为正常

        try
        {
            using (var client = new TcpClient())
            {
                IAsyncResult ar = client.BeginConnect("127.0.0.1", port, null, null);
                bool ok = ar.AsyncWaitHandle.WaitOne(500);
                if (!ok) return false;
                client.EndConnect(ar);
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    private static void LogStateChange(string state, string server)
    {
        string key = state + "|" + (server ?? "");
        if (key == _lastStateKey) return;
        _lastStateKey = key;
        Log("state: " + key);
    }

    private static void LoadConfig()
    {
        string path = Path.Combine(_baseDir, "config.ini");
        if (!File.Exists(path)) return;

        try
        {
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";") || line.StartsWith("[")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                string v = line.Substring(eq + 1).Trim();

                if (k == "hosts")
                {
                    List<string> list = new List<string>();
                    foreach (string h in v.Split(','))
                    {
                        string t = h.Trim();
                        if (t.Length > 0) list.Add(t);
                    }
                    _panelHosts = list.ToArray();
                }
                else if (k == "keep_local")
                {
                    _keepLocal = v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
                }
                else if (k == "repair_dead_proxy")
                {
                    _repairDead = v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
                }
                else if (k == "grace_seconds")
                {
                    int n;
                    if (int.TryParse(v, out n) && n >= 0) _graceSeconds = n;
                }
                else if (k == "poll_seconds")
                {
                    int n;
                    if (int.TryParse(v, out n) && n >= 1) _pollMs = n * 1000;
                }
            }
        }
        catch
        {
        }
    }

    private static void Log(string message)
    {
        try
        {
            RotateIfNeeded();
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine;
            File.AppendAllText(_logPath, line, new UTF8Encoding(false));
        }
        catch
        {
        }
    }

    private static void RotateIfNeeded()
    {
        try
        {
            var info = new FileInfo(_logPath);
            if (info.Exists && info.Length > 1024 * 1024)
            {
                string old = _logPath + ".1";
                if (File.Exists(old)) File.Delete(old);
                File.Move(_logPath, old);
            }
        }
        catch
        {
        }
    }

    private static object ReadValue(string name)
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegPath, false))
        {
            if (key == null) return null;
            return key.GetValue(name, null);
        }
    }

    private static string ReadString(string name)
    {
        object v = ReadValue(name);
        return v == null ? "" : Convert.ToString(v);
    }

    private static void WriteDword(string name, int value)
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegPath, true))
        {
            if (key == null) return;
            key.SetValue(name, value, RegistryValueKind.DWord);
        }
    }

    private static void WriteString(string name, string value)
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegPath, true))
        {
            if (key == null) return;
            key.SetValue(name, value ?? "", RegistryValueKind.String);
        }
    }

    private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
    private const int INTERNET_OPTION_REFRESH = 37;

    [DllImport("wininet.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
    private static extern int DnsFlushResolverCache();

    // 让 WinINET 系（Edge/Chrome/系统设置）立即感知代理与例外变化，无需重启浏览器
    private static void NotifySettingsChanged()
    {
        try
        {
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
        }
        catch
        {
        }
    }

    private static void FlushDns()
    {
        try
        {
            DnsFlushResolverCache();
        }
        catch
        {
        }
    }
}
