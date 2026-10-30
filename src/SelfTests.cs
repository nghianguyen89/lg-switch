using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace LgSwitch
{
    internal static class SelfTests
    {
        static int count;
        static void Assert(bool value, string name)
        { if (!value) throw new Exception("FAIL: " + name); count++; }
        static void Throws(Action action, string name)
        {
            bool rejected = false;
            try { action(); } catch (Exception) { rejected = true; }
            Assert(rejected, name);
        }
        static void PaintIsolation(Control button, int state)
        {
            button.Enabled = state != 2;
            if (state == 1) button.GetType().GetMethod("OnMouseEnter", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button, new object[] { EventArgs.Empty });
            var clip = state == 3 ? new Rectangle(10, 5, button.Width / 2, button.Height / 2) : button.ClientRectangle;
            var deviceClip = clip; deviceClip.Offset(35, 25);
            using (var bitmap = new Bitmap(button.Width + 80, button.Height + 70))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Magenta); graphics.TranslateTransform(35, 25); graphics.SetClip(clip);
                button.GetType().GetMethod("OnPaint", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(button, new object[] { new PaintEventArgs(graphics, clip) });
                bool isolated = true;
                for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++)
                {
                    bool untouched = bitmap.GetPixel(x, y).ToArgb() == Color.Magenta.ToArgb();
                    if (deviceClip.Contains(x, y) ? untouched : !untouched) isolated = false;
                }
                Assert(isolated, button.GetType().Name + " repaints its full clipped area without changing parent pixels, state=" + state);
            }
        }
        internal static Topology Fixture()
        {
            var t = new Topology { Paths = new DisplayPath[2], Modes = new DisplayMode[4] };
            for (int i = 0; i < 2; i++)
            {
                t.Paths[i] = new DisplayPath { Flags = 1, Source = new SourceInfo { Id = (uint)i, ModeIndex = (uint)(i * 2) }, Target = new TargetInfo { Id = (uint)(10 + i), ModeIndex = (uint)(i * 2 + 1), Technology = i == 0 ? 10u : 5u, Available = 1 } };
                t.Modes[i * 2] = new DisplayMode { Type = 1, Id = (uint)i, Data = new byte[48] };
                t.Modes[i * 2 + 1] = new DisplayMode { Type = 2, Id = (uint)(10 + i), Data = new byte[48] };
                Array.Copy(BitConverter.GetBytes(1920), 0, t.Modes[i * 2].Data, 0, 4);
                Array.Copy(BitConverter.GetBytes(1080), 0, t.Modes[i * 2].Data, 4, 4);
                Array.Copy(BitConverter.GetBytes(i * 1920), 0, t.Modes[i * 2].Data, 12, 4);
            }
            return t;
        }
        internal static string Run()
        {
            count = 0;
            Assert(Marshal.SizeOf(typeof(Luid)) == 8, "LUID ABI");
            Assert(Marshal.SizeOf(typeof(SourceInfo)) == 20, "source ABI");
            Assert(Marshal.SizeOf(typeof(TargetInfo)) == 48, "target ABI");
            Assert(Marshal.SizeOf(typeof(DisplayPath)) == 72, "path ABI");
            Assert(Marshal.SizeOf(typeof(DisplayMode)) == 64, "mode ABI");
            Assert(Marshal.SizeOf(typeof(TargetName)) == 420, "target-name ABI");
            Assert(Marshal.SizeOf(typeof(SourceName)) == 84, "source-name ABI");
            var original = Fixture(); string saved = original.Encode();
            var restored = Topology.Decode(saved);
            Assert(restored.Encode() == saved, "snapshot roundtrip preserves native modes and indexes");
            var one = original.Only(1);
            Assert(one.Paths.Length == 1 && one.Paths[0].Target.Id == 11, "ASUS retained and LG removed regardless of monitor number");
            Assert(one.Paths[0].Source.ModeIndex == 2 && one.Modes.Length == 4, "mode index table preserved after path filter");
            Assert(BitConverter.ToInt32(one.Modes[2].Data, 12) == 0 && BitConverter.ToInt32(one.Modes[2].Data, 16) == 0, "ASUS-only has desktop origin");
            Assert(BitConverter.ToInt32(original.Modes[2].Data, 12) == 1920 && original.Encode() == saved, "ASUS-only cannot mutate recovery snapshot");
            Throws(delegate { Topology.Decode("bad snapshot"); }, "corrupt snapshot rejected");
            Throws(delegate { Topology.Decode(Convert.ToBase64String(new byte[12])); }, "unknown snapshot version rejected");
            var invalid = Fixture(); invalid.Paths[0].Source.ModeIndex = 100;
            Throws(delegate { Topology.Decode(invalid.Encode()); }, "out-of-range snapshot mode index rejected");
            invalid = Fixture(); invalid.Modes[2].Type = 2;
            Throws(delegate { invalid.Only(1); }, "target mode cannot be used as ASUS source");
            var endpoints = new[] {
                new Endpoint { Id = "DISPLAY#GSM_TEST", Index = 0, Technology = 10 },
                new Endpoint { Id = "DISPLAY#AUS_TEST", Index = 1, Technology = 5 }
            };
            Core.CheckPair(original, endpoints, endpoints[0].Id, endpoints[1].Id); count++;
            Throws(delegate { Core.CheckPair(original, endpoints, endpoints[0].Id, endpoints[0].Id); }, "same monitor cannot be LG and safety screen");
            var clone = Fixture(); clone.Paths[1].Source.Id = clone.Paths[0].Source.Id;
            Throws(delegate { Core.CheckPair(clone, endpoints, endpoints[0].Id, endpoints[1].Id); }, "clone topology rejected");
            Throws(delegate { Core.CheckPair(original, endpoints.Take(1).ToArray(), endpoints[0].Id, endpoints[1].Id); }, "missing safety screen rejected");
            Throws(delegate { Guard.PathFor("..\\settings"); }, "recovery file traversal rejected");
            using (var ready = new ManualResetEvent(false)) using (var release = new ManualResetEvent(false))
            {
                var blocked = Task.Run(delegate { using (Core.DdcLock()) { ready.Set(); release.WaitOne(5000); } });
                try
                {
                    Assert(ready.WaitOne(2000), "DDC lock owner ready");
                    Throws(Child.EnsureIdle, "unresolved DDC blocks further I/O and topology changes");
                }
                finally { release.Set(); blocked.Wait(); }
                Child.EnsureIdle(); count++;
            }
            Assert(Inputs.IsKnown("dp") && Inputs.IsKnown("hdmi1") && Inputs.IsKnown("hdmi2") && !Inputs.IsKnown("hdmi3"), "only three requested inputs allowed");
            Assert(Inputs.StandardValue("dp") == 15, "standard DP value");
            Assert(Inputs.StandardValue("hdmi1") == 17, "standard HDMI1 value");
            Assert(Inputs.StandardValue("hdmi2") == 18, "standard HDMI2 value");
            Assert(Inputs.FromStandard(0) == null && Inputs.FromStandard(255) == null, "unknown input never defaults to DP");
            Assert(Inputs.FromStandard(15) == "dp" && Inputs.FromStandard(17) == "hdmi1" && Inputs.FromStandard(18) == "hdmi2", "recognized input readback");
            Throws(delegate { Inputs.StandardValue("hdmi3"); }, "unknown input rejected before hardware changes");
            using (var legacy = new StringReader("<Settings><Version>1</Version><Provider>intel</Provider><LastRequested>dp</LastRequested><ConfirmedHdmi1>true</ConfirmedHdmi1></Settings>"))
            {
                var settings = (Settings)Files.Serializer.Deserialize(legacy);
                Assert(settings.Provider == "intel" && settings.LastRequested == "dp", "0.3 settings remain readable after confirmation flags removed");
            }
            using (var ready = new ManualResetEvent(false)) using (var release = new ManualResetEvent(false))
            {
                var pending = Task.Run(delegate { using (var marker = new Mutex(false, Guard.ArmedMutex)) { marker.WaitOne(); try { ready.Set(); release.WaitOne(5000); } finally { marker.ReleaseMutex(); } } });
                try
                {
                    Assert(ready.WaitOne(2000), "pending recovery marker ready");
                    Throws(delegate { using (Core.OperationLock()) { } }, "pending watchdog blocks a new switch");
                }
                finally { release.Set(); pending.Wait(); }
                using (Core.OperationLock()) { count++; }
            }
            var sequence = new List<string>(); bool disabled = false;
            Action<string> send = delegate(string input) { sequence.Add("send:" + input); };
            Action only = delegate { sequence.Add("only"); disabled = true; };
            Throws(delegate { ExternalTransition.Run("hdmi1", delegate { throw new IOException("driver failure"); }, only); }, "failed write cannot proceed to topology change");
            Assert(!disabled, "write failure preserves LG desktop path");
            Throws(delegate { ExternalTransition.Run("dp", send, only); }, "DP cannot use HDMI-only transition");
            Assert(sequence.Count == 0 && !disabled, "invalid external input has no I/O side effects");
            sequence.Clear();
            ExternalTransition.Run("hdmi2", send, only);
            Assert(disabled && String.Join(",", sequence) == "send:hdmi2,only", "HDMI2 request precedes ASUS-only without a popup");
            sequence.Clear();
            ExternalTransition.Run("hdmi1", send, only);
            Assert(disabled && String.Join(",", sequence) == "send:hdmi1,only", "HDMI1 request precedes ASUS-only without a popup");
            IntelDdc.SelfTest(); count++;
            if (!SystemInformation.HighContrast)
            {
                for (int state = 0; state < 4; state++)
                {
                    using (var button = new QuietButton { Text = "Làm mới", Size = new Size(120, 40) }) PaintIsolation(button, state);
                    using (var button = new SourceButton { Heading = "DisplayPort", Caption = "NUC · LG + ASUS", InputKey = "dp", Size = new Size(210, 168) }) PaintIsolation(button, state);
                }
                using (var bitmap = new Bitmap(200, 120)) using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Magenta); graphics.TranslateTransform(40, 30); graphics.SetClip(new Rectangle(0, 0, 120, 50));
                    UiTheme.Focus(graphics, new Rectangle(0, 0, 120, 50), UiTheme.Text, 1);
                    Assert(bitmap.GetPixel(0, 0).ToArgb() == Color.Magenta.ToArgb(), "keyboard focus respects translated graphics clipping");
                }
            }
            return "PASS: " + count + " self-checks, including Intel ABI/packet/mapping checks. No monitor/API mutations.\r\nHardware switching, watchdog timeout recovery and UI appearance are not covered.\r\n";
        }
        internal static string Watchdog()
        {
            // Synthetic snapshot never reaches Recover: only the start/ready/cancel path is exercised.
            using (var g = new Guard(new Settings { LgId = "test-lg", AsusId = "test-asus", Snapshot = Fixture().Encode() })) { g.Complete(); }
            return "PASS: independent watchdog starts, signals ready, and accepts cancel. No screen or input changes.\r\nTimeout/crash restoration NOT RUN.\r\n";
        }
    }
}
