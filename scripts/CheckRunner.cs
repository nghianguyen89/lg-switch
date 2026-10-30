using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Xml;

namespace LgSwitch
{
    // Developer-only entry point; it never dispatches DDC commands or loads live app settings.
    internal static class CheckRunner
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
        [DllImport("kernel32.dll")] static extern uint SizeofResource(IntPtr module, IntPtr resource);
        [DllImport("kernel32.dll")] static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
        [DllImport("kernel32.dll")] static extern IntPtr LockResource(IntPtr resource);
        [DllImport("kernel32.dll")] static extern bool FreeLibrary(IntPtr module);

        static void VerifyManifest(string path)
        {
            IntPtr module = LoadLibraryEx(path, IntPtr.Zero, 2);
            if (module == IntPtr.Zero) throw new InvalidOperationException("Cannot read production PE resources.");
            try
            {
                IntPtr resource = FindResource(module, new IntPtr(1), new IntPtr(24));
                uint length = SizeofResource(module, resource);
                IntPtr data = LockResource(LoadResource(module, resource));
                if (resource == IntPtr.Zero || data == IntPtr.Zero || length == 0 || length > 65536) throw new InvalidDataException("Embedded manifest missing.");
                var bytes = new byte[length]; Marshal.Copy(data, bytes, 0, (int)length);
                var xml = new XmlDocument { XmlResolver = null };
                using (var stream = new MemoryStream(bytes)) xml.Load(stream);
                var level = xml.SelectSingleNode("//*[local-name()='requestedExecutionLevel']");
                if (level == null || level.Attributes["level"].Value != "requireAdministrator" || level.Attributes["uiAccess"].Value != "false")
                    throw new InvalidDataException("Production manifest does not request administrator at launch.");
                File.WriteAllText(Path.Combine(Files.Root, "manifest-check.txt"), "PASS: embedded production manifest requests requireAdministrator; uiAccess=false. No execution/elevation.\r\n");
            }
            finally { FreeLibrary(module); }
        }

        [STAThread] static int Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                if (args.Length == 1 && args[0] == "--self-test")
                { File.WriteAllText(Path.Combine(Files.Root, "self-test.txt"), SelfTests.Run(), Encoding.UTF8); return 0; }
                if (args.Length == 2 && args[0] == "--verify-manifest")
                { VerifyManifest(Encoding.UTF8.GetString(Convert.FromBase64String(args[1]))); return 0; }
                using (var form = new MainForm(false, true))
                {
                    if (args.Length == 1 && args[0] == "--render-preview") { form.RenderPreview(Path.Combine(Files.Root, "preview.png")); return 0; }
                    if (args.Length != 0) throw new ArgumentException("Unknown check command.");
                    form.PreparePreview(); Application.Run(form); return 0;
                }
            }
            catch (Exception ex)
            { File.WriteAllText(Path.Combine(Files.Root, "last-error.txt"), ex.ToString(), Encoding.UTF8); return 1; }
        }
    }
}
