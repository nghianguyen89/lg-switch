using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;

namespace LgSwitch
{
    public sealed class Settings
    {
        public int Version = 1;
        public string LgId, AsusId, Snapshot;
        public string Provider = "auto", LastRequested;
    }

    internal static class Inputs
    {
        internal static bool IsKnown(string input) { return input == "dp" || input == "hdmi1" || input == "hdmi2"; }
        internal static uint StandardValue(string input)
        {
            if (input == "dp") return 0x0f;
            if (input == "hdmi1") return 0x11;
            if (input == "hdmi2") return 0x12;
            throw new ArgumentException("Unknown input.");
        }
        internal static string FromStandard(uint value) { return value == 0x0f ? "dp" : value == 0x11 ? "hdmi1" : value == 0x12 ? "hdmi2" : null; }
        internal static string Label(string input) { return input == "dp" ? "DisplayPort" : input == "hdmi1" ? "HDMI-1" : input == "hdmi2" ? "HDMI-2" : "chưa xác định"; }
    }

    internal static class ExternalTransition
    {
        internal static void Run(string input, Action<string> send, Action asusOnly)
        {
            if (input != "hdmi1" && input != "hdmi2") throw new ArgumentException("HDMI input required.");
            send(input);
            asusOnly();
        }
    }

    internal static class Files
    {
        internal static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
        internal static string Config { get { return Path.Combine(Root, "settings.xml"); } }
        internal static readonly XmlSerializer Serializer = new XmlSerializer(typeof(Settings));
        internal static Settings Load(string path)
        {
            if (new FileInfo(path).Length > 100000) throw new InvalidDataException("Settings file is too large.");
            using (var r = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
            {
                var s = (Settings)Serializer.Deserialize(r);
                if (s.Version != 1) throw new InvalidDataException("Unsupported settings version. Existing file preserved.");
                if (s.Provider != "auto" && s.Provider != "standard" && s.Provider != "intel") throw new InvalidDataException("Unknown LG control method.");
                Topology.Decode(s.Snapshot);
                return s;
            }
        }
        internal static void Save(Settings s, string path)
        {
            string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var w = new StreamWriter(temp, false, new UTF8Encoding(false))) Serializer.Serialize(w, s);
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }
        internal static void Log(string text)
        {
            try { File.AppendAllText(Path.Combine(Root, "LgSwitch.log"), DateTimeOffset.Now.ToString("o") + " " + text + Environment.NewLine, Encoding.UTF8); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static class Child
    {
        internal static void EnsureIdle() { using (Core.DdcLock()) { } }
        internal static Process Start(string args, bool redirect)
        {
            var psi = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, args) {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Files.Root,
                RedirectStandardOutput = redirect, RedirectStandardError = redirect
            };
            return Process.Start(psi);
        }
        internal static string Ddc(string id, string action, string provider = "standard")
        {
            if (provider != "standard" && provider != "intel") throw new ArgumentException("Unknown control method.");
            EnsureIdle();
            string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(id));
            using (var p = Start("--ddc " + encoded + " " + action + " " + provider, true))
            {
                // Start reading before waiting, so even a long capabilities reply cannot fill the pipe.
                var output = p.StandardOutput.ReadToEndAsync(); var error = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(8000))
                {
                    p.Kill();
                    if (!p.WaitForExit(2000))
                        throw new InvalidOperationException("DDC driver worker has not exited. Further DDC/topology changes are blocked while it owns the I/O lock. Use the joystick; ASUS stays enabled.");
                    throw new TimeoutException("DDC timed out (8 seconds). Worker exited; input outcome is unknown. ASUS stays enabled.");
                }
                string result = output.GetAwaiter().GetResult().Trim(); string detail = error.GetAwaiter().GetResult().Trim();
                if (p.ExitCode != 0) throw new InvalidOperationException("DDC: " + detail);
                return result;
            }
        }
        internal static void Dp(string id, string provider)
        {
            Exception last = null;
            for (int i = 0; i < 3; i++)
            {
                try { Ddc(id, "dp", provider); return; }
                catch (TimeoutException) { throw; }
                catch (Exception ex) { last = ex; Thread.Sleep(1000); }
            }
            throw new InvalidOperationException("Cannot send DP command after re-enabling Extend. Use LG joystick > DisplayPort. " + last.Message);
        }
    }

