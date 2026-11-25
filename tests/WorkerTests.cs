using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;

namespace NexusAnima240
{
    internal static class WorkerTests
    {
        private static void Check(bool condition,string reason){if(!condition)throw new Exception(reason);}
        private static void Run(Action<Process,IntPtr,long> test)
        {
            string directory=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"native-test");
            using(var process=Process.Start(new ProcessStartInfo(Path.Combine(directory,"NexusAnima.exe")){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=directory}))
            {
                try
                {
                    IntPtr handle=process.Handle;long module=0;var timer=Stopwatch.StartNew();
                    while(module==0 && timer.ElapsedMilliseconds<3000){module=Native.FindUnityModule(handle,Path.Combine(directory,"UnityPlayer.dll"));if(module==0)Thread.Sleep(25);}
                    Check(module!=0,"Mock engine did not load");
                    for(int i=0;i<Profile.Code.Length;i++)
                    {
                        UIntPtr written;
                        if(!Native.WriteProcessMemory(handle,new IntPtr(module+Profile.CodeRvas[i]),Profile.Code[i],(UIntPtr)Profile.Code[i].Length,out written))throw Native.Error("Initialize owned mock process");
                    }
                    test(process,handle,module);
                }
                finally{if(!process.HasExited){process.Kill();process.WaitForExit();}}
            }
        }
        private static int Main(string[] args)
        {
            Files.Initialize();
            if(args.Length>0 && args[0]=="--owner")
            {
                int pid=Int32.Parse(args[1]);IntPtr handle=Native.OpenProcess(0x10043a,false,pid);
                if(handle==IntPtr.Zero)throw Native.Error("Open owned mock");
                var bridge=WorkerBridge.Load(handle,pid,137);bridge.Start();
                File.WriteAllText(args[2],"ready");
                while(true){bridge.Tick();Thread.Sleep(100);}
            }
            try
            {
                Run(delegate(Process process,IntPtr handle,long module)
                {
                    using(var bridge=WorkerBridge.Load(handle,process.Id,137))
                    {
                        bridge.Start();Thread.Sleep(150);bridge.Tick();
                        Check(bridge.Current==137 && Native.ReadInt(handle,module+Profile.FpsRva)==137,"Worker did not apply 137");
                        Native.WriteInt(handle,module+Profile.FpsRva,90);Thread.Sleep(250);bridge.Tick();
                        Check(bridge.Current==137 && bridge.Original==90,"Worker lost a later game-selected cap");
                        Check(bridge.Stop() && Native.ReadInt(handle,module+Profile.FpsRva)==90,"Worker did not restore the later cap");
                    }
                });
                Console.WriteLine("PASS: production worker applies 137, maintains it after game reset, and restores the latest game-selected 90.");
                Run(delegate(Process process,IntPtr handle,long module)
                {
                    using(var bridge=WorkerBridge.Load(handle,process.Id,240))
                    {
                        bridge.Start();
                        using(var mapping=MemoryMappedFile.OpenExisting("Local\\NexusAnimaFPS_v2_"+process.Id))
                        using(var view=mapping.CreateViewAccessor()){view.Write(8,360);}
                        Thread.Sleep(250);
                        Check(bridge.State==4 && bridge.ErrorCode==5 && Native.ReadInt(handle,module+Profile.FpsRva)==60,"Invalid IPC target was accepted or not restored");
                    }
                });
                Console.WriteLine("PASS: worker independently rejects 360 and restores 60.");
                Run(delegate(Process process,IntPtr handle,long module)
                {
                    using(var bridge=WorkerBridge.Load(handle,process.Id,240))
                    {
                        bridge.Start();Native.WriteInt(handle,module+Profile.FpsRva,9999);Thread.Sleep(250);
                        Check(bridge.State==4 && bridge.ErrorCode==7 && Native.ReadInt(handle,module+Profile.FpsRva)==9999,"Worker overwrote an unexpected game value");
                    }
                });
                Console.WriteLine("PASS: unexpected game memory value stops the worker without overwriting it.");
                Run(delegate(Process process,IntPtr handle,long module)
                {
                    using(var bridge=WorkerBridge.Load(handle,process.Id,137))
                    {
                        bridge.Start();Thread.Sleep(11000);
                        Check(bridge.State==3 && bridge.RestoreResult==1 && Native.ReadInt(handle,module+Profile.FpsRva)==60,"Lost owner heartbeat did not restore 60");
                    }
                });
                Console.WriteLine("PASS: loss of owner heartbeat restores 60 and stops the worker automatically.");
                Run(delegate(Process process,IntPtr handle,long module)
                {
                    string ready=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"worker-owner-"+Guid.NewGuid().ToString("N")+".ready");
                    using(var owner=Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--owner "+process.Id+" \""+ready+"\""){UseShellExecute=false,CreateNoWindow=true}))
                    {
                        try
                        {
                            var timer=Stopwatch.StartNew();while(!File.Exists(ready) && !owner.HasExited && timer.ElapsedMilliseconds<5000)Thread.Sleep(25);
                            Check(File.Exists(ready),"Crash-test owner did not activate worker");
                            using(var mapping=MemoryMappedFile.OpenExisting("Local\\NexusAnimaFPS_v2_"+process.Id))
                            using(var view=mapping.CreateViewAccessor())
                            {
                                owner.Kill();owner.WaitForExit();Thread.Sleep(500);
                                Check(view.ReadInt32(24)==3 && view.ReadInt32(52)==1 && Native.ReadInt(handle,module+Profile.FpsRva)==60,"Owner process exit did not restore 60");
                            }
                        }
                        finally{if(!owner.HasExited){owner.Kill();owner.WaitForExit();}if(File.Exists(ready))File.Delete(ready);}
                    }
                });
                Console.WriteLine("PASS: terminating the owning test tool restores 60 while the mock game stays running.");
                Run(delegate(Process process,IntPtr handle,long module)
                {
                    Native.WriteInt(handle,module+Profile.CodeRvas[0],0);
                    bool rejected=false;try{using(var bridge=WorkerBridge.Load(handle,process.Id,137)){} }catch(InvalidOperationException){rejected=true;}
                    Check(rejected && Native.ReadInt(handle,module+Profile.FpsRva)==60,"Mismatched engine was accepted");
                });
                Console.WriteLine("PASS: engine-signature mismatch prevents activation.");
                return 0;
            }
            catch(Exception e){Console.WriteLine("FAIL: "+e);return 1;}
        }
    }
}
