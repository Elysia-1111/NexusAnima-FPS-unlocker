using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace NexusAnima240
{
    internal static class StartupWorkerTests
    {
        private static void Check(bool condition,string reason){if(!condition)throw new Exception(reason);}
        private static void Run(bool apc,bool cancel)
        {
            string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"native-test"),path=Path.Combine(directory,"NexusAnima.exe");
            var startup=new Native.StartupInfo();startup.cb=(uint)Marshal.SizeOf(typeof(Native.StartupInfo));Native.ProcessInfo created;
            if(!Native.CreateProcess(path,new StringBuilder("\""+path+"\""),IntPtr.Zero,IntPtr.Zero,false,4,IntPtr.Zero,directory,ref startup,out created))throw Native.Error("Create owned suspended mock");
            bool resumed=false;WorkerBridge bridge=null;
            try
            {
                bridge=WorkerBridge.PrepareStartup(created.Process,created.Thread,created.Pid,137,apc);
                Check(Native.ResumeThread(created.Thread)!=UInt32.MaxValue,"Could not resume mock");resumed=true;
                if(cancel)
                {
                    bridge.Dispose();bridge=null;Thread.Sleep(300);
                    uint code;Check(Native.GetExitCodeProcess(created.Process,out code) && code==259,"Cancel stopped the mock game");
                }
                else
                {
                    bridge.WaitUntilReady(delegate{return false;});bridge.Start();Thread.Sleep(250);bridge.Tick();
                    Check(bridge.Current==137,"Startup worker did not set 137");
                    Check(bridge.Stop() && bridge.Current==60,"Startup worker did not restore 60");
                }
                Console.WriteLine("PASS: "+(apc?"startup APC":"startup thread")+" path "+(cancel?"cancellation leaves mock game running":"applies 137 and restores 60")+".");
            }
            finally
            {
                if(!resumed)Native.ResumeThread(created.Thread);
                if(bridge!=null)bridge.Dispose();
                Native.CloseHandle(created.Thread);Native.CloseHandle(created.Process);
                using(var process=Process.GetProcessById(created.Pid)){if(!process.HasExited){process.Kill();process.WaitForExit();}}
            }
        }
        private static int Main()
        {
            Files.Initialize();
            try{Run(false,false);Run(true,false);Run(true,true);return 0;}
            catch(Exception e){Console.WriteLine("FAIL: "+e);return 1;}
        }
    }
}
