using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("宝塔网络代理修复")]
[assembly: System.Reflection.AssemblyProduct("宝塔网络代理修复")]
[assembly: System.Reflection.AssemblyDescription("梯子开启时自动将宝塔面板地址加入 Windows 代理例外")]
[assembly: System.Reflection.AssemblyCompany("")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace BaoTaProxyFix
{
    /// <summary>
    /// 宝塔网络代理修复
    /// 常驻守护：当系统代理（梯子）开启时，自动把宝塔面板地址加入 Windows 代理例外，
    /// 保证「梯子开着 + 面板直连」同时成立。
    /// 事件驱动（RegNotifyChangeKeyValue），空闲时 CPU 为 0。
    /// </summary>
    internal static class Program
    {
        private const string AppName = "宝塔网络代理修复";
        private const string RegPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        private const string MutexName = @"Local\BaoTaProxyFix.SingleInstance";
        private const int RegNotifyChangeLastSet = 0x00000004;
        private const int RegNotifyChangeName = 0x00000001;
        private const int LogMaxBytes = 128 * 1024;
        private const int FallbackWaitMs = 60000;

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegNotifyChangeKeyValue(IntPtr hKey, bool watchSubtree,
            int notifyFilter, IntPtr hEvent, bool asynchronous);

        private static string _baseDir;
        private static string _logFile;
        private static string _configFile;
        private static readonly List<string> Hosts = new List<string>();
        private static bool _keepLocal = true;
        private static string _lastState = "";
        private static readonly object LogLock = new object();

        [STAThread]
        private static int Main(string[] args)
        {
            bool createdNew;
            using (var mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew) return 0; // 已有实例在跑，静默退出

                try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Idle; } catch { }

                _baseDir = AppDomain.CurrentDomain.BaseDirectory;
                _logFile = Path.Combine(_baseDir, "run.log");
                _configFile = Path.Combine(_baseDir, "config.ini");

                Log("start, pid=" + Process.GetCurrentProcess().Id);
                ReloadConfig();
                EnsureBypass("startup");
                WatchLoop();
            }
            return 0;
        }

        private static void WatchLoop()
        {
            while (true)
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(RegPath, false))
                    {
                        if (key == null)
                        {
                            Thread.Sleep(5000);
                            continue;
                        }

                        using (var signal = new ManualResetEvent(false))
                        {
                            int rc = RegNotifyChangeKeyValue(
                                key.Handle.DangerousGetHandle(),
                                false,
                                RegNotifyChangeLastSet | RegNotifyChangeName,
                                signal.SafeWaitHandle.DangerousGetHandle(),
                                true);

                            if (rc != 0)
                            {
                                // 无法注册通知（少见），退化为兜底轮询
                                Thread.Sleep(5000);
                            }
                            else
                            {
                                signal.WaitOne(FallbackWaitMs);
                            }
                        }
                    }
                }
                catch
                {
                    Thread.Sleep(5000);
                }

                ReloadConfig();
                EnsureBypass("change");
            }
        }

        private static void ReloadConfig()
        {
            try
            {
                if (!File.Exists(_configFile)) return;

                var hosts = new List<string>();
                bool keepLocal = true;

                foreach (var raw in File.ReadAllLines(_configFile, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    string k = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string v = line.Substring(eq + 1).Trim();

                    if (k == "hosts")
                    {
                        foreach (var h in v.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            string t = h.Trim();
                            if (t.Length > 0 && !hosts.Exists(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase)))
                                hosts.Add(t);
                        }
                    }
                    else if (k == "keep_local")
                    {
                        keepLocal = !(v == "0" || string.Equals(v, "false", StringComparison.OrdinalIgnoreCase));
                    }
                }

                lock (Hosts)
                {
                    Hosts.Clear();
                    Hosts.AddRange(hosts);
                    _keepLocal = keepLocal;
                }
            }
            catch (Exception ex)
            {
                Log("config error: " + ex.Message);
            }
        }

        private static void EnsureBypass(string reason)
        {
            try
            {
                List<string> hosts;
                bool keepLocal;
                lock (Hosts)
                {
                    hosts = new List<string>(Hosts);
                    keepLocal = _keepLocal;
                }
                if (hosts.Count == 0) return;

                using (var key = Registry.CurrentUser.OpenSubKey(RegPath, true))
                {
                    if (key == null) return;

                    int enable = ToInt(key.GetValue("ProxyEnable", 0));
                    string server = (key.GetValue("ProxyServer", "") as string) ?? "";
                    string over = (key.GetValue("ProxyOverride", "") as string) ?? "";

                    if (enable != 1 || server.Trim().Length == 0)
                    {
                        SetState("proxy_off|" + server);
                        return;
                    }

                    var parts = new List<string>();
                    foreach (var p in over.Split(';'))
                    {
                        string t = p.Trim();
                        if (t.Length > 0) parts.Add(t);
                    }

                    bool changed = false;
                    foreach (var h in hosts)
                    {
                        if (!parts.Exists(x => string.Equals(x, h, StringComparison.OrdinalIgnoreCase)))
                        {
                            parts.Add(h);
                            changed = true;
                        }
                    }
                    if (keepLocal && !parts.Exists(x => string.Equals(x, "<local>", StringComparison.OrdinalIgnoreCase)))
                    {
                        parts.Add("<local>");
                        changed = true;
                    }

                    if (!changed)
                    {
                        SetState("ready|" + server);
                        return;
                    }

                    string updated = string.Join(";", parts);
                    key.SetValue("ProxyOverride", updated, RegistryValueKind.String);
                    Log("bypass updated (" + reason + ") -> " + updated);
                    SetState("ready|" + server);
                }
            }
            catch (Exception ex)
            {
                Log("ensure error: " + ex.Message);
            }
        }

        private static int ToInt(object o)
        {
            if (o is int) return (int)o;
            int v;
            return int.TryParse(Convert.ToString(o), out v) ? v : 0;
        }

        private static void SetState(string state)
        {
            if (string.Equals(state, _lastState, StringComparison.Ordinal)) return;
            _lastState = state;
            Log("state: " + state);
        }

        private static void Log(string message)
        {
            try
            {
                lock (LogLock)
                {
                    Directory.CreateDirectory(_baseDir);
                    var fi = new FileInfo(_logFile);
                    if (fi.Exists && fi.Length > LogMaxBytes)
                    {
                        string old = _logFile + ".old";
                        try { if (File.Exists(old)) File.Delete(old); } catch { }
                        try { File.Move(_logFile, old); } catch { }
                    }
                    File.AppendAllText(_logFile,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + message + Environment.NewLine,
                        Encoding.UTF8);
                }
            }
            catch { }
        }
    }
}
