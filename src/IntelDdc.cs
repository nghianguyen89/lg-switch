using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;

namespace LgSwitch
{
    // IGCL v1.0 x64 ABI: intel/drivers.gpu.control-library, includes/igcl_api.h (Code_Ver1.0).
    // Intel's Generic_Sample identifies pDeviceID as LUID and WindowsDisplayEncoderID as target ID.
    internal static class IntelDdc
    {
        [StructLayout(LayoutKind.Sequential)] struct InitArgs
        {
            public uint Size; public byte Version; public uint AppVersion, Flags, SupportedVersion;
            public Guid ApplicationUID;
        }
        // Unused fields retain their native ABI space; no packed byte-array union (alignment is 8).
        [StructLayout(LayoutKind.Explicit, Size = 320)] struct AdapterProperties
        {
            [FieldOffset(0)] public uint Size;
            [FieldOffset(4)] public byte Version;
            [FieldOffset(8)] public IntPtr DeviceId;
            [FieldOffset(16)] public uint DeviceIdSize;
            [FieldOffset(20)] public uint DeviceType;
            [FieldOffset(64)] public uint Vendor;
        }
        [StructLayout(LayoutKind.Explicit, Size = 200)] struct DisplayProperties
        {
            [FieldOffset(0)] public uint Size;
            [FieldOffset(4)] public byte Version;
            [FieldOffset(8)] public uint TargetId;
            [FieldOffset(24)] public uint Type;
            [FieldOffset(32)] public uint ConverterOutput;
            [FieldOffset(44)] public uint ConverterType;
            [FieldOffset(48)] public uint ConfigFlags;
            [FieldOffset(72)] public uint TimingSize;
        }
        [StructLayout(LayoutKind.Explicit, Size = 112)] struct EncoderProperties
        {
            [FieldOffset(0)] public uint Size;
            [FieldOffset(4)] public byte Version;
            [FieldOffset(8)] public uint TargetId;
            [FieldOffset(24)] public uint Type;
            [FieldOffset(36)] public uint ConfigFlags;
        }
        [StructLayout(LayoutKind.Sequential)] struct I2cArgs
        {
            public uint Size; public byte Version;
            public uint DataSize, Address, Operation, Offset, Flags;
            public ulong Rad;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)] public byte[] Data;
        }
        [StructLayout(LayoutKind.Sequential)] struct AuxArgs
        {
            public uint Size; public byte Version;
            public uint Operation, Flags, Address;
            public ulong Rad;
            public uint PortId, DataSize;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 132)] public byte[] Data;
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint Init(ref InitArgs args, out IntPtr api);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint Close(IntPtr api);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint Enumerate(IntPtr owner, ref uint count, [Out] IntPtr[] handles);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint GetAdapter(IntPtr adapter, ref AdapterProperties props);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint GetDisplay(IntPtr output, ref DisplayProperties props);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint GetEncoder(IntPtr output, ref EncoderProperties props);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint I2c(IntPtr output, ref I2cArgs args);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate uint Aux(IntPtr output, ref AuxArgs args);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)] static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);

        internal static bool IsAdministrator
        {
            get { using (var id = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }
        }
        static void Check(uint result, string call)
        {
            if (result == 0) return;
            string detail;
            switch (result)
            {
                case 0x40000003: detail = "device lost or graphics driver reset"; break;
                case 0x40000006: detail = "insufficient permissions"; break;
                case 0x40000009: detail = "driver does not support this IGCL structure/API version"; break;
                case 0x4000000a: case 0x40000015: detail = "feature is unsupported by this driver"; break;
                case 0x4000000f: detail = "driver rejected the structure/data size"; break;
                case 0x40000017: detail = "Intel kernel driver call failed"; break;
                default: detail = "driver returned an error"; break;
            }
            throw new InvalidOperationException(call + " failed: " + detail + " (IGCL 0x" + result.ToString("X8") + "). No fallback output ordinal will be used.");
        }
        sealed class Session : IDisposable
        {
            IntPtr library;
            internal IntPtr Api;
            internal Close CloseApi;
            internal Enumerate Devices, Outputs;
            internal GetAdapter Adapter;
            internal GetDisplay Display;
            internal GetEncoder Encoder;
            internal I2c Access;
            internal Aux AuxAccess;
            T Function<T>(string name)
            {
                IntPtr address = GetProcAddress(library, name);
                if (address == IntPtr.Zero) throw new EntryPointNotFoundException("System32 ControlLib.dll does not export " + name + ".");
                return (T)(object)Marshal.GetDelegateForFunctionPointer(address, typeof(T));
            }
            internal Session()
            {
                SelfTest();
                string path = Path.Combine(Environment.SystemDirectory, "ControlLib.dll");
                // Restrict the root DLL and its dependencies to its System32 directory and System32.
                library = LoadLibraryEx(path, IntPtr.Zero, 0x100 | 0x800);
                if (library == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot load Intel IGCL from " + path);
                try
                {
                    var init = Function<Init>("ctlInit"); CloseApi = Function<Close>("ctlClose");
                    Devices = Function<Enumerate>("ctlEnumerateDevices"); Outputs = Function<Enumerate>("ctlEnumerateDisplayOutputs");
                    Adapter = Function<GetAdapter>("ctlGetDeviceProperties"); Display = Function<GetDisplay>("ctlGetDisplayProperties");
                    Encoder = Function<GetEncoder>("ctlGetAdaperDisplayEncoderProperties"); Access = Function<I2c>("ctlI2CAccess");
                    IntPtr aux = GetProcAddress(library, "ctlAUXAccess");
                    if (aux != IntPtr.Zero) AuxAccess = (Aux)Marshal.GetDelegateForFunctionPointer(aux, typeof(Aux));
                    var args = new InitArgs { Size = 36, AppVersion = 0x10000 };
                    Check(init(ref args, out Api), "ctlInit (API 1.0)");
                    if (Api == IntPtr.Zero) throw new InvalidOperationException("IGCL returned a null API handle.");
                }
                catch { Dispose(); throw; }
            }
            public void Dispose()
            {
                if (Api != IntPtr.Zero) { CloseApi(Api); Api = IntPtr.Zero; }
                if (library != IntPtr.Zero) { FreeLibrary(library); library = IntPtr.Zero; }
            }
        }
        static IntPtr[] Handles(Enumerate enumerate, IntPtr owner, string name)
        {
            uint count = 0; Check(enumerate(owner, ref count, null), name + " count");
            if (count > 256) throw new InvalidOperationException(name + " returned an invalid count.");
            if (count == 0) return new IntPtr[0];
            var handles = new IntPtr[count]; uint capacity = count;
            Check(enumerate(owner, ref count, handles), name);
            if (count > capacity || count == 0) throw new InvalidOperationException(name + ": enumeration changed; retry.");
            Array.Resize(ref handles, (int)count);
            if (handles.Any(h => h == IntPtr.Zero) || handles.Distinct().Count() != handles.Length)
                throw new InvalidOperationException(name + " returned null or duplicate handles.");
            return handles;
        }
        static TargetInfo Target(string stableId)
        {
            if (String.IsNullOrWhiteSpace(stableId) || stableId.IndexOf("DISPLAY#GSM", StringComparison.OrdinalIgnoreCase) < 0)
                throw new ArgumentException("An exact stable LG monitor device path is required.");
            var topology = Native.Query();
            var matches = Native.Endpoints(topology).Where(e => String.Equals(e.Id, stableId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1 || matches[0].Technology != 10)
                throw new InvalidOperationException("LG must be one unique active external DP target. Enable Extend and retry.");
            return topology.Paths[matches[0].Index].Target;
        }
        static bool Matches(Luid adapter, uint target, TargetInfo expected)
        { return adapter.Low == expected.Adapter.Low && adapter.High == expected.Adapter.High && target == expected.Id && expected.Technology == 10; }
        static IntPtr UniqueOutput(List<IntPtr> matches)
        {
            if (matches.Count != 1) throw new InvalidOperationException("Expected exactly one IGCL output matching LG's adapter LUID and Windows target ID; found " + matches.Count + ". Refusing I2C.");
            return matches[0];
        }
        static IntPtr Find(Session session, TargetInfo expected, StringBuilder log)
        {
            var matches = new List<IntPtr>();
            foreach (var adapter in Handles(session.Devices, session.Api, "ctlEnumerateDevices"))
            {
                IntPtr id = Marshal.AllocHGlobal(8);
                try
                {
                    Marshal.WriteInt64(id, 0);
                    var a = new AdapterProperties { Size = 320, DeviceId = id, DeviceIdSize = 8 };
                    Check(session.Adapter(adapter, ref a), "ctlGetDeviceProperties");
                    if (a.DeviceId != id || a.DeviceIdSize != 8) throw new InvalidOperationException("IGCL changed the LUID buffer contract; refusing I2C.");
                    var luid = (Luid)Marshal.PtrToStructure(id, typeof(Luid));
                    log.AppendLine("IGCL adapter LUID: " + luid.High.ToString("X8") + ":" + luid.Low.ToString("X8") + "; vendor=0x" + a.Vendor.ToString("X4"));
                    if (a.DeviceType != 1 || a.Vendor != 0x8086 || luid.Low != expected.Adapter.Low || luid.High != expected.Adapter.High) continue;
                    foreach (var output in Handles(session.Outputs, adapter, "ctlEnumerateDisplayOutputs"))
                    {
                        var d = new DisplayProperties { Size = 200, TimingSize = 64 };
                        Check(session.Display(output, ref d), "ctlGetDisplayProperties");
                        log.AppendLine("IGCL target=" + d.TargetId + "; type=" + d.Type + "; flags=0x" + d.ConfigFlags.ToString("X"));
                        if (!Matches(luid, d.TargetId, expected)) continue;
                        var e = new EncoderProperties { Size = 112 };
                        Check(session.Encoder(output, ref e), "ctlGetAdaperDisplayEncoderProperties");
                        // This driver repeats DP in ConverterOutput even when ConverterType says no converter.
                        if (d.Type != 1 || (d.ConfigFlags & 3) != 3 || d.ConverterType != 0 ||
                            e.TargetId != d.TargetId || e.Type != 1 || (e.ConfigFlags & (1 | 2 | 32 | 64 | 128 | 256)) != 0)
                            throw new InvalidOperationException("LG's exact IGCL target is not active direct external DP (or is tiled/virtual/hidden). Refusing I2C. Display type=" + d.Type + ", flags=0x" + d.ConfigFlags.ToString("X") + ", converter=" + d.ConverterType + "/" + d.ConverterOutput + "; encoder target=" + e.TargetId + ", type=" + e.Type + ", flags=0x" + e.ConfigFlags.ToString("X"));
                        matches.Add(output);
                    }
                }
                finally { Marshal.FreeHGlobal(id); }
            }
            return UniqueOutput(matches);
        }
        internal static string Diagnose(string stableLgId)
        {
            var target = Target(stableLgId);
            var log = new StringBuilder("Intel IGCL discovery only; no I2C calls.\r\n");
            log.AppendLine("Administrator: " + IsAdministrator + "; Windows target=" + target.Id);
            using (var session = new Session()) Find(session, target, log);
            log.AppendLine("Exact LG LUID + target ID mapping PASS. Input support still requires a supervised test.");
            return log.ToString();
        }
        static byte Value(string action)
        {
            switch (action) { case "dp": return 0xD0; case "hdmi1": return 0x90; case "hdmi2": return 0x91; default: throw new ArgumentException("Intel LG input must be dp, hdmi1 or hdmi2."); }
        }
        static I2cArgs Packet(byte value)
        {
            var bytes = new byte[128];
            bytes[0] = 0x84; bytes[1] = 0x03; bytes[2] = 0xF4; bytes[3] = 0; bytes[4] = value;
            byte checksum = 0x6E ^ 0x50;
            for (int i = 0; i < 5; i++) checksum ^= bytes[i];
            bytes[5] = checksum;
            return new I2cArgs { Size = 168, DataSize = 6, Address = 0x6E, Operation = 2, Offset = 0x50, Data = bytes };
        }
        static AuxArgs AuxPacket(byte value)
        {
            var packet = Packet(value); var bytes = new byte[132];
            bytes[0] = 0x50; Array.Copy(packet.Data, 0, bytes, 1, 6);
            return new AuxArgs { Size = 176, Operation = 2, Flags = 2, Address = 0x6E, DataSize = 7, Data = bytes };
        }
        sealed class Brightness
        {
            internal uint Current, Maximum;
            public override string ToString() { return Current + "/" + Maximum; }
        }
        static Brightness ParseBrightness(byte[] bytes, uint size)
        {
            if (size != 11 || bytes == null || bytes.Length < 11) throw new InvalidDataException("brightness reply length must be 11");
            if (bytes[0] != 0x6E || bytes[1] != 0x88 || bytes[2] != 2 || bytes[4] != 0x10)
                throw new InvalidDataException("wrong brightness reply envelope/opcode/feature");
            byte checksum = 0x50; for (int i = 0; i < 11; i++) checksum ^= bytes[i];
            if (checksum != 0) throw new InvalidDataException("brightness reply checksum mismatch");
            if (bytes[3] == 1) throw new InvalidDataException("monitor reports brightness unsupported");
            if (bytes[3] != 0 || bytes[5] != 0) throw new InvalidDataException("invalid brightness result/type");
            var result = new Brightness { Maximum = (uint)((bytes[6] << 8) | bytes[7]), Current = (uint)((bytes[8] << 8) | bytes[9]) };
            if (result.Maximum == 0 || result.Current > result.Maximum) throw new InvalidDataException("invalid brightness range");
            return result;
        }
        static Brightness Baseline(string id, StringBuilder log)
        {
            try
            {
                string text = Native.Ddc(id, "brightness"); uint current, maximum;
                string[] parts = text.Trim().Split('/');
                if (parts.Length != 2 || !UInt32.TryParse(parts[0], out current) || !UInt32.TryParse(parts[1], out maximum) || maximum == 0 || current > maximum)
                    throw new InvalidDataException("unexpected Win32 brightness format: " + text);
                var result = new Brightness { Current = current, Maximum = maximum };
                log.AppendLine("Win32 brightness=" + result); return result;
            }
            catch (Exception ex) { log.AppendLine("Win32 brightness unavailable: " + ex.Message); return null; }
        }
        static Brightness Probe(Session session, IntPtr output, bool useAux, StringBuilder log)
        {
            string transport = useAux ? "AUX" : "I2C";
            if (useAux && session.AuxAccess == null) { log.AppendLine("AUX unavailable: ctlAUXAccess export missing"); return null; }
            try
            {
                uint write, read, size; byte[] data = Enumerable.Repeat((byte)0xFF, useAux ? 132 : 128).ToArray();
                if (useAux)
                {
                    // AUX raw framing is inferred from Intel's EDID sample; require a validated GET before SET.
                    var args = new AuxArgs { Size = 176, Operation = 2, Flags = 2, Address = 0x6E, DataSize = 5, Data = new byte[132] };
                    Array.Copy(new byte[] { 0x51, 0x82, 0x01, 0x10, 0xAC }, args.Data, 5);
                    write = session.AuxAccess(output, ref args); log.AppendLine("AUX GET request status=0x" + write.ToString("X8"));
                    Check(write, "AUX brightness GET request"); Thread.Sleep(200);
                    args = new AuxArgs { Size = 176, Operation = 1, Flags = 2, Address = 0x6E, DataSize = 11, Data = data };
                    read = session.AuxAccess(output, ref args); size = args.DataSize; data = args.Data;
                }
                else
                {
                    var args = new I2cArgs { Size = 168, Operation = 2, Address = 0x6E, Offset = 0x51, DataSize = 4, Data = new byte[128] };
                    Array.Copy(new byte[] { 0x82, 0x01, 0x10, 0xAC }, args.Data, 4);
                    write = session.Access(output, ref args); log.AppendLine("I2C GET request status=0x" + write.ToString("X8"));
                    Check(write, "I2C brightness GET request"); Thread.Sleep(200);
                    args = new I2cArgs { Size = 168, Operation = 1, Address = 0x6E, Offset = 0x51, DataSize = 11, Data = data };
                    read = session.Access(output, ref args); size = args.DataSize; data = args.Data;
                }
                log.AppendLine(transport + " GET reply status=0x" + read.ToString("X8") + "; size=" + size + "; raw=" + BitConverter.ToString(data, 0, (int)Math.Min(size, (uint)data.Length)));
                Check(read, transport + " brightness GET reply");
                var result = ParseBrightness(data, size); log.AppendLine(transport + " brightness validated=" + result); return result;
            }
            catch (Exception ex) { log.AppendLine(transport + " brightness FAILED: " + ex.Message); return null; }
        }
        static bool SameBrightness(Brightness first, Brightness second)
        { return first.Current == second.Current && first.Maximum == second.Maximum; }
        static void RequireAgreement(Brightness baseline, Brightness raw)
        {
            if (baseline == null) throw new InvalidOperationException("Win32 brightness baseline unavailable; no F4 SET sent.");
            if (raw == null) throw new InvalidOperationException("No validated LG brightness transport; no F4 SET sent.");
            if (!SameBrightness(baseline, raw)) throw new InvalidOperationException("Raw brightness differs from Win32; no F4 SET sent.");
        }
        static void RequireAdministrator()
        {
            if (!IsAdministrator) throw new UnauthorizedAccessException("Intel raw GET requests also require Run as administrator (the GET request uses a transport WRITE). No command was sent.");
        }
        internal static string TransportDiagnostics(string stableLgId)
        {
            RequireAdministrator(); var target = Target(stableLgId);
            var log = new StringBuilder("GET-only brightness transport checks; no SetVCP or topology changes.\r\n");
            var baseline = Baseline(stableLgId, log);
            using (var session = new Session())
            {
                IntPtr output = Find(session, target, log);
                foreach (bool aux in new[] { false, true })
                {
                    var result = Probe(session, output, aux, log);
                    if (baseline != null && result != null) log.AppendLine((aux ? "AUX" : "I2C") + " matches Win32: " + SameBrightness(baseline, result));
                }
            }
            return log.ToString();
        }
        internal static string SetInput(string stableLgId, string action)
        {
            var packet = Packet(Value(action));
            RequireAdministrator();
            var target = Target(stableLgId);
            var log = new StringBuilder(); var baseline = Baseline(stableLgId, log); string transport;
            using (var session = new Session())
            {
                IntPtr output = Find(session, target, log);
                var brightness = Probe(session, output, true, log); bool useAux = brightness != null;
                if (!useAux) brightness = Probe(session, output, false, log);
                try { RequireAgreement(baseline, brightness); }
                catch (InvalidOperationException ex) { throw new InvalidOperationException(ex.Message + "\r\n" + log); }
                var current = Target(stableLgId);
                if (!Matches(current.Adapter, current.Id, target)) throw new InvalidOperationException("LG identity changed during discovery. Refusing I2C.");
                transport = useAux ? "AUX" : "I2C";
                log.AppendLine("Selected transport=" + transport + "; brightness preflight PASS");
                // One SET on the validated transport; an uncertain SET never triggers another transport's SET.
                uint status;
                if (useAux) { var auxPacket = AuxPacket(Value(action)); status = session.AuxAccess(output, ref auxPacket); }
                else status = session.Access(output, ref packet);
                Check(status, transport + " LG VCP 0xF4");
            }
            return log + "WRITE_ACCEPTED; transport=" + transport + "; LG private VCP 0xF4=" + Value(action).ToString("X2") + "; physical input change still needs verification";
        }
        static void Assert(bool condition, string detail)
        { if (!condition) throw new InvalidOperationException("IntelDdc self-check failed: " + detail); }
        internal static void SelfTest()
        {
            Assert(IntPtr.Size == 8, "x64 is required");
            Assert(Marshal.SizeOf(typeof(InitArgs)) == 36 && Marshal.SizeOf(typeof(AdapterProperties)) == 320 &&
                Marshal.SizeOf(typeof(DisplayProperties)) == 200 && Marshal.SizeOf(typeof(EncoderProperties)) == 112 && Marshal.SizeOf(typeof(I2cArgs)) == 168 && Marshal.SizeOf(typeof(AuxArgs)) == 176, "IGCL v1.0 structure sizes");
            Assert(Marshal.OffsetOf(typeof(I2cArgs), "Rad").ToInt32() == 32 && Marshal.OffsetOf(typeof(I2cArgs), "Data").ToInt32() == 40 &&
                Marshal.OffsetOf(typeof(InitArgs), "ApplicationUID").ToInt32() == 20, "IGCL field alignment");
            Assert(Marshal.OffsetOf(typeof(AuxArgs), "Operation").ToInt32() == 8 && Marshal.OffsetOf(typeof(AuxArgs), "Flags").ToInt32() == 12 &&
                Marshal.OffsetOf(typeof(AuxArgs), "Address").ToInt32() == 16 && Marshal.OffsetOf(typeof(AuxArgs), "Rad").ToInt32() == 24 &&
                Marshal.OffsetOf(typeof(AuxArgs), "PortId").ToInt32() == 32 && Marshal.OffsetOf(typeof(AuxArgs), "DataSize").ToInt32() == 36 &&
                Marshal.OffsetOf(typeof(AuxArgs), "Data").ToInt32() == 40, "AUX field alignment");
            foreach (string action in new[] { "dp", "hdmi1", "hdmi2" })
            {
                var p = Packet(Value(action)); byte checksum = 0x6E ^ 0x50;
                for (int i = 0; i < 6; i++) checksum ^= p.Data[i];
                Assert(checksum == 0 && p.DataSize == 6 && p.Address == 0x6E && p.Offset == 0x50 && p.Operation == 2 && p.Flags == 0, "LG packet");
                var aux = AuxPacket(Value(action));
                Assert(aux.Size == 176 && aux.Operation == 2 && aux.Flags == 2 && aux.Address == 0x6E && aux.DataSize == 7 && aux.Data[0] == 0x50 &&
                    aux.Rad == 0 && aux.PortId == 0 && aux.Data.Skip(1).Take(6).SequenceEqual(p.Data.Take(6)), "AUX explicit source and unchanged LG payload");
            }
            Assert(Packet(0x90).Data[5] == 0xDD && Packet(0x91).Data[5] == 0xDC && Packet(0xD0).Data[5] == 0x9D, "known LG checksums");
            byte[] reply = { 0x6E, 0x88, 0x02, 0x00, 0x10, 0x00, 0x00, 0x64, 0x00, 0x19, 0xD9 };
            var brightness = ParseBrightness(reply, 11);
            Assert(brightness.Current == 25 && brightness.Maximum == 100, "known brightness response");
            Assert(SameBrightness(brightness, new Brightness { Current = 25, Maximum = 100 }) &&
                !SameBrightness(brightness, new Brightness { Current = 26, Maximum = 100 }), "Win32 brightness agreement");
            RequireAgreement(brightness, new Brightness { Current = 25, Maximum = 100 });
            for (int missing = 0; missing < 3; missing++)
            {
                bool blocked = false;
                try { RequireAgreement(missing == 0 ? null : brightness, missing == 1 ? null : new Brightness { Current = 26, Maximum = 100 }); }
                catch (InvalidOperationException) { blocked = true; }
                Assert(blocked, "missing or differing brightness baseline blocks SET case " + missing);
            }
            for (int fault = 0; fault < 6; fault++)
            {
                byte[] bad = (byte[])reply.Clone(); uint size = 11;
                if (fault == 0) { bad[3] = 1; bad[10] ^= 1; }
                if (fault == 1) size = 10;
                if (fault == 2) bad = Enumerable.Repeat((byte)0xFF, 11).ToArray();
                if (fault == 3) bad[10] ^= 1;
                if (fault == 4) { bad[4] ^= 1; bad[10] ^= 1; }
                if (fault == 5) bad = new byte[10];
                bool rejected = false; try { ParseBrightness(bad, size); } catch (InvalidDataException) { rejected = true; }
                Assert(rejected, "invalid brightness reply case " + fault);
            }
            var target = new TargetInfo { Adapter = new Luid { Low = 4, High = 2 }, Id = 7, Technology = 10 };
            Assert(Matches(target.Adapter, 7, target) && !Matches(target.Adapter, 8, target) &&
                !Matches(new Luid { Low = 5, High = 2 }, 7, target) && !Matches(new Luid { Low = 4, High = 3 }, 7, target), "exact adapter and target matching");
            var hdmi = target; hdmi.Technology = 5;
            Assert(!Matches(target.Adapter, 7, hdmi), "DP transport required");
            bool badAction = false; try { Value("hdmi"); } catch (ArgumentException) { badAction = true; }
            Assert(badAction, "ambiguous HDMI action refusal");
            bool refused = false; try { UniqueOutput(new List<IntPtr> { new IntPtr(1), new IntPtr(2) }); } catch (InvalidOperationException) { refused = true; }
            Assert(refused, "ambiguous output refusal");
            refused = false; try { UniqueOutput(new List<IntPtr>()); } catch (InvalidOperationException) { refused = true; }
            Assert(refused, "missing output refusal");
        }
    }
}
