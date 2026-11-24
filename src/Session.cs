using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace NexusAnima240
{
    internal static class Files
    {
        internal static string DataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
        internal static string JournalPath { get { return Path.Combine(DataDir, "restore.json"); } }
        internal static void Initialize()
        {
            try { Directory.CreateDirectory(DataDir); File.AppendAllText(Path.Combine(DataDir, "session.log"), ""); }
            catch { DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusAnima240"); Directory.CreateDirectory(DataDir); }
        }
        internal static void Log(string message)
        {
            try { File.AppendAllText(Path.Combine(DataDir, "session.log"), DateTimeOffset.Now.ToString("o") + " " + message + Environment.NewLine, Encoding.UTF8); }
            catch { }
        }
        internal static void Save<T>(string path, T data)
        {
            string temp = path + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            { new DataContractJsonSerializer(typeof(T)).WriteObject(stream, data); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        internal static T Load<T>(string path)
        { using (var stream = File.OpenRead(path)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream); }
    }

    [DataContract] internal sealed class RestoreRecord
    {
        [DataMember] public int Pid;
        [DataMember] public long Created;
        [DataMember] public int Original;
        [DataMember] public int Target;
        [DataMember] public string Hash;
    }

    internal sealed class FpsLease
    {
        internal readonly IntPtr Handle;
        internal readonly long Address;
        internal int Original;
        internal readonly int Target;
        internal bool WasWritten;
        internal Action<int> BeforeWrite;
        internal FpsLease(IntPtr handle, long address, int original, int target)
        {
            if (!Profile.PlausibleOriginal(original)) throw new InvalidOperationException("原始帧率值异常，已拒绝修改: " + original);
            if (target<1 || target>Profile.MaxFps) throw new ArgumentException("帧率必须是 1–240 之间的整数。");
            Handle = handle; Address = address; Original = original; Target = target;
        }
        internal int Current { get { return Native.ReadInt(Handle, Address); } }
        internal void Apply()
        {
            int current = Current;
            if (current == Target) return;
            if (!Profile.PlausibleOriginal(current)) throw new InvalidOperationException("运行中的帧率值异常，已停止: " + current);
            // The game can select its real cap after the loading screen. Remember each new
            // game-supplied value before overriding it; never restore an earlier loading cap.
            if (!WasWritten || Original!=current)
            {
                if (BeforeWrite!=null) BeforeWrite(current);
                Original=current;
            }
            // Track the write before attempting it so partial failures still enter the rollback path.
            WasWritten = true;
            Native.WriteInt(Handle, Address, Target);
        }
        internal string Restore()
        {
            if (!WasWritten) return "未修改帧率，无需恢复。";
            int current = Current;
            if (current == Target) { Native.WriteInt(Handle, Address, Original); WasWritten = false; return "已恢复游戏帧率 " + Original + "。"; }
            if (current == Original) { WasWritten = false; return "游戏已恢复原始帧率。"; }
            if (!Profile.PlausibleOriginal(current)) throw new InvalidOperationException("恢复时发现异常值，已保留恢复记录；重启游戏可清除临时修改。");
            WasWritten = false;
            return "游戏已自行改为 " + current + "，保留游戏当前设置。";
        }
    }

    internal sealed class Session : IDisposable
    {
        internal int Pid;
        internal long Created, ModuleBase;
        internal string GamePath;
        internal IntPtr Handle;
        internal FpsLease Lease;
        internal bool Active;
        internal bool Launched;
        internal bool Restored;
        internal string RestoreMessage;
        private Mutex writerMutex;
        private bool ownsMutex;

        private void LockWriter()
        {
            writerMutex=new Mutex(false,"Local\\NexusAnima240_Writer");
            try { ownsMutex=writerMutex.WaitOne(0); } catch (AbandonedMutexException) { ownsMutex=true; }
            if (!ownsMutex) throw new InvalidOperationException("另一个解锁实例正在运行，请先停止它。");
        }

        internal static int FindPid()
        {
            Process[] matches = Process.GetProcessesByName("NexusAnima");
            try
            {
                if (matches.Length == 0) throw new InvalidOperationException("未发现 NexusAnima.exe。请先从米哈游启动器打开游戏。");
                if (matches.Length != 1) throw new InvalidOperationException("发现多个游戏进程，请只保留一个游戏实例。");
                return matches[0].Id;
            }
            finally { foreach (var p in matches) p.Dispose(); }
        }

        internal static Session Launch(string gamePath, int target, Action<string> progress, Func<bool> cancel)
        {
            var s=new Session();
            try
            {
                s.LockWriter();
                Process[] existing=Process.GetProcessesByName("NexusAnima");
                int count=existing.Length; foreach (var p in existing) p.Dispose();
                if (count!=0) throw new InvalidOperationException("请先从游戏内正常退出《崩坏：因缘精灵》，再由本工具启动。当前游戏不会被强制关闭。");
                s.GamePath=Path.GetFullPath(gamePath);
                if (!String.Equals(Path.GetFileName(s.GamePath),"NexusAnima.exe",StringComparison.OrdinalIgnoreCase) || Profile.Sha256(s.GamePath)!=Profile.ExeHash)
                    throw new InvalidOperationException("游戏可执行文件指纹不匹配，已拒绝启动。");
                string unityPath=Path.Combine(Path.GetDirectoryName(s.GamePath),"UnityPlayer.dll");
                Profile.CheckFile(unityPath);
                Native.StartupInfo startup=new Native.StartupInfo(); startup.cb=(uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.StartupInfo));
                Native.ProcessInfo created;
                // Normal process creation: retain the handle supplied by Windows. No suspended launch,
                // injection, code patching, privilege adjustment, or game protection changes.
                if (!Native.CreateProcess(s.GamePath,new StringBuilder("\""+s.GamePath+"\""),IntPtr.Zero,IntPtr.Zero,false,0,IntPtr.Zero,Path.GetDirectoryName(s.GamePath),ref startup,out created))
                    throw Native.Error("启动游戏失败");
                s.Handle=created.Process; s.Pid=created.Pid; Native.CloseHandle(created.Thread);
                s.Launched=true;
                s.Created=Native.CreationTime(s.Handle);
                Files.Log("Launched original game pid="+s.Pid+"; retained CreateProcess handle.");
                progress("游戏已启动，等待引擎加载和帧率初始化…");
                Stopwatch timer=Stopwatch.StartNew();
                Exception last=null;
                while (timer.Elapsed.TotalSeconds<90)
                {
                    if (cancel()) throw new OperationCanceledException("已取消等待；游戏继续正常运行。");
                    if (!s.Alive) throw new InvalidOperationException("游戏在初始化阶段已退出，请从官方启动器检查游戏状态。");
                    try
                    {
                        s.ModuleBase=Native.FindUnityModule(s.Handle,unityPath);
                        if (s.ModuleBase!=0 && timer.Elapsed.TotalSeconds>=5)
                        {
                            Profile.CheckMemory(s.Handle,s.ModuleBase);
                            int current=Native.ReadInt(s.Handle,s.ModuleBase+Profile.FpsRva);
                            if (Profile.PlausibleOriginal(current) && current>0)
                            {
                                s.RecoverPrevious();
                                current=Native.ReadInt(s.Handle,s.ModuleBase+Profile.FpsRva);
                                s.Lease=new FpsLease(s.Handle,s.ModuleBase+Profile.FpsRva,current,target);
                                Files.Log("Launch validation passed pid="+s.Pid+" original="+current);
                                return s;
                            }
                        }
                    }
                    catch (Win32Exception e)
                    {
                        last=e;
                        if (e.NativeErrorCode==5 && timer.Elapsed.TotalSeconds>=10)
                            throw new InvalidOperationException("启动时取得的句柄仍被拒绝读取。当前保护不支持此方法，未修改游戏。",e);
                    }
                    Thread.Sleep(200);
                }
                throw new InvalidOperationException("等待引擎初始化超时。"+(last==null ? "" : " 最后错误："+last.Message));
            }
            catch { s.Dispose(); throw; }
        }

        private void RecoverPrevious()
        {
            if (!File.Exists(Files.JournalPath)) return;
            RestoreRecord record;
            try { record = Files.Load<RestoreRecord>(Files.JournalPath); }
            catch (Exception e) { throw new InvalidOperationException("恢复记录损坏，已拒绝覆盖，请保留 data 目录用于排查。", e); }
            if (record.Pid == Pid && record.Created == Created)
            {
                if (record.Hash != Profile.Hash) throw new InvalidOperationException("恢复记录的引擎版本不匹配。");
                var recovery = new FpsLease(Handle, ModuleBase + Profile.FpsRva, record.Original, record.Target);
                recovery.WasWritten = true;
                Files.Log("Recovered previous session: " + recovery.Restore());
            }
            File.Delete(Files.JournalPath);
        }

        internal bool Alive
        {
            get { uint code; return Handle != IntPtr.Zero && Native.GetExitCodeProcess(Handle, out code) && code == 259; }
        }

        internal void Start()
        {
            if (Active) return;
            Profile.CheckMemory(Handle, ModuleBase);
            if (!Alive) throw new InvalidOperationException("游戏已退出。");
            Files.Save(Files.JournalPath, new RestoreRecord { Pid=Pid, Created=Created, Original=Lease.Original, Target=Lease.Target, Hash=Profile.Hash });
            Lease.BeforeWrite=delegate(int original)
            {
                Files.Save(Files.JournalPath,new RestoreRecord {Pid=Pid,Created=Created,Original=original,Target=Lease.Target,Hash=Profile.Hash});
                Files.Log("Captured game-selected restore target="+original);
            };
            Active = true;
            try { Lease.Apply(); Files.Log("Enabled target=" + Lease.Target); }
            catch { Stop(); throw; }
        }

        internal void Reconfigure(int target)
        {
            if (Active) throw new InvalidOperationException("请先停止当前解锁。");
            if (!Alive) throw new InvalidOperationException("游戏已退出。");
            Profile.CheckMemory(Handle,ModuleBase);
            Lease=new FpsLease(Handle,ModuleBase+Profile.FpsRva,Native.ReadInt(Handle,ModuleBase+Profile.FpsRva),target);
            Restored=false; RestoreMessage=null;
        }

        internal void Tick()
        {
            if (!Active) return;
            if (!Alive) { Stop(); throw new InvalidOperationException("游戏已退出，已停止解锁。"); }
            Lease.Apply();
        }

        internal string Stop()
        {
            if (!Active) return RestoreMessage ?? "未启用。";
            Active = false;
            try
            {
                if (Alive)
                {
                    Profile.CheckMemory(Handle, ModuleBase);
                    RestoreMessage = Lease.Restore();
                }
                else RestoreMessage = "游戏进程已退出，临时修改已随进程清除。";
                Restored = true;
                if (File.Exists(Files.JournalPath)) File.Delete(Files.JournalPath);
                Files.Log(RestoreMessage);
                return RestoreMessage;
            }
            catch (Exception e)
            {
                Restored = false;
                RestoreMessage = "恢复未完成：" + e.Message + " 请重启游戏以恢复默认状态。";
                Files.Log(RestoreMessage);
                return RestoreMessage;
            }
        }

        public void Dispose()
        {
            if (Active) Stop();
            if (Handle != IntPtr.Zero) { Native.CloseHandle(Handle); Handle = IntPtr.Zero; }
            if (writerMutex != null) { if (ownsMutex) writerMutex.ReleaseMutex(); writerMutex.Dispose(); writerMutex = null; ownsMutex = false; }
        }
    }
}