    // Independent process survives a UI crash and a blocked graphics call.
    internal sealed class Guard : IDisposable
    {
        internal const string ArmedMutex = "Local\\Nghia.LgSwitch.RecoveryArmed";
        readonly string token;
        readonly EventWaitHandle cancel, ready;
        readonly Process process;
        internal Guard(Settings settings)
        {
            token = Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(Path.Combine(Files.Root, "recovery"));
            Files.Save(settings, PathFor(token));
            cancel = new EventWaitHandle(false, EventResetMode.ManualReset, "Local\\LgSwitch.Cancel." + token);
            ready = new EventWaitHandle(false, EventResetMode.ManualReset, "Local\\LgSwitch.Ready." + token);
            var parent = Process.GetCurrentProcess();
            process = Child.Start("--guard " + token + " " + parent.Id + " " + parent.StartTime.ToUniversalTime().Ticks, false);
            if (!ready.WaitOne(5000))
            {
                cancel.Set(); process.Dispose(); ready.Dispose(); cancel.Dispose();
                throw new InvalidOperationException("Recovery watchdog did not start. No display change was made.");
            }
        }
        internal static string PathFor(string token)
        {
            Guid parsed;
            if (!Guid.TryParseExact(token, "N", out parsed)) throw new ArgumentException("Invalid recovery token.");
            return Path.Combine(Files.Root, "recovery", token + ".xml");
        }
        internal static void Run(string token, int parentId, long parentStart)
        {
            var settings = Files.Load(PathFor(token));
            using (var armed = new Mutex(false, ArmedMutex))
            using (var cancel = EventWaitHandle.OpenExisting("Local\\LgSwitch.Cancel." + token))
            using (var ready = EventWaitHandle.OpenExisting("Local\\LgSwitch.Ready." + token))
            {
                bool acquired;
                try { acquired = armed.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new InvalidOperationException("Another recovery watchdog is already armed.");
                try
                {
                    ready.Set();
                    if (cancel.WaitOne(90000)) return;
                    try
                    {
                        var parent = Process.GetProcessById(parentId);
                        if (parent.StartTime.ToUniversalTime().Ticks == parentStart) { parent.Kill(); parent.WaitForExit(3000); }
                    }
                    catch (ArgumentException) { }
                    using (Core.OperationLock())
                    {
                        Files.Log("WATCHDOG: restoring saved Extend and requesting DP.");
                        Core.Recover(settings);
                    }
                }
                finally { armed.ReleaseMutex(); }
            }
        }
        internal void Complete() { cancel.Set(); }
        public void Dispose()
        {
            process.WaitForExit(3000); process.Dispose(); ready.Dispose(); cancel.Dispose();
        }
    }

    internal static class Core
    {
        sealed class LockLease : IDisposable
        {
            readonly Mutex mutex;
            internal LockLease(string name)
            {
                mutex = new Mutex(false, name);
                bool acquired;
                try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) { mutex.Dispose(); throw new InvalidOperationException("Đang có lệnh màn hình hoặc phục hồi chưa kết thúc. Chờ hoàn tất rồi thử lại."); }
            }
            public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
        }
        internal static IDisposable OperationLock()
        {
            var operation = new LockLease("Local\\Nghia.LgSwitch.Operation");
            try { using (var pending = new LockLease(Guard.ArmedMutex)) { } return operation; }
            catch { operation.Dispose(); throw; }
        }
        internal static IDisposable DdcLock() { return new LockLease("Local\\Nghia.LgSwitch.DdcIO"); }

