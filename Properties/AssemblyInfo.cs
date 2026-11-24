using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("Bins for Windows 11")]
[assembly: AssemblyDescription("Grouped taskbar icon launcher for Windows 11")]
[assembly: AssemblyCompany("Piyush Agade")]
[assembly: AssemblyProduct("Bins for Windows 11")]
[assembly: AssemblyCopyright("Copyright © 2026 Piyush Agade")]
[assembly: ComVisible(false)]

[assembly: AssemblyVersion("1.5.12.0")]
[assembly: AssemblyFileVersion("1.5.12.0")]
[assembly: AssemblyInformationalVersion("1.5.12")]

namespace WinBins
{
    public static class AppVersion
    {
        public const string Current = "1.5.12";
        public static string Display => "v" + Current;
    }
}
