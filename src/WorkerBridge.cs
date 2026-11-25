using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace NexusAnima240
{
    internal sealed class WorkerBridge : IDisposable
    {
        [DllImport("kernel32.dll",SetLastError=true)] private static extern IntPtr VirtualAllocEx(IntPtr process,IntPtr address,UIntPtr size,uint type,uint protect);
        [DllImport("kernel32.dll",SetLastError=true)] private static extern bool VirtualFreeEx(IntPtr process,IntPtr address,UIntPtr size,uint type);
        [DllImport("kernel32.dll",SetLastError=true)] private static extern IntPtr CreateRemoteThread(IntPtr process,IntPtr attributes,UIntPtr stack,IntPtr start,IntPtr parameter,uint flags,out uint id);
        [DllImport("kernel32.dll",SetLastError=true)] private static extern bool VirtualProtectEx(IntPtr process,IntPtr address,UIntPtr size,uint protection,out uint previous);
        [DllImport("kernel32.dll",SetLastError=true)] private static extern bool FlushInstructionCache(IntPtr process,IntPtr address,UIntPtr size);
        [DllImport("kernel32.dll",SetLastError=true)] private static extern uint QueueUserAPC(IntPtr routine,IntPtr thread,UIntPtr argument);
        [DllImport("kernel32.dll",SetLastError=true)] private static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
        [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetExitCodeThread(IntPtr handle,out uint code);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr GetModuleHandle(string name);
        [DllImport("kernel32.dll",CharSet=CharSet.Ansi,SetLastError=true)] private static extern IntPtr GetProcAddress(IntPtr module,string name);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool GetModuleHandleEx(uint flags,IntPtr address,out IntPtr module);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern uint GetModuleFileName(IntPtr module,StringBuilder name,uint size);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern IntPtr LoadLibraryEx(string path,IntPtr file,uint flags);
        [DllImport("kernel32.dll",SetLastError=true)] private static extern bool FreeLibrary(IntPtr module);

        private MemoryMappedFile mapping;
        private MemoryMappedViewAccessor view;
        private IntPtr thread;
        private bool startupThread;
        internal int State {get {return view.ReadInt32(24);}}
        internal int ErrorCode {get {return view.ReadInt32(28);}}
        internal int Original {get {return view.ReadInt32(32);}}
        internal int Current {get {return view.ReadInt32(36);}}
        internal int Writes {get {return view.ReadInt32(40);}}
        internal bool WasWritten {get {return view.ReadInt32(48)!=0;}}
        internal int RestoreResult {get {return view.ReadInt32(52);}}

        private static string ExtractWorker()
        {
            byte[] bytes;
            using(var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("NexusAnimaFpsWorker.dll"))
            {
                if(resource==null)throw new InvalidOperationException("缺少内置帧率模块，请重新下载完整工具。");
                using(var stream=new MemoryStream()){resource.CopyTo(stream);bytes=stream.ToArray();}
            }
            string hash;
            using(var sha=System.Security.Cryptography.SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
            string directory=Path.Combine(Files.DataDir,"native");Directory.CreateDirectory(directory);
            string path=Path.Combine(directory,"NexusAnimaFpsWorker-"+hash.Substring(0,16)+".dll");
            if(!File.Exists(path))
            {
                string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                try{File.WriteAllBytes(temp,bytes);File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}
            }
            if(Profile.Sha256(path)!=hash)throw new InvalidDataException("帧率模块校验失败，已拒绝加载。");
            return Path.GetFullPath(path);
        }
        private static IntPtr RemoteSystemFunction(IntPtr process,string name,string library="kernel32.dll")
        {
            IntPtr address=GetProcAddress(GetModuleHandle(library),name),module;
            if(address==IntPtr.Zero || !GetModuleHandleEx(6,address,out module))throw Native.Error("读取系统函数信息失败");
            var path=new StringBuilder(32768);
            if(GetModuleFileName(module,path,(uint)path.Capacity)==0)throw Native.Error("读取系统模块信息失败");
            long remote=Native.FindImageByMemoryMap(process,Path.GetFileName(path.ToString()));
            if(remote==0)throw new InvalidOperationException("游戏系统模块尚未就绪。");
            return new IntPtr(remote+address.ToInt64()-module.ToInt64());
        }
        internal static WorkerBridge PrepareStartup(IntPtr process,IntPtr primaryThread,int pid,int target,bool useApc=false)
        {
            if(target<1 || target>240)throw new ArgumentOutOfRangeException("target");
            var bridge=new WorkerBridge();
            try
            {
                bridge.mapping=MemoryMappedFile.CreateNew("Local\\NexusAnimaFPS_v2_"+pid,64,MemoryMappedFileAccess.ReadWrite);
                bridge.view=bridge.mapping.CreateViewAccessor(0,64,MemoryMappedFileAccess.ReadWrite);
                bridge.view.Write(0,0x5046414e);bridge.view.Write(4,2);bridge.view.Write(8,target);
                bridge.view.Write(16,Process.GetCurrentProcess().Id);bridge.view.Write(56,pid);bridge.Heartbeat();
                byte[] code;
                using(var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("NexusAnimaBootstrap.bin"))
                {
                    if(resource==null)throw new InvalidOperationException("缺少内置启动模块。");
                    using(var stream=new MemoryStream()){resource.CopyTo(stream);code=stream.ToArray();}
                }
                if(code.Length==0 || code.Length>4096)throw new InvalidDataException("内置启动模块格式异常。");
                byte[] path=Encoding.Unicode.GetBytes(ExtractWorker()+"\0"),symbol=Encoding.ASCII.GetBytes("NexusSchedule\0");
                byte[] context=new byte[72+path.Length+symbol.Length];
                IntPtr remoteContext=VirtualAllocEx(process,IntPtr.Zero,(UIntPtr)context.Length,0x3000,4);
                IntPtr remoteCode=VirtualAllocEx(process,IntPtr.Zero,(UIntPtr)code.Length,0x3000,4);
                if(remoteContext==IntPtr.Zero || remoteCode==IntPtr.Zero)throw Native.Error("无法准备启动模块");
                Buffer.BlockCopy(BitConverter.GetBytes(RemoteSystemFunction(process,"LdrLoadDll","ntdll.dll").ToInt64()),0,context,0,8);
                Buffer.BlockCopy(BitConverter.GetBytes(RemoteSystemFunction(process,"LdrGetProcedureAddress","ntdll.dll").ToInt64()),0,context,8,8);
                Buffer.BlockCopy(BitConverter.GetBytes((ushort)(path.Length-2)),0,context,32,2);Buffer.BlockCopy(BitConverter.GetBytes((ushort)path.Length),0,context,34,2);
                Buffer.BlockCopy(BitConverter.GetBytes(remoteContext.ToInt64()+72),0,context,40,8);
                Buffer.BlockCopy(BitConverter.GetBytes((ushort)(symbol.Length-1)),0,context,48,2);Buffer.BlockCopy(BitConverter.GetBytes((ushort)symbol.Length),0,context,50,2);
                Buffer.BlockCopy(BitConverter.GetBytes(remoteContext.ToInt64()+72+path.Length),0,context,56,8);
                Buffer.BlockCopy(path,0,context,72,path.Length);Buffer.BlockCopy(symbol,0,context,72+path.Length,symbol.Length);
                UIntPtr written;
                if(!Native.WriteProcessMemory(process,remoteContext,context,(UIntPtr)context.Length,out written) || written.ToUInt64()!=(ulong)context.Length)throw Native.Error("启动模块参数写入失败");
                if(!Native.WriteProcessMemory(process,remoteCode,code,(UIntPtr)code.Length,out written) || written.ToUInt64()!=(ulong)code.Length)throw Native.Error("启动模块写入失败");
                uint previous;
                if(!VirtualProtectEx(process,remoteCode,(UIntPtr)code.Length,0x20,out previous) || !FlushInstructionCache(process,remoteCode,(UIntPtr)code.Length))throw Native.Error("启动模块初始化失败");
                uint tid;
                if(!useApc)bridge.thread=CreateRemoteThread(process,IntPtr.Zero,UIntPtr.Zero,remoteCode,remoteContext,0,out tid);
                bridge.startupThread=true;
                if(bridge.thread==IntPtr.Zero)
                {
                    if(!useApc && Marshal.GetLastWin32Error()!=5)throw Native.Error("启动模块线程创建失败");
                    if(QueueUserAPC(remoteCode,primaryThread,new UIntPtr((ulong)remoteContext.ToInt64()))==0)throw Native.Error("游戏未允许准备启动模块");
                    Files.Log("FPS module queued on the retained startup thread.");
                }
                else
                {
                    var wait=Stopwatch.StartNew();
                    while(bridge.view.ReadInt32(60)==0 && wait.ElapsedMilliseconds<10000)
                    {
                        bridge.Heartbeat();
                        if(WaitForSingleObject(bridge.thread,0)==0){uint exit;GetExitCodeThread(bridge.thread,out exit);if(exit!=0)throw new InvalidOperationException("启动模块返回错误 0x"+exit.ToString("X8")+"。");}
                        Thread.Sleep(25);
                    }
                    if(bridge.view.ReadInt32(60)==0)throw new InvalidOperationException("启动模块未能及时加载。");
                    Files.Log("FPS module loaded before the primary game thread was resumed.");
                }
                // Startup code/context stay allocated until game exit. They are small,
                // and keeping them avoids freeing an APC's active stack/arguments.
                return bridge;
            }
            catch{bridge.Dispose();throw;}
        }
        internal void WaitUntilReady(Func<bool> cancel)
        {
            var wait=Stopwatch.StartNew();
            while(wait.ElapsedMilliseconds<90000)
            {
                Heartbeat();
                if(cancel())throw new OperationCanceledException("已取消等待；游戏继续正常运行。");
                int state=State;if(state==1)return;
                if(state>=3)throw new InvalidOperationException("帧率模块未能初始化，代码 "+ErrorCode+"。");
                if(thread!=IntPtr.Zero && WaitForSingleObject(thread,0)==0){uint exit;GetExitCodeThread(thread,out exit);if(exit!=0)throw new InvalidOperationException("启动模块返回错误 0x"+exit.ToString("X8")+"。");}
                Thread.Sleep(50);
            }
            throw new InvalidOperationException("等待游戏引擎超时。");
        }
        internal static WorkerBridge Load(IntPtr process,int pid,int target)
        {
            if(target<1 || target>240)throw new ArgumentOutOfRangeException("target");
            var bridge=new WorkerBridge();
            try
            {
                bridge.mapping=MemoryMappedFile.CreateNew("Local\\NexusAnimaFPS_v2_"+pid,64,MemoryMappedFileAccess.ReadWrite);
                bridge.view=bridge.mapping.CreateViewAccessor(0,64,MemoryMappedFileAccess.ReadWrite);
                bridge.view.Write(0,0x5046414e);bridge.view.Write(4,2);bridge.view.Write(8,target);
                bridge.view.Write(16,Process.GetCurrentProcess().Id);bridge.view.Write(56,pid);bridge.Heartbeat();
                string dll=ExtractWorker();byte[] path=Encoding.Unicode.GetBytes(dll+"\0");
                IntPtr loader=RemoteSystemFunction(process,"LoadLibraryW");
                IntPtr argument=VirtualAllocEx(process,IntPtr.Zero,(UIntPtr)path.Length,0x3000,4),loadThread=IntPtr.Zero;
                if(argument==IntPtr.Zero)throw Native.Error("无法准备帧率模块路径");
                bool completed=false;
                try
                {
                    UIntPtr written;
                    if(!Native.WriteProcessMemory(process,argument,path,(UIntPtr)path.Length,out written))throw Native.Error("无法传入帧率模块路径");
                    if(written.ToUInt64()!=(ulong)path.Length)throw new InvalidOperationException("帧率模块路径写入不完整。");
                    uint tid;loadThread=CreateRemoteThread(process,IntPtr.Zero,UIntPtr.Zero,loader,argument,0,out tid);
                    if(loadThread==IntPtr.Zero)throw Native.Error("游戏未允许加载帧率模块");
                    uint wait=WaitForSingleObject(loadThread,10000);completed=wait==0;
                    if(!completed)throw new InvalidOperationException("等待帧率模块加载超时。");
                }
                finally
                {
                    if(loadThread!=IntPtr.Zero)Native.CloseHandle(loadThread);
                    // A timed-out loader may still be reading its argument.
                    if(completed || loadThread==IntPtr.Zero)VirtualFreeEx(process,argument,UIntPtr.Zero,0x8000);
                }
                long remoteModule=Native.FindImageByMemoryMap(process,Path.GetFileName(dll));
                if(remoteModule==0)throw new InvalidOperationException("帧率模块未加载成功，游戏继续正常运行。");
                IntPtr local=LoadLibraryEx(dll,IntPtr.Zero,1);long entry;
                if(local==IntPtr.Zero)throw Native.Error("读取内置模块信息失败");
                try
                {
                    IntPtr start=GetProcAddress(local,"NexusStart");
                    if(start==IntPtr.Zero)throw new InvalidOperationException("帧率模块入口不存在。");
                    entry=remoteModule+start.ToInt64()-local.ToInt64();
                }
                finally{FreeLibrary(local);}
                bridge.Heartbeat();uint workerTid;
                bridge.thread=CreateRemoteThread(process,IntPtr.Zero,UIntPtr.Zero,new IntPtr(entry),IntPtr.Zero,0,out workerTid);
                if(bridge.thread==IntPtr.Zero)throw Native.Error("游戏未允许启动帧率模块");
                bridge.WaitForState(1,5000);
                Files.Log("In-process FPS worker ready; runtime communication uses shared memory.");
                return bridge;
            }
            catch{bridge.Dispose();throw;}
        }
        private void Heartbeat(){view.Write(20,unchecked((int)Native.GetTickCount()));Thread.MemoryBarrier();}
        private void WaitForState(int expected,int milliseconds)
        {
            var watch=Stopwatch.StartNew();
            while(watch.ElapsedMilliseconds<milliseconds)
            {
                Heartbeat();int state=State;
                if(state==expected)return;
                if(state==4)throw new InvalidOperationException("帧率模块停止，校验代码 "+ErrorCode+"。");
                if(!startupThread && thread!=IntPtr.Zero && WaitForSingleObject(thread,0)==0)
                {
                    uint code;GetExitCodeThread(thread,out code);throw new InvalidOperationException("帧率模块已退出，代码 "+code+"。");
                }
                Thread.Sleep(25);
            }
            throw new InvalidOperationException("帧率模块未及时响应。");
        }
        internal void Start(){Heartbeat();view.Write(12,1);Thread.MemoryBarrier();WaitForState(2,3000);}
        internal void Tick()
        {
            Heartbeat();int state=State;
            if(state!=2)throw new InvalidOperationException("帧率维持已停止，模块状态 "+state+"，代码 "+ErrorCode+"。");
            uint last=unchecked((uint)view.ReadInt32(44));
            if(unchecked(Native.GetTickCount()-last)>5000)throw new InvalidOperationException("帧率模块失去响应，已请求恢复。");
        }
        internal bool Stop()
        {
            if(view==null)return true;
            view.Write(12,2);Thread.MemoryBarrier();
            var watch=Stopwatch.StartNew();
            while(State<3 && watch.ElapsedMilliseconds<3000){Heartbeat();Thread.Sleep(25);}
            return State>=3 && RestoreResult>=1 && RestoreResult<=3;
        }
        public void Dispose()
        {
            if(view!=null){try{if(State<3)Stop();}catch(Exception e){Files.Log(e.Message);}view.Dispose();view=null;}
            if(mapping!=null){mapping.Dispose();mapping=null;}
            if(thread!=IntPtr.Zero){Native.CloseHandle(thread);thread=IntPtr.Zero;}
        }
    }
}
