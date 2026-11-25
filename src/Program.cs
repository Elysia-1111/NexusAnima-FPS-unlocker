using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace NexusAnima240
{
    [DataContract] internal sealed class Sample
    {
        [DataMember] public string Time;
        [DataMember] public int? MemoryTarget;
        [DataMember] public double? MeasuredFps;
        [DataMember] public string State;
    }
    [DataContract] internal sealed class Report
    {
        [DataMember] public string Mode, Time, Error, RestoreMessage, EngineHash;
        [DataMember] public bool Administrator, VerifiedProfile, Applied, Restored;
        [DataMember] public int ProcessId, OriginalTarget, RequestedTarget;
        [DataMember] public int? AfterRestoreTarget;
        [DataMember] public double? BeforeFps, AfterRestoreFps;
        [DataMember] public List<Sample> Samples = new List<Sample>();
        [DataMember] public bool Finished;
        [DataMember] public int WorkerWrites;
        [DataMember] public double ObservationSeconds;
    }

    internal static class Program
    {
        internal static bool IsAdmin { get { using (var id = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); } }
        internal static string Option(string[] args, string key, string fallback)
        {
            int i = Array.IndexOf(args, key);
            if (i < 0) return fallback;
            if (i + 1 == args.Length) throw new ArgumentException(key + " 缺少参数。");
            return args[i + 1];
        }
        internal static int ParseTarget(string[] args)
        {
            int target = Int32.Parse(Option(args, "--fps", "240"));
            if (target<1 || target>Profile.MaxFps) throw new ArgumentException("帧率必须是 1–240 之间的整数。");
            return target;
        }

        [STAThread] private static int Main(string[] args)
        {
            Files.Initialize();
            try
            {
                if (args.Contains("--self-test")) return SelfTests.Run(Option(args, "--report", Path.Combine(Files.DataDir, "self-test.txt")));
                if (args.Contains("--launch-run")) return CommandLine(args);
                Native.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Ui.Initialize();
                int fps=args.Contains("--fps") ? ParseTarget(args) : AppSettings.Load().FrameRate;
                using (var form=new MainForm(args.Contains("--launch-auto"),fps))
                {
                    if (args.Contains("--render-preview")) form.RenderPreview(Option(args,"--render-preview",null));
                    else Application.Run(form);
                }
                return 0;
            }
            catch (Exception e) { Files.Log(e.ToString()); return 1; }
        }

        private static int CommandLine(string[] args)
        {
            string mode = "launch-run-worker";
            var report = new Report { Mode=mode, Time=DateTimeOffset.Now.ToString("o"), Administrator=IsAdmin, EngineHash=Profile.Hash };
            string reportPath = Option(args, "--report", Path.Combine(Files.DataDir, mode + ".json"));
            Session session = null;
            var observation = new Stopwatch();
            try
            {
                report.RequestedTarget = ParseTarget(args);
                int seconds = Int32.Parse(Option(args, "--seconds", "180"));
                if (seconds < 1 || seconds > 600) throw new ArgumentException("测试持续时间须为 1–600 秒。");
                session=Session.Launch(Option(args,"--game-path",GameLocator.Resolve(AppSettings.Load().GamePath)),report.RequestedTarget,Files.Log,delegate { return false; });
                report.ProcessId=session.Pid;
                Thread.Sleep(2500);
                report.BeforeFps=Rtss.ReadFps(session.Pid);
                report.VerifiedProfile = true;
                report.OriginalTarget = session.Lease.Original;
                session.Start(); report.Applied = session.ConfirmedTarget.HasValue;
                observation.Start();
                long nextSample = 0, nextSave = 0;
                while (observation.Elapsed.TotalSeconds < seconds)
                {
                    session.Tick();
                    if (session.ConfirmedTarget.HasValue) report.Applied = true;
                    if (observation.ElapsedMilliseconds >= nextSample)
                    {
                        report.Samples.Add(new Sample { Time=DateTimeOffset.Now.ToString("o"), MemoryTarget=session.ConfirmedTarget, MeasuredFps=Rtss.ReadFps(session.Pid), State="active" });
                        nextSample = observation.ElapsedMilliseconds + 1000;
                    }
                    if (observation.ElapsedMilliseconds >= nextSave)
                    {
                        report.ObservationSeconds=observation.Elapsed.TotalSeconds;
                        report.WorkerWrites=session.Worker.Writes;
                        Files.Save(reportPath,report); nextSave=observation.ElapsedMilliseconds+5000;
                    }
                    Thread.Sleep(100);
                }
            }
            catch (Exception e) { report.Error = e.Message; Files.Log(e.ToString()); }
            finally
            {
                observation.Stop();
                if (session != null)
                {
                    try
                    {
                        if (session.Active) { report.RestoreMessage=session.Stop(); report.Restored=session.Restored; }
                        if (session.Lease!=null) report.OriginalTarget=session.Lease.Original;
                        if (session.Alive && report.Restored && session.Worker!=null) report.AfterRestoreTarget=session.Worker.Current;
                    }
                    catch(Exception e) { report.Error=(report.Error==null ? "" : report.Error+"; ")+"结束验证时："+e.Message; Files.Log(e.ToString()); }
                    finally
                    {
                        if(session.Worker!=null)report.WorkerWrites=session.Worker.Writes;
                        session.Dispose();
                    }
                    if (report.Applied) { Thread.Sleep(3500); report.AfterRestoreFps=Rtss.ReadFps(report.ProcessId); }
                }
                report.ObservationSeconds=observation.Elapsed.TotalSeconds; report.Finished=true;
                Files.Save(reportPath, report);
            }
            return report.Error == null && (!report.Applied || report.Restored) ? 0 : 1;
        }
    }

    internal static class SelfTests
    {
        internal static int Run(string output)
        {
            var results = new List<string>();
            IntPtr h=IntPtr.Zero, value=IntPtr.Zero;
            try
            {
                foreach (int index in Enumerable.Range(0,Profile.Code.Length))
                {
                    Profile.CheckBytes(index,Profile.Code[index]);
                    byte[] mutated=(byte[])Profile.Code[index].Clone(); mutated[0]^=1;
                    bool rejected=false; try { Profile.CheckBytes(index,mutated); } catch (InvalidOperationException) { rejected=true; }
                    if (!rejected) throw new Exception("Mismatched signature accepted");
                }
                results.Add("PASS: all four code signatures reject modified bytes.");
                if (Profile.CodeRvas[0]+6+BitConverter.ToInt32(Profile.Code[0],2)!=Profile.FpsRva || Profile.CodeRvas[1]+6+BitConverter.ToInt32(Profile.Code[1],2)!=Profile.FpsRva) throw new Exception("RIP target mismatch");
                results.Add("PASS: getter and setter resolve to the same frame target RVA.");
                value=Marshal.AllocHGlobal(4); Marshal.WriteInt32(value,60);
                h=Native.OpenProcess(0x438,false,Process.GetCurrentProcess().Id);
                if (h==IntPtr.Zero) throw Native.Error("self-test OpenProcess");
                var lease=new FpsLease(h,value.ToInt64(),60,240);
                lease.Apply(); if (Marshal.ReadInt32(value)!=240) throw new Exception("Write/readback failed");
                lease.Restore(); if (Marshal.ReadInt32(value)!=60) throw new Exception("Restore failed");
                results.Add("PASS: live process-memory write 60 -> 240 and restore 240 -> 60 (own test process only).");
                var late=new FpsLease(h,value.ToInt64(),240,240);
                bool durableBeforeWrite=false;
                late.BeforeWrite=delegate(int original) { durableBeforeWrite=original==60 && Marshal.ReadInt32(value)==60; };
                late.Apply(); late.Restore();
                if (!durableBeforeWrite || late.Original!=60 || Marshal.ReadInt32(value)!=60) throw new Exception("Late initialization restored loading-screen target");
                results.Add("PASS: delayed game cap 240 -> 60 is saved BEFORE writing 240 and is restored to 60.");
                var noJournal=new FpsLease(h,value.ToInt64(),60,240);
                noJournal.BeforeWrite=delegate(int original) { throw new IOException("simulated journal failure"); };
                bool journalFailure=false; try { noJournal.Apply(); } catch (IOException) { journalFailure=true; }
                if (!journalFailure || Marshal.ReadInt32(value)!=60) throw new Exception("Wrote despite failed recovery journal");
                results.Add("PASS: failed recovery-journal persistence prevents memory writes.");
                lease.Apply(); Marshal.WriteInt32(value,30); lease.Restore();
                if (Marshal.ReadInt32(value)!=30) throw new Exception("Clobbered new game setting");
                results.Add("PASS: restoration preserves an intervening game-selected value.");
                Marshal.WriteInt32(value,777777); bool invalid=false;
                try { lease.Apply(); } catch (InvalidOperationException) { invalid=true; }
                if (!invalid || Marshal.ReadInt32(value)!=777777) throw new Exception("Invalid memory value written");
                results.Add("PASS: unexpected values are rejected without writing.");
                bool badTarget=false; try { new FpsLease(h,value.ToInt64(),60,10000); } catch (ArgumentException) { badTarget=true; }
                if (!badTarget) throw new Exception("Invalid target allowed");
                results.Add("PASS: unsupported requested frame rates are rejected.");
                foreach(int target in new[] {1,137,239,240})
                {
                    Marshal.WriteInt32(value,60);
                    var range=new FpsLease(h,value.ToInt64(),60,target);range.Apply();
                    if(Marshal.ReadInt32(value)!=target)throw new Exception("Integer target failed: "+target);
                    range.Restore();if(Marshal.ReadInt32(value)!=60)throw new Exception("Integer restoration failed");
                    if(Program.ParseTarget(new[] {"--fps",target.ToString()})!=target)throw new Exception("CLI target parse failed");
                }
                foreach(string invalidInput in new[] {"0","-1","241","360","60.5","abc"})
                {
                    bool rejected=false;try{Program.ParseTarget(new[] {"--fps",invalidInput});}catch(ArgumentException){rejected=true;}catch(FormatException){rejected=true;}
                    if(!rejected)throw new Exception("Invalid FPS accepted: "+invalidInput);
                }
                results.Add("PASS: arbitrary integers 1/137/239/240 accepted; 0/-1/241/360/decimal/text rejected.");
                string test=Path.Combine(Files.DataDir,"self-test-restore.json");
                Files.Save(test,new RestoreRecord {Pid=123,Created=456,Original=60,Target=240,Hash=Profile.Hash});
                RestoreRecord read=Files.Load<RestoreRecord>(test);
                if (read.Created!=456 || read.Original!=60 || read.Hash!=Profile.Hash) throw new Exception("Journal round trip failed");
                File.Delete(test); results.Add("PASS: crash recovery record is durably written and read.");
                File.WriteAllLines(output,results); return 0;
            }
            catch (Exception e) { results.Add("FAIL: "+e); File.WriteAllLines(output,results); return 1; }
            finally { if (h!=IntPtr.Zero) Native.CloseHandle(h); if (value!=IntPtr.Zero) Marshal.FreeHGlobal(value); }
        }
    }
}
