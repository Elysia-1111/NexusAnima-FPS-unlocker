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
        internal int? LastConfirmedValue;
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
            if (current == Target) { LastConfirmedValue=current; return; }
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
            LastConfirmedValue=Target;
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
        internal long Created;
        internal string GamePath;
        internal IntPtr Handle;
        internal FpsLease Lease;
        internal WorkerBridge Worker;
        internal bool Active;
        internal bool Launched;
        internal bool Restored;
        internal string RestoreMessage;
        private Mutex writerMutex;
        private bool ownsMutex;
        private IntPtr startupThread;
        internal int? ConfirmedTarget { get { return Worker==null || Worker.State!=2 ? (int?)null : Worker.Current; } }

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
                // Prepare the FPS module using the handles returned for this launch,
                // then always resume the primary thread, including on setup failure.
                if (!Native.CreateProcess(s.GamePath,new StringBuilder("\""+s.GamePath+"\""),IntPtr.Zero,IntPtr.Zero,false,4,IntPtr.Zero,Path.GetDirectoryName(s.GamePath),ref startup,out created))
                    throw Native.Error("启动游戏失败");
                s.Handle=created.Process; s.Pid=created.Pid;s.startupThread=created.Thread;
                s.Launched=true;
                s.Created=Native.CreationTime(s.Handle);
                Files.Log("Created game pid="+s.Pid+" with startup preparation enabled.");
                progress("正在准备帧率模块…");
                try{s.Worker=WorkerBridge.PrepareStartup(s.Handle,s.startupThread,s.Pid,target);}
                finally{s.ResumeStartup();}
                progress("游戏已启动，等待引擎准备就绪…");
                s.Worker.WaitUntilReady(delegate
                {
                    if(!s.Alive)throw new InvalidOperationException("游戏在初始化阶段已退出。");
                    return cancel();
                });
                s.Lease=new FpsLease(s.Handle,0,s.Worker.Original,target);
                if(File.Exists(Files.JournalPath))File.Delete(Files.JournalPath);
                Files.Log("Game worker validated engine; original="+s.Worker.Original+".");
                return s;
            }
            catch { s.Dispose(); throw; }
        }

        private void ResumeStartup()
        {
            if(startupThread==IntPtr.Zero)return;
            if(Native.ResumeThread(startupThread)==UInt32.MaxValue)throw Native.Error("继续启动游戏失败");
            Native.CloseHandle(startupThread);startupThread=IntPtr.Zero;
            Files.Log("Primary game thread resumed.");
        }

        internal bool Alive
        {
            get
            {
                if (Handle==IntPtr.Zero) return false;
                uint code;
                if (!Native.GetExitCodeProcess(Handle,out code)) throw Native.Error("无法确认游戏运行状态");
                return code==259;
            }
        }

        internal void Start()
        {
            if (Active) return;
            if (!Alive) throw new InvalidOperationException("游戏已退出。");
            Files.Save(Files.JournalPath, new RestoreRecord { Pid=Pid, Created=Created, Original=Lease.Original, Target=Lease.Target, Hash=Profile.Hash });
            Active = true;
            try { Worker.Start();SynchronizeWorker();Files.Log("Enabled in-process target=" + Lease.Target); }
            catch { Stop(); throw; }
        }

        internal void Reconfigure(int target)
        {
            throw new InvalidOperationException("请正常退出游戏后，再选择新的帧率启动。");
        }

        private void SynchronizeWorker()
        {
            int original=Worker.Original;
            if(Worker.WasWritten && original!=Lease.Original)
            {
                Files.Save(Files.JournalPath,new RestoreRecord {Pid=Pid,Created=Created,Original=original,Target=Lease.Target,Hash=Profile.Hash});
                Files.Log("Worker captured game-selected restore target="+original);
            }
            Lease.Original=original;Lease.LastConfirmedValue=Worker.Current;Lease.WasWritten=Worker.WasWritten;
        }

        internal void Tick()
        {
            if (!Active) return;
            if (!Alive) { Stop(); throw new InvalidOperationException("游戏已退出，已停止解锁。"); }
            Worker.Tick();SynchronizeWorker();
        }

        internal string Stop()
        {
            if (!Active) return RestoreMessage ?? "未启用。";
            Active = false;
            try
            {
                if (Alive)
                {
                    bool restored=Worker.Stop();
                    Lease.Original=Worker.Original;Lease.LastConfirmedValue=Worker.Current;
                    if(!restored)throw new InvalidOperationException("帧率模块未确认恢复完成，当前帧率状态无法确认。");
                    Lease.WasWritten=false;
                    RestoreMessage=Worker.RestoreResult==1 ? "已恢复游戏帧率 "+Worker.Current+"。" : Worker.RestoreResult==2 ? "保留游戏后来选择的帧率设置。" : "未修改帧率，无需恢复。";
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
            if(startupThread!=IntPtr.Zero){try{ResumeStartup();}catch(Exception e){Files.Log(e.ToString());}}
            if (Active) Stop();
            if (Worker!=null){Worker.Dispose();Worker=null;}
            if (Handle != IntPtr.Zero) { Native.CloseHandle(Handle); Handle = IntPtr.Zero; }
            if (writerMutex != null) { if (ownsMutex) writerMutex.ReleaseMutex(); writerMutex.Dispose(); writerMutex = null; ownsMutex = false; }
        }
    }
}
