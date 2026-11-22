using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;

namespace WinBins.Models
{
    public enum BinIconMode
    {
        GridMosaic = 0,
        CustomIcon = 1
    }

    public enum BinFlyoutDensity
    {
        Compact = 0,
        Normal = 1,
        Spacious = 2
    }

    public enum BinThemeMode
    {
        Dark = 0,
        Light = 1,
        System = 2
    }

    public enum BinWindowStyle
    {
        Normal = 0,
        Maximized = 1,
        Minimized = 2
    }

    public enum BinSectionLayout
    {
        Tabs = 0,
        StackedHeaders = 1
    }

    public enum LiveFolderSortOrder
    {
        NewestFirst = 0,
        NameAscending = 1,
        TypeThenName = 2
    }

    public enum BinItemKind
    {
        Application = 0,
        Folder = 1,
        Shortcut = 2,
        FileOrUrl = 3,
        WebUrl = 4,
        PackagedApp = 5,
        SystemCommand = 6
    }

    [DataContract]
    public class AppConfig
    {
        [DataMember(Order = 1)]
        public List<BinGroup> Bins { get; set; } = new List<BinGroup>();

        [DataMember(Order = 2)]
        public bool LaunchAtStartup { get; set; } = false;

        [DataMember(Order = 3)]
        public bool ClosePopupOnLaunch { get; set; } = true;

        [DataMember(Order = 4)]
        public bool MinimizeToTrayOnClose { get; set; } = true;

        [DataMember(Order = 5)]
        public bool HidePinToTaskbarPrompt { get; set; } = false;

        [DataMember(Order = 6)]
        public bool HideRunningAppsOnTaskbar { get; set; } = false;

        [DataMember(Order = 7)]
        public bool HoverToOpenFlyout { get; set; } = false;

        [DataMember(Order = 8)]
        public int HoverOpenDelayMs { get; set; } = 280;

        [DataMember(Order = 9)]
        public BinThemeMode ThemeMode { get; set; } = BinThemeMode.Dark;

        [DataMember(Order = 10)]
        public bool UseAcrylicFlyout { get; set; } = true;

        [DataMember(Order = 11)]
        public string GlobalSearchHotkey { get; set; } = "Ctrl+Alt+Space";

        public AppConfig()
        {
            SetFieldDefaults();
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            SetFieldDefaults();
        }

        private void SetFieldDefaults()
        {
            Bins = new List<BinGroup>();
            LaunchAtStartup = false;
            ClosePopupOnLaunch = true;
            MinimizeToTrayOnClose = true;
            HidePinToTaskbarPrompt = false;
            HideRunningAppsOnTaskbar = false;
            HoverToOpenFlyout = false;
            HoverOpenDelayMs = 280;
            ThemeMode = BinThemeMode.Dark;
            UseAcrylicFlyout = true;
            GlobalSearchHotkey = "Ctrl+Alt+Space";
        }

        public void EnsureDefaults()
        {
            if (Bins == null)
            {
                Bins = new List<BinGroup>();
            }

            if (HoverOpenDelayMs < 100) HoverOpenDelayMs = 100;
            if (HoverOpenDelayMs > 1500) HoverOpenDelayMs = 1500;
            if (GlobalSearchHotkey == null) GlobalSearchHotkey = string.Empty;

            foreach (var bin in Bins)
            {
                bin.EnsureDefaults();
            }
        }
    }

    [DataContract]
    public class BinGroup
    {
        [DataMember(Order = 1)]
        public string Id { get; set; }

        [DataMember(Order = 2)]
        public string Name { get; set; }

        [DataMember(Order = 3)]
        public BinIconMode IconMode { get; set; }

        [DataMember(Order = 4)]
        public string CustomIconPath { get; set; }

        [DataMember(Order = 5)]
        public int CustomIconIndex { get; set; }

        [DataMember(Order = 6)]
        public int Columns { get; set; }

        [DataMember(Order = 7)]
        public bool IsEnabledOnTaskbar { get; set; }

        [DataMember(Order = 8)]
        public int IconRevision { get; set; }

        [DataMember(Order = 9)]
        public List<BinItem> Items { get; set; }

        [DataMember(Order = 10)]
        public string FlyoutIconPath { get; set; }

        [DataMember(Order = 11)]
        public int FlyoutIconIndex { get; set; }

        [DataMember(Order = 12)]
        public int MaxRows { get; set; }

        [DataMember(Order = 13)]
        public BinFlyoutDensity Density { get; set; }

        [DataMember(Order = 14)]
        public int ItemPadding { get; set; }