        internal static void CheckPair(Topology t, Endpoint[] endpoints, string lg, string asus)
        {
            if (endpoints.Length != 2 || lg == asus || !endpoints.Any(e => e.Id == lg) || !endpoints.Any(e => e.Id == asus))
                throw new InvalidOperationException("Start with exactly LG + ASUS in Extend. No other screens may be active.");
            var le = endpoints.Single(e => e.Id == lg); var ae = endpoints.Single(e => e.Id == asus);
            if (le.Technology != 10 || lg.IndexOf("DISPLAY#GSM", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException("Selected LG must be connected by external DisplayPort.");
            if (asus.IndexOf("DISPLAY#AUS", StringComparison.OrdinalIgnoreCase) < 0 && asus.IndexOf("DISPLAY#ACI", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException("Selected safety screen is not identified as ASUS.");
            var lp = t.Paths[le.Index]; var ap = t.Paths[ae.Index];
            if (lp.Source.Id == ap.Source.Id && lp.Source.Adapter.Low == ap.Source.Adapter.Low && lp.Source.Adapter.High == ap.Source.Adapter.High)
                throw new InvalidOperationException("Clone/Duplicate is unsupported. Select Extend first.");
        }

        internal static Settings Capture(string lg, string asus)
        {
            var t = Native.Query(); var endpoints = Native.Endpoints(t); CheckPair(t, endpoints, lg, asus);
            string caps = Child.Ddc(lg, "caps");
            if (caps.IndexOf("UP600", StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException("Capabilities did not identify UP600. No settings saved.");
            Native.Validate(t); Native.Validate(t.Only(endpoints.Single(e => e.Id == asus).Index));
            return new Settings { LgId = lg, AsusId = asus, Snapshot = t.Encode() };
        }

        internal static Settings OpenSettings()
        {
            if (File.Exists(Files.Config)) return Files.Load(Files.Config);
            var t = Native.Query(); var endpoints = Native.Endpoints(t);
            var lg = endpoints.Where(e => e.Technology == 10 && e.Id.IndexOf("DISPLAY#GSM", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            var asus = endpoints.Where(e => e.Id.IndexOf("DISPLAY#AUS", StringComparison.OrdinalIgnoreCase) >= 0 || e.Id.IndexOf("DISPLAY#ACI", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            if (lg.Length != 1 || asus.Length != 1) throw new InvalidOperationException("Để LG nhận DisplayPort và chọn Extend trong Windows một lần để app nhận đúng LG + ASUS.");
            var s = Capture(lg[0].Id, asus[0].Id); Files.Save(s, Files.Config); return s;
        }

        internal static void ResolveProvider(Settings s)
        {
            if (s.Provider != "auto") return;
            if (IsLaptop(s))
            {
                // No HMONITOR exists for inactive LG. Choose the available control path; SetInput rechecks its exact identity after Extend.
                s.Provider = File.Exists(Path.Combine(Environment.SystemDirectory, "ControlLib.dll")) ? "intel" : "standard";
                Files.Log("LG inactive; provider probes deferred until Extend. Selected path: " + s.Provider);
                Files.Save(s, Files.Config); return;
            }
            try
            {
                uint value;
                if (UInt32.TryParse(Child.Ddc(s.LgId, "read"), out value) && Inputs.FromStandard(value) != null)
                { s.Provider = "standard"; Files.Save(s, Files.Config); return; }
            }
            catch (Exception ex) { Files.Log("Standard input read: " + ex.Message); }
            try
            {
                Files.Log(Child.Ddc(s.LgId, "intel-probe", "intel"));
                s.Provider = "intel";
            }
            catch (Exception ex) { Files.Log("Intel discovery unavailable: " + ex.Message); s.Provider = "standard"; }
            Files.Save(s, Files.Config);
        }

        internal static string ReadInput(Settings s)
        {
            if (IsLaptop(s)) return null;
            uint value;
            return UInt32.TryParse(Child.Ddc(s.LgId, "read"), out value) ? Inputs.FromStandard(value) : null;
        }

        internal static Topology Saved(Settings s)
        {
            var t = Topology.Decode(s.Snapshot);
            // Device names are looked up again: stale adapter/target IDs must never select another screen.
            CheckPair(t, Native.Endpoints(t, false), s.LgId, s.AsusId);
            return t;
        }
        internal static bool IsLaptop(Settings s)
        {
            var t = Native.Query(); var endpoints = Native.Endpoints(t);
            if (endpoints.Length == 1 && endpoints[0].Id == s.AsusId) return true;
            CheckPair(t, endpoints, s.LgId, s.AsusId); return false;
        }
        internal static void Verify(Settings s, bool laptop)
        {
            if (IsLaptop(s) != laptop) throw new InvalidOperationException("Windows did not reach the requested topology. Recovery required.");
        }
        internal static void RefreshSnapshot(Settings s)
        {
            var t = Native.Query(); CheckPair(t, Native.Endpoints(t), s.LgId, s.AsusId);
            Native.Validate(t); s.Snapshot = t.Encode(); Files.Save(s, Files.Config);
        }
        internal static void RestoreTopology(Settings s)
        {
            using (DdcLock()) Native.Apply(Saved(s));
            Thread.Sleep(1500); Verify(s, false);
        }
        internal static void Recover(Settings s)
        {
            RestoreTopology(s);
            ResolveProvider(s);
            Child.Dp(s.LgId, s.Provider);
            Files.Log("Extend verified; DP write accepted (physical image not independently verified).");
        }

        internal static void ToExternal(Settings s, string input)
        {
            if (input != "hdmi1" && input != "hdmi2") throw new ArgumentException("HDMI input required.");
            // Switching between HDMI ports also needs a live DP control path on the NUC.
            if (IsLaptop(s)) RestoreTopology(s);
            var saved = Saved(s); var endpoints = Native.Endpoints(saved, false);
            var only = saved.Only(endpoints.Single(e => e.Id == s.AsusId).Index);
            Native.Validate(only);
            ExternalTransition.Run(input,
                delegate(string requested) {
                    string result = Child.Ddc(s.LgId, requested, s.Provider);
                    Files.Log(requested + " driver ACK only; provider=" + s.Provider + "; " + result);
                    Thread.Sleep(1200);
                },
                delegate { using (DdcLock()) Native.Apply(only); Thread.Sleep(1200); Verify(s, true); });
            Files.Log("ASUS-only verified. LG's inactive DP DDC handle may no longer exist.");
        }

        internal static void Select(Settings s, string input)
        {
            if (!Inputs.IsKnown(input)) throw new ArgumentException("Unknown input.");
            if (s.Provider != "standard" && s.Provider != "intel") throw new InvalidOperationException("Chưa chọn được cách điều khiển LG. Bấm Đọc lại hoặc mở Tuỳ chọn.");
            if (s.Provider == "intel" && !IntelDdc.IsAdministrator) throw new UnauthorizedAccessException("Windows chưa cấp quyền điều khiển LG qua Intel. Chưa đổi màn hình.");
            using (OperationLock())
            {
                if (!IsLaptop(s)) RefreshSnapshot(s);
                using (var guard = new Guard(s))
                {
                    try
                    {
                        if (input == "dp") Recover(s); else ToExternal(s, input);
                        s.LastRequested = input; Files.Save(s, Files.Config);
                        Files.Log("Requested input sent; Windows topology verified: " + input);
                        guard.Complete();
                    }
                    catch (Exception original)
                    {
                        s.LastRequested = null;
                        try { Files.Save(s, Files.Config); } catch (Exception ex) { Files.Log("Cannot save cleared input request: " + ex.Message); }
                        try { Recover(s); } catch (Exception recovery) { throw new InvalidOperationException("Chưa phục hồi được LG; watchdog vẫn chờ thử lại sau 90 giây. Dùng joystick chọn DisplayPort nếu cần. " + recovery.Message, original); }
                        guard.Complete();
                        throw;
                    }
                }
            }
        }

        internal static string Diagnose()
        {
            var b = new StringBuilder(); var t = Native.Query(); var endpoints = Native.Endpoints(t);
            b.AppendLine("LgSwitch 1.0.1 read-only diagnostics - " + DateTimeOffset.Now.ToString("o"));
            b.AppendLine("No input or topology changes. SetDisplayConfig is used only with SDC_VALIDATE.");
            b.AppendLine("Active paths: " + t.Paths.Length + "; modes: " + t.Modes.Length);
            foreach (var e in endpoints)
            {
                b.AppendLine(e.ToString()); b.AppendLine("Device identity: " + e.Id);
                if (e.Id.IndexOf("DISPLAY#GSM", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    foreach (string action in new[] { "read", "brightness", "caps" })
                    {
                        try { b.AppendLine(action + ": " + Child.Ddc(e.Id, action)); }
                        catch (Exception ex) { b.AppendLine(action + ": FAILED - " + ex.Message); }
                    }
                    try { b.AppendLine("Intel discovery: " + Child.Ddc(e.Id, "intel-probe", "intel")); }
                    catch (Exception ex) { b.AppendLine("Intel discovery: FAILED - " + ex.Message); }
                    if (IntelDdc.IsAdministrator)
                    {
                        try { b.AppendLine("Intel GET-only transport check: " + Child.Ddc(e.Id, "intel-transport", "intel")); }
                        catch (Exception ex) { b.AppendLine("Intel GET-only transport check: FAILED - " + ex.Message); }
                    }
                    else b.AppendLine("Intel GET-only transport check: NOT RUN (requires administrator; no input SET needed).");
                }
            }
            b.AppendLine("Intel IGCL runtime: " + File.Exists(Path.Combine(Environment.SystemDirectory, "ControlLib.dll")));
            b.AppendLine("VCP 0x60 value 0 means unknown here, not verified DP/HDMI.");
            var lg = endpoints.Where(e => e.Id.IndexOf("DISPLAY#GSM", StringComparison.OrdinalIgnoreCase) >= 0 && e.Technology == 10).ToArray();
            var asus = endpoints.Where(e => e.Id.IndexOf("DISPLAY#AUS", StringComparison.OrdinalIgnoreCase) >= 0 || e.Id.IndexOf("DISPLAY#ACI", StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            if (lg.Length == 1 && asus.Length == 1)
            {
                try
                {
                    CheckPair(t, endpoints, lg[0].Id, asus[0].Id);
                    Native.Validate(t); Native.Validate(t.Only(asus[0].Index));
                    b.AppendLine("SDC_VALIDATE: saved Extend and exact ASUS-only configuration PASS (no apply).");
                }
                catch (Exception ex) { b.AppendLine("SDC_VALIDATE: FAIL - " + ex.Message); }
            }
            return b.ToString();
        }
    }
}
