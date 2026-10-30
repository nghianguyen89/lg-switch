using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace LgSwitch
{
    [StructLayout(LayoutKind.Sequential)] public struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] public struct Rational { public uint Numerator, Denominator; }
    [StructLayout(LayoutKind.Sequential)] public struct SourceInfo
    { public Luid Adapter; public uint Id, ModeIndex, Status; }
    [StructLayout(LayoutKind.Sequential)] public struct TargetInfo
    {
        public Luid Adapter;
        public uint Id, ModeIndex, Technology, Rotation, Scaling;
        public Rational Refresh;
        public uint Scanline;
        public int Available;
        public uint Status;
    }
    [StructLayout(LayoutKind.Sequential)] public struct DisplayPath
    { public SourceInfo Source; public TargetInfo Target; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] public struct DisplayMode
    {
        public uint Type, Id;
        public Luid Adapter;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 48)] public byte[] Data;
    }
    [StructLayout(LayoutKind.Sequential)] public struct DeviceHeader
    { public uint Type, Size; public Luid Adapter; public uint Id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct TargetName
    {
        public DeviceHeader Header;
        public uint Flags, Technology;
        public ushort Manufacturer, Product;
        public uint Connector;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Friendly;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DevicePath;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct SourceName
    {
        public DeviceHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Gdi;
    }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct MonitorInfo
    {
        public uint Size; public Rect Monitor, Work; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct PhysicalMonitor
    {
        public IntPtr Handle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
    }

    public sealed class Endpoint
    {
        public string Id, Friendly, Gdi;
        public int Index;
        public uint Technology;
        public override string ToString() { return Friendly + " | " + Gdi + " | " + (Technology == 10 ? "DP" : "type " + Technology); }
    }

    public sealed class Topology
    {
        public DisplayPath[] Paths;
        public DisplayMode[] Modes;

        public Topology Clone()
        {
            return new Topology { Paths = (DisplayPath[])Paths.Clone(), Modes = Modes.Select(m => {
                m.Data = (byte[])m.Data.Clone(); return m;
            }).ToArray() };
        }

        public Topology Only(int index)
        {
            var result = Clone();
            var path = result.Paths[index];
            uint mode = path.Source.ModeIndex;
            if (mode >= result.Modes.Length || result.Modes[mode].Type != 1)
                throw new InvalidOperationException("Missing source mode; cannot safely enable ASUS only.");
            Array.Copy(BitConverter.GetBytes(0), 0, result.Modes[mode].Data, 12, 4);
            Array.Copy(BitConverter.GetBytes(0), 0, result.Modes[mode].Data, 16, 4);
            result.Paths = new[] { path };
            return result;
        }

        public string Encode()
        {
            using (var ms = new MemoryStream()) using (var w = new BinaryWriter(ms))
            {
                w.Write(1); w.Write(Paths.Length); w.Write(Modes.Length);
                foreach (var p in Paths) w.Write(Native.Bytes(p));
                foreach (var m in Modes) w.Write(Native.Bytes(m));
                return Convert.ToBase64String(ms.ToArray());
            }
        }

        public static Topology Decode(string text)
        {
            using (var ms = new MemoryStream(Convert.FromBase64String(text))) using (var r = new BinaryReader(ms))
            {
                if (r.ReadInt32() != 1) throw new InvalidDataException("Unknown snapshot version.");
                int paths = r.ReadInt32(), modes = r.ReadInt32();
                if (paths != 2 || modes < 2 || modes > 64 || ms.Length != 12 + paths * 72 + modes * 64)
                    throw new InvalidDataException("Invalid two-screen snapshot.");
                var t = new Topology { Paths = new DisplayPath[paths], Modes = new DisplayMode[modes] };
                for (int i = 0; i < paths; i++) t.Paths[i] = Native.Struct<DisplayPath>(r.ReadBytes(72));
                for (int i = 0; i < modes; i++) t.Modes[i] = Native.Struct<DisplayMode>(r.ReadBytes(64));
                foreach (var p in t.Paths)
                    if ((p.Flags & 1) == 0 || p.Source.ModeIndex >= modes || p.Target.ModeIndex >= modes)
                        throw new InvalidDataException("Invalid snapshot mode indexes.");
                return t;
            }
        }
    }

    internal static class Native
    {
        [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
        [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] DisplayPath[] pathArray, ref uint modes, [Out] DisplayMode[] modeArray, IntPtr topology);
        [DllImport("user32.dll")] static extern int SetDisplayConfig(uint paths, DisplayPath[] pathArray, uint modes, DisplayMode[] modeArray, uint flags);
        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] static extern int TargetDeviceInfo(ref TargetName info);
        [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] static extern int SourceDeviceInfo(ref SourceName info);
        delegate bool MonitorCallback(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);
        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorCallback callback, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("dxva2.dll", SetLastError = true)] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);
        [DllImport("dxva2.dll", SetLastError = true)] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PhysicalMonitor[] physical);
        [DllImport("dxva2.dll", SetLastError = true)] static extern bool DestroyPhysicalMonitors(uint count, PhysicalMonitor[] physical);
        [DllImport("dxva2.dll", SetLastError = true)] static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr monitor, byte code, out uint type, out uint value, out uint maximum);
        [DllImport("dxva2.dll", SetLastError = true)] static extern bool SetVCPFeature(IntPtr monitor, byte code, uint value);
        [DllImport("dxva2.dll", SetLastError = true)] static extern bool GetCapabilitiesStringLength(IntPtr monitor, out uint length);
        [DllImport("dxva2.dll", SetLastError = true, CharSet = CharSet.Ansi)] static extern bool CapabilitiesRequestAndCapabilitiesReply(IntPtr monitor, StringBuilder text, uint length);

        internal static byte[] Bytes<T>(T value)
        {
            int size = Marshal.SizeOf(typeof(T)); var ptr = Marshal.AllocHGlobal(size);
            try { Marshal.StructureToPtr(value, ptr, false); var bytes = new byte[size]; Marshal.Copy(ptr, bytes, 0, size); return bytes; }
            finally { Marshal.FreeHGlobal(ptr); }
        }
        internal static T Struct<T>(byte[] bytes)
        {
            var ptr = Marshal.AllocHGlobal(bytes.Length);
            try { Marshal.Copy(bytes, 0, ptr, bytes.Length); return (T)Marshal.PtrToStructure(ptr, typeof(T)); }
            finally { Marshal.FreeHGlobal(ptr); }
        }
        internal static void Check(int error, string operation)
        { if (error != 0) throw new Win32Exception(error, operation + ": " + new Win32Exception(error).Message); }

        internal static Topology Query()
        {
            for (int retry = 0; retry < 5; retry++)
            {
                uint paths, modes; Check(GetDisplayConfigBufferSizes(2, out paths, out modes), "GetDisplayConfigBufferSizes");
                var p = new DisplayPath[paths]; var m = new DisplayMode[modes];
                int error = QueryDisplayConfig(2, ref paths, p, ref modes, m, IntPtr.Zero);
                if (error == 122) continue;
                Check(error, "QueryDisplayConfig"); Array.Resize(ref p, (int)paths); Array.Resize(ref m, (int)modes);
                return new Topology { Paths = p, Modes = m };
            }
            throw new InvalidOperationException("Display topology is changing. Wait and retry.");
        }
        internal static Endpoint[] Endpoints(Topology t, bool readSourceName = true)
        {
            return t.Paths.Select((p, i) => {
                var target = new TargetName { Header = new DeviceHeader { Type = 2, Size = (uint)Marshal.SizeOf(typeof(TargetName)), Adapter = p.Target.Adapter, Id = p.Target.Id } };
                var source = new SourceName { Header = new DeviceHeader { Type = 1, Size = (uint)Marshal.SizeOf(typeof(SourceName)), Adapter = p.Source.Adapter, Id = p.Source.Id } };
                Check(TargetDeviceInfo(ref target), "Target device name");
                if (readSourceName) Check(SourceDeviceInfo(ref source), "Source device name");
                if (String.IsNullOrWhiteSpace(target.DevicePath)) throw new InvalidOperationException("Monitor has no stable device identity.");
                return new Endpoint { Id = target.DevicePath, Friendly = target.Friendly, Gdi = source.Gdi, Index = i, Technology = p.Target.Technology };
            }).ToArray();
        }
        internal static void Validate(Topology t) { Check(SetDisplayConfig((uint)t.Paths.Length, t.Paths, (uint)t.Modes.Length, t.Modes, 0x20 | 0x40), "Validate display configuration"); }
        internal static void Apply(Topology t)
        {
            Validate(t);
            Check(SetDisplayConfig((uint)t.Paths.Length, t.Paths, (uint)t.Modes.Length, t.Modes, 0x20 | 0x80), "Apply display configuration");
        }

        // Called only in a disposable process: a graphics driver's DDC call can hang.
        internal static string Ddc(string id, string action)
        {
            var t = Query(); var endpoints = Endpoints(t);
            var endpoint = endpoints.SingleOrDefault(e => String.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
            if (endpoint == null) throw new InvalidOperationException("LG is not an active desktop target. Enable Extend first.");
            if (endpoints.Count(e => e.Gdi == endpoint.Gdi) != 1) throw new InvalidOperationException("Cloned source: cannot safely address a physical monitor.");
            IntPtr monitorHandle = IntPtr.Zero;
            MonitorCallback callback = delegate(IntPtr handle, IntPtr dc, IntPtr rect, IntPtr data) {
                var mi = new MonitorInfo { Size = (uint)Marshal.SizeOf(typeof(MonitorInfo)) };
                if (GetMonitorInfo(handle, ref mi) && String.Equals(mi.Device, endpoint.Gdi, StringComparison.OrdinalIgnoreCase)) monitorHandle = handle;
                return true;
            };
            if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero) || monitorHandle == IntPtr.Zero)
                throw new InvalidOperationException("No current HMONITOR for LG.");
            uint count;
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitorHandle, out count)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Physical monitor count failed.");
            if (count != 1) throw new InvalidOperationException("Expected exactly one physical monitor; refusing ambiguous DDC target.");
            var physical = new PhysicalMonitor[count];
            if (!GetPhysicalMonitorsFromHMONITOR(monitorHandle, count, physical)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Physical monitor enumeration failed.");
            try
            {
                IntPtr handle = physical[0].Handle;
                if (Inputs.IsKnown(action))
                {
                    if (!SetVCPFeature(handle, 0x60, Inputs.StandardValue(action))) throw new Win32Exception(Marshal.GetLastWin32Error(), "Set VCP 0x60 failed.");
                    return "WRITE_ACCEPTED; physical input change still needs verification";
                }
                if (action == "caps")
                {
                    uint length;
                    if (!GetCapabilitiesStringLength(handle, out length)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Capabilities length failed.");
                    if (length < 1 || length > 65536) throw new InvalidDataException("Invalid capabilities length.");
                    var text = new StringBuilder((int)length);
                    if (!CapabilitiesRequestAndCapabilitiesReply(handle, text, length)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Capabilities request failed.");
                    return text.ToString();
                }
                if (action != "read" && action != "brightness") throw new ArgumentException("Unknown DDC action.");
                uint type, value, maximum;
                byte code = action == "brightness" ? (byte)0x10 : (byte)0x60;
                if (!GetVCPFeatureAndVCPFeatureReply(handle, code, out type, out value, out maximum)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Read VCP 0x" + code.ToString("X2") + " failed.");
                return action == "brightness" ? value + "/" + maximum : value.ToString();
            }
            finally { DestroyPhysicalMonitors(count, physical); }
        }
    }
}