        [DataMember(Order = 15)]
        public bool ShowFlyoutTitle { get; set; }

        [DataMember(Order = 16)]
        public bool ShowFlyoutAddButton { get; set; }

        [DataMember(Order = 17)]
        public bool ShowFlyoutEditButton { get; set; }

        [DataMember(Order = 18)]
        public int TaskbarOrder { get; set; }

        [DataMember(Order = 19)]
        public bool HideRunningAppsOnTaskbar { get; set; }

        [DataMember(Order = 20)]
        public string Hotkey { get; set; }

        [DataMember(Order = 21)]
        public bool ShowMosaicBackplate { get; set; }

        [DataMember(Order = 22)]
        public BinSectionLayout SectionLayout { get; set; }

        [DataMember(Order = 23)]
        public string LiveFolderPath { get; set; }

        [DataMember(Order = 24)]
        public int LiveFolderMaxItems { get; set; }

        [DataMember(Order = 25)]
        public LiveFolderSortOrder LiveFolderSort { get; set; }

        [DataMember(Order = 26)]
        public string AccentColorHex { get; set; }

        [DataMember(Order = 27)]
        public bool FloatOpenAppsFirst { get; set; }

        public BinGroup()
        {
            SetFieldDefaults();
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            SetFieldDefaults();
        }

