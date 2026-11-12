using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("Bins for Windows 11")]
[assembly: AssemblyDescription("Grouped taskbar icon launcher for Windows 11")]
[assembly: AssemblyCompany("Piyush Agade")]
[assembly: AssemblyProduct("Bins for Windows 11")]
[assembly: AssemblyCopyright("Copyright © 2026 Piyush Agade")]
[assembly: ComVisible(false)]

[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]
[assembly: AssemblyInformationalVersion("1.1.0")]

namespace WinBins
{
    public static class AppVersion
    {
        public const string Current = "1.1.0";
        public static string Display => "v" + Current;
    }
}
