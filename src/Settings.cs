using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using Microsoft.Win32;

namespace NexusAnima240
{
    [DataContract] internal sealed class AppSettings
    {
        [DataMember] public string GamePath="";
        [DataMember] public int FrameRate=240;
        internal static string FilePath { get { return Path.Combine(Files.DataDir,"settings.json"); } }
        internal static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var value=Files.Load<AppSettings>(FilePath);
                    if (value.FrameRate<1 || value.FrameRate>Profile.MaxFps) value.FrameRate=240;
                    return value;
                }
            }
            catch (Exception e) { Files.Log("Settings load: "+e.Message); }
            return new AppSettings();
        }
        internal void Save() { Files.Save(FilePath,this); }
    }

    internal static class GameLocator
    {
        internal static bool IsGame(string path)
        {
            try { return !String.IsNullOrWhiteSpace(path) && String.Equals(Path.GetFileName(path),"NexusAnima.exe",StringComparison.OrdinalIgnoreCase) && File.Exists(path) && File.Exists(Path.Combine(Path.GetDirectoryName(path),"UnityPlayer.dll")); }
            catch { return false; }
        }
        private static string InFolder(string folder)
        {
            if (String.IsNullOrWhiteSpace(folder)) return null;
            foreach (string relative in new[] {"NexusAnima.exe",@"NexusAnima Game\NexusAnima.exe",@"games\NexusAnima Game\NexusAnima.exe"})
            { string path=Path.Combine(folder,relative); if (IsGame(path)) return path; }
            return null;
        }
        internal static string Detect()
        {
            Process[] games=Process.GetProcessesByName("NexusAnima");
            foreach (var p in games)
            { try { string path=Native.ImagePath(p.Id); if (IsGame(path)) return path; } catch { } finally { p.Dispose(); } }
            if (IsGame(Profile.DefaultGamePath)) return Profile.DefaultGamePath;
            foreach (string root in new[] {Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)})
                foreach (string folder in new[] {"Nexus Anima","HoYoPlay","miHoYo Launcher"})
                { string found=InFolder(Path.Combine(root,folder)); if (found!=null) return found; }
            foreach (RegistryView view in new[] {RegistryView.Registry64,RegistryView.Registry32})
            {
                try
                {
                    using (var hive=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,view))
                    using (var list=hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        if (list==null) continue;
                        foreach (string key in list.GetSubKeyNames())
                            using (var item=list.OpenSubKey(key))
                            {
                                if (item==null) continue;
                                string name=item.GetValue("DisplayName","") as string ?? "";
                                if (name.IndexOf("Nexus Anima",StringComparison.OrdinalIgnoreCase)<0 && name.IndexOf("因缘精灵",StringComparison.OrdinalIgnoreCase)<0) continue;
                                string found=InFolder(item.GetValue("InstallLocation","") as string);
                                if (found!=null) return found;
                            }
                    }
                }
                catch { }
            }
            return null;
        }
        internal static string Resolve(string saved)
        {
            // Keep a previously chosen, still-valid installation; otherwise auto-detect.
            if (IsGame(saved)) return saved;
            return Detect();
        }
    }
}