        private void SetFieldDefaults()
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 10);
            Name = "New Bin";
            IconMode = BinIconMode.GridMosaic;
            CustomIconPath = string.Empty;
            CustomIconIndex = 0;
            Columns = 4;
            IsEnabledOnTaskbar = true;
            IconRevision = 1;
            Items = new List<BinItem>();
            FlyoutIconPath = string.Empty;
            FlyoutIconIndex = 0;
            MaxRows = 0; // 0 = Auto
            Density = BinFlyoutDensity.Normal;
            ItemPadding = 6;
            ShowFlyoutTitle = true;
            ShowFlyoutAddButton = true;
            ShowFlyoutEditButton = true;
            TaskbarOrder = int.MaxValue;
            HideRunningAppsOnTaskbar = false;
            Hotkey = string.Empty;
            ShowMosaicBackplate = false;
            SectionLayout = BinSectionLayout.Tabs;
            LiveFolderPath = string.Empty;
            LiveFolderMaxItems = 12;
            LiveFolderSort = LiveFolderSortOrder.NewestFirst;
            AccentColorHex = string.Empty;
            FloatOpenAppsFirst = false;
        }

        public void EnsureDefaults()
        {
            if (string.IsNullOrWhiteSpace(Id))
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 10);
            }

            if (string.IsNullOrWhiteSpace(Name))
            {
                Name = "Bin";
            }

            if (Columns < 1) Columns = 1;
            if (Columns > 10) Columns = 10;
            if (MaxRows < 0) MaxRows = 0;
            if (MaxRows > 12) MaxRows = 12;
            if (ItemPadding < 0) ItemPadding = 0;
            if (ItemPadding > 20) ItemPadding = 20;
            if (IconRevision < 1) IconRevision = 1;
            if (LiveFolderMaxItems < 1) LiveFolderMaxItems = 12;
            if (LiveFolderMaxItems > 48) LiveFolderMaxItems = 48;

            if (CustomIconPath == null) CustomIconPath = string.Empty;
            if (FlyoutIconPath == null) FlyoutIconPath = string.Empty;
            if (Hotkey == null) Hotkey = string.Empty;
            if (LiveFolderPath == null) LiveFolderPath = string.Empty;
            if (AccentColorHex == null) AccentColorHex = string.Empty;

            if (Items == null)
            {
                Items = new List<BinItem>();
            }

            foreach (var item in Items)
            {
                item.EnsureDefaults();
            }
        }
    }

    [DataContract]
    public class BinItem
    {
        public const string ShellAppsPrefix = @"shell:AppsFolder\";
        public const string SystemPrefix = "bin://system/";

        [DataMember(Order = 1)]
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 10);

        [DataMember(Order = 2)]
        public string Name { get; set; } = string.Empty;

        [DataMember(Order = 3)]
        public string TargetPath { get; set; } = string.Empty;

        [DataMember(Order = 4)]
        public string Arguments { get; set; } = string.Empty;

        [DataMember(Order = 5)]
        public string WorkingDirectory { get; set; } = string.Empty;

        [DataMember(Order = 6)]
        public string CustomIconPath { get; set; } = string.Empty;

        [DataMember(Order = 7)]
        public BinItemKind Kind { get; set; } = BinItemKind.Application;

        [DataMember(Order = 8)]
        public int CustomIconIndex { get; set; } = 0;

        [DataMember(Order = 9)]
        public bool RunAsAdmin { get; set; } = false;

        [DataMember(Order = 10)]
        public BinWindowStyle WindowStyle { get; set; } = BinWindowStyle.Normal;

        [DataMember(Order = 11)]
        public string Section { get; set; } = string.Empty;

        [DataMember(Order = 12)]
        public bool IncludeInOpenAll { get; set; } = true;

        [DataMember(Order = 13)]
        public int OpenAllDelayMs { get; set; } = 0;

        [IgnoreDataMember]
        public bool IsLiveFolderItem { get; set; }

        public BinItem()
        {
            SetFieldDefaults();
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            SetFieldDefaults();
        }

        private void SetFieldDefaults()
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 10);
            Name = string.Empty;
            TargetPath = string.Empty;
            Arguments = string.Empty;
            WorkingDirectory = string.Empty;
            CustomIconPath = string.Empty;
            CustomIconIndex = 0;
            Kind = BinItemKind.Application;
            RunAsAdmin = false;
            WindowStyle = BinWindowStyle.Normal;
            Section = string.Empty;
            IncludeInOpenAll = true;
            OpenAllDelayMs = 0;
            IsLiveFolderItem = false;
        }

        public void EnsureDefaults()
        {
            if (string.IsNullOrWhiteSpace(Id))
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 10);
            }

            if (Arguments == null) Arguments = string.Empty;
            if (WorkingDirectory == null) WorkingDirectory = string.Empty;
            if (CustomIconPath == null) CustomIconPath = string.Empty;
            if (Section == null) Section = string.Empty;
            if (OpenAllDelayMs < 0) OpenAllDelayMs = 0;
            if (OpenAllDelayMs > 15000) OpenAllDelayMs = 15000;

            if (!string.IsNullOrWhiteSpace(TargetPath) && IsSystemCommand(TargetPath))
            {
                Kind = BinItemKind.SystemCommand;
            }
            else if (!string.IsNullOrWhiteSpace(TargetPath) && IsWebUrl(TargetPath))
            {
                Kind = BinItemKind.WebUrl;
            }
            else if (!string.IsNullOrWhiteSpace(TargetPath) && IsPackagedApp(TargetPath))
            {
                Kind = BinItemKind.PackagedApp;
            }

            if (string.IsNullOrWhiteSpace(Name) && !string.IsNullOrWhiteSpace(TargetPath))
            {
                Name = DeriveDisplayName(TargetPath);
            }
        }

        public static BinItem FromPath(string path)
        {
            string cleanPath = (path ?? string.Empty).Trim().Trim('"');
            BinItemKind kind = DetectKind(cleanPath);
            string name = DeriveDisplayName(cleanPath);

            return new BinItem
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 10),
                Name = name,
                TargetPath = cleanPath,
                Kind = kind
            };
        }

        public static BinItem FromUrl(string normalizedUrl, string customName = null, string customIconPath = null, int customIconIndex = 0)
        {
            string url = (normalizedUrl ?? string.Empty).Trim();
            string name = !string.IsNullOrWhiteSpace(customName)
                ? customName.Trim()
                : DeriveUrlDisplayName(url);

            return new BinItem
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 10),
                Name = name,
                TargetPath = url,
                Kind = BinItemKind.WebUrl,
                CustomIconPath = customIconPath ?? string.Empty,
                CustomIconIndex = customIconIndex
            };
        }

        public static BinItem FromPackagedApp(string appUserModelId, string displayName)
        {
            string fullTarget = appUserModelId != null && appUserModelId.StartsWith(ShellAppsPrefix, StringComparison.OrdinalIgnoreCase)
                ? appUserModelId.Trim()
                : ShellAppsPrefix + (appUserModelId ?? string.Empty).Trim();

            return new BinItem
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 10),
                Name = string.IsNullOrWhiteSpace(displayName) ? "App" : displayName.Trim(),
                TargetPath = fullTarget,
                Kind = BinItemKind.PackagedApp
            };
        }

        public static BinItem FromSystemAction(string actionKey, string displayName = null, string customCommand = null)
        {
            string cleanKey = (actionKey ?? string.Empty).Trim().ToLowerInvariant();
            if (cleanKey.StartsWith(SystemPrefix, StringComparison.OrdinalIgnoreCase))
            {
                cleanKey = cleanKey.Substring(SystemPrefix.Length);
            }

            string fullTarget = SystemPrefix + cleanKey;
            return new BinItem
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 10),
                Name = !string.IsNullOrWhiteSpace(displayName) ? displayName.Trim() : DeriveSystemActionTitle(cleanKey),
                TargetPath = fullTarget,
                Arguments = customCommand ?? string.Empty,
                Kind = BinItemKind.SystemCommand,
                IncludeInOpenAll = false
            };
        }

        public static bool IsSystemCommand(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.Trim().StartsWith(SystemPrefix, StringComparison.OrdinalIgnoreCase);
        }

        public static string GetSystemActionKey(string value)
        {
            if (!IsSystemCommand(value))
                return string.Empty;

            return value.Trim().Substring(SystemPrefix.Length).Trim('/').ToLowerInvariant();
        }

        public static string DeriveSystemActionTitle(string actionKey)
        {
            switch ((actionKey ?? string.Empty).ToLowerInvariant())
            {
                case "recyclebin": return "Recycle Bin";
                case "lock": return "Lock PC";
                case "sleep": return "Sleep";
                case "restart": return "Restart PC";
                case "shutdown": return "Shut Down";
                case "snip": return "Screen Snip";
                case "taskmgr": return "Task Manager";
                case "settings": return "Windows Settings";
                case "powershell": return "PowerShell Command";
                default: return "System Action";
            }
        }

        public static bool IsPackagedApp(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.Trim().StartsWith(ShellAppsPrefix, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsWebUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string trimmed = value.Trim();
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri uri))
                return false;

            return (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) &&
                   !string.IsNullOrWhiteSpace(uri.Host);
        }

        public static bool TryNormalizeWebUrl(string rawInput, out string normalizedUrl)
        {
            normalizedUrl = null;
            if (string.IsNullOrWhiteSpace(rawInput))
                return false;

            string candidate = rawInput.Trim();
            if (candidate.IndexOf("://", StringComparison.Ordinal) < 0)
            {
                bool isLocal = candidate.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ||
                               candidate.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase);
                candidate = (isLocal ? "http://" : "https://") + candidate;
            }

            if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri uri) &&
                (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) &&
                !string.IsNullOrWhiteSpace(uri.Host) &&
                (uri.Host.Contains(".") ||
                 string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                 uri.HostNameType == UriHostNameType.IPv4 ||
                 uri.HostNameType == UriHostNameType.IPv6))
            {
                normalizedUrl = uri.AbsoluteUri;
                return true;
            }

            return false;
        }

        public static string DeriveUrlDisplayName(string url)
        {
            if (Uri.TryCreate((url ?? string.Empty).Trim(), UriKind.Absolute, out Uri uri) &&
                !string.IsNullOrWhiteSpace(uri.Host))
            {
                string host = uri.Host;
                if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) && host.Length > 4)
                {
                    host = host.Substring(4);
                }
                return host;
            }

            return "Website";
        }

        public static BinItemKind DetectKind(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return BinItemKind.FileOrUrl;

            if (IsSystemCommand(path))
                return BinItemKind.SystemCommand;

            if (IsPackagedApp(path))
                return BinItemKind.PackagedApp;

            if (IsWebUrl(path))
                return BinItemKind.WebUrl;

            try
            {
                if (Directory.Exists(path))
                    return BinItemKind.Folder;

                string ext = Path.GetExtension(path)?.ToLowerInvariant() ?? string.Empty;
                if (ext == ".lnk" || ext == ".url" || ext == ".appref-ms")
                    return BinItemKind.Shortcut;

                if (ext == ".exe" || ext == ".bat" || ext == ".cmd" || ext == ".msc" || ext == ".cpl")
                    return BinItemKind.Application;
            }
            catch
            {
            }

            return BinItemKind.FileOrUrl;
        }

        public static string DeriveDisplayName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "Item";

            if (IsSystemCommand(path))
                return DeriveSystemActionTitle(GetSystemActionKey(path));

            if (IsWebUrl(path))
                return DeriveUrlDisplayName(path);

            try
            {
                string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (Directory.Exists(path))
                {
                    string folderName = Path.GetFileName(trimmed);
                    return string.IsNullOrWhiteSpace(folderName) ? trimmed : folderName;
                }

                string ext = Path.GetExtension(trimmed)?.ToLowerInvariant() ?? string.Empty;
                if (ext == ".exe" || ext == ".lnk" || ext == ".url" || ext == ".appref-ms" || ext == ".msc")
                {
                    string withoutExt = Path.GetFileNameWithoutExtension(trimmed);
                    if (!string.IsNullOrWhiteSpace(withoutExt))
                        return withoutExt;
                }

                string fileName = Path.GetFileName(trimmed);
                return string.IsNullOrWhiteSpace(fileName) ? trimmed : fileName;
            }
            catch
            {
                return path;
            }
        }
    }
}
