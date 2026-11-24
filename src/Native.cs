using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace NexusAnima240
{
    internal static class Native
    {
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool ReadProcessMemory(IntPtr handle, IntPtr address, byte[] buffer, UIntPtr size, out UIntPtr read);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool WriteProcessMemory(IntPtr handle, IntPtr address, byte[] buffer, UIntPtr size, out UIntPtr written);
        [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)] internal static extern bool QueryFullProcessImageName(IntPtr handle, uint flags, StringBuilder path, ref uint size);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool GetProcessTimes(IntPtr handle, out long creation, out long exit, out long kernel, out long user);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool GetExitCodeProcess(IntPtr handle, out uint code);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern UIntPtr VirtualQueryEx(IntPtr handle, IntPtr address, out MemoryInfo info, UIntPtr length);
        [DllImport("psapi.dll", SetLastError=true)] internal static extern bool EnumProcessModulesEx(IntPtr handle, [Out] IntPtr[] modules, uint size, out uint needed, uint filter);
        [DllImport("psapi.dll", SetLastError=true, CharSet=CharSet.Unicode)] internal static extern uint GetModuleFileNameEx(IntPtr handle, IntPtr module, StringBuilder path, uint size);
        [DllImport("psapi.dll", SetLastError=true, CharSet=CharSet.Unicode)] internal static extern uint GetMappedFileName(IntPtr handle, IntPtr address, StringBuilder path, uint size);
        [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)] internal static extern IntPtr OpenFileMapping(uint access, bool inherit, string name);
        [DllImport("kernel32.dll", SetLastError=true)] internal static extern IntPtr MapViewOfFile(IntPtr mapping, uint access, uint high, uint low, UIntPtr size);
        [DllImport("kernel32.dll")] internal static extern bool UnmapViewOfFile(IntPtr address);
        [DllImport("kernel32.dll")] internal static extern uint GetTickCount();
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)] internal static extern bool CreateProcess(string application, StringBuilder commandLine, IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint flags, IntPtr environment, string directory, ref StartupInfo startup, out ProcessInfo info);
        [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] internal struct StartupInfo
        {
            public uint cb; public string reserved, desktop, title;
            public uint x, y, width, height, xChars, yChars, fillAttribute, flags;
            public ushort showWindow, reservedBytes;
            public IntPtr reservedPointer, stdin, stdout, stderr;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct ProcessInfo
        { public IntPtr Process, Thread; public int Pid, Tid; }

        [StructLayout(LayoutKind.Sequential)] internal struct MemoryInfo
        {
            public IntPtr BaseAddress, AllocationBase;
            public uint AllocationProtect;
            public UIntPtr RegionSize;
            public uint State, Protect, Type;
        }

        internal static Exception Error(string operation)
        {
            int code = Marshal.GetLastWin32Error();
            return new Win32Exception(code, operation + "，错误 " + code + ": " + new Win32Exception(code).Message);
        }

        internal static string ImagePath(int pid)
        {
            IntPtr h = OpenProcess(0x1000, false, pid);
            if (h == IntPtr.Zero) throw Error("读取游戏路径失败");
            try { return ImagePath(h); } finally { CloseHandle(h); }
        }

        internal static string ImagePath(IntPtr h)
        {
            StringBuilder path = new StringBuilder(32768);
            uint length = (uint)path.Capacity;
            if (!QueryFullProcessImageName(h, 0, path, ref length)) throw Error("读取游戏路径失败");
            return path.ToString();
        }

        internal static long CreationTime(IntPtr h)
        {
            long start, exit, kernel, user;
            if (!GetProcessTimes(h, out start, out exit, out kernel, out user)) throw Error("读取进程标识失败");
            return start;
        }

        internal static long FindImageByMemoryMap(IntPtr h, string expectedFileName)
        {
            // A documented query fallback when PSAPI cannot read the process module list.
            // No access-right changes, privilege adjustment, injections or hooks.
            long address=0, lastAllocation=-1, found=0;
            for (int regions=0; regions<100000 && address<0x7fffffff0000; regions++)
            {
                MemoryInfo info;
                if (VirtualQueryEx(h,new IntPtr(address),out info,(UIntPtr)Marshal.SizeOf(typeof(MemoryInfo))).ToUInt64()==0)
                {
                    int error=Marshal.GetLastWin32Error();
                    if (error==87 && address>0x10000) break;
                    throw Error("读取进程内存映射失败");
                }
                long next=info.BaseAddress.ToInt64()+(long)info.RegionSize.ToUInt64();
                if (next<=address) throw new InvalidOperationException("进程内存映射边界异常。");
                long allocation=info.AllocationBase.ToInt64();
                if (info.Type==0x1000000 && allocation!=lastAllocation)
                {
                    lastAllocation=allocation;
                    StringBuilder name=new StringBuilder(32768);
                    if (GetMappedFileName(h,info.BaseAddress,name,(uint)name.Capacity)>0 &&
                        String.Equals(Path.GetFileName(name.ToString()),expectedFileName,StringComparison.OrdinalIgnoreCase))
                    {
                        if (found!=0 && found!=allocation) throw new InvalidOperationException("发现多个同名引擎模块，已停止。");
                        found=allocation;
                    }
                }
                address=next;
            }
            return found;
        }

        internal static long FindUnityModule(IntPtr h, string expectedPath)
        {
            IntPtr[] mods=new IntPtr[2048]; uint needed;
            bool available=EnumProcessModulesEx(h,mods,(uint)(mods.Length*IntPtr.Size),out needed,3);
            if (!available) return FindImageByMemoryMap(h,"UnityPlayer.dll");
            if (needed>mods.Length*IntPtr.Size) throw new InvalidOperationException("模块列表超出边界，已停止。");
            long found=0;
            for (int i=0;i<needed/IntPtr.Size;i++)
            {
                StringBuilder name=new StringBuilder(32768);
                if (GetModuleFileNameEx(h,mods[i],name,(uint)name.Capacity)>0 && String.Equals(name.ToString(),expectedPath,StringComparison.OrdinalIgnoreCase))
                { if (found!=0) throw new InvalidOperationException("游戏引擎模块不唯一。"); found=mods[i].ToInt64(); }
            }
            return found;
        }

        internal static byte[] Read(IntPtr h, long address, int size)
        {
            byte[] data = new byte[size]; UIntPtr read;
            if (!ReadProcessMemory(h, new IntPtr(address), data, (UIntPtr)size, out read) || read.ToUInt64() != (ulong)size)
                throw Error("读取帧率内存失败");
            return data;
        }

        internal static int ReadInt(IntPtr h, long address) { return BitConverter.ToInt32(Read(h, address, 4), 0); }

        internal static void WriteInt(IntPtr h, long address, int value)
        {
            MemoryInfo info;
            if (VirtualQueryEx(h, new IntPtr(address), out info, (UIntPtr)Marshal.SizeOf(typeof(MemoryInfo))).ToUInt64() == 0)
                throw Error("无法检查内存页");
            uint access = info.Protect & 0xff;
            if (info.State != 0x1000 || (info.Protect & 0x100) != 0 || (access != 0x04 && access != 0x08) ||
                address < info.BaseAddress.ToInt64() || address + 4 > info.BaseAddress.ToInt64() + (long)info.RegionSize.ToUInt64())
                throw new InvalidOperationException("帧率地址不在可写数据页，已停止。");
            UIntPtr written;
            if (!WriteProcessMemory(h, new IntPtr(address), BitConverter.GetBytes(value), (UIntPtr)4, out written) || written.ToUInt64() != 4)
                throw Error("写入帧率失败");
            if (ReadInt(h, address) != value) throw new InvalidOperationException("帧率写入回读不一致，已停止。");
        }
    }

    // Read-only consumer of the locally installed RTSS SDK v2 shared-memory format.
    // dwTime0/dwTime1/dwFrames: offset 268/272/276; data is matched to the exact game PID.
    internal static class Rtss
    {
        internal static double? ReadFps(int pid)
        {
            IntPtr h = Native.OpenFileMapping(4, false, "RTSSSharedMemoryV2");
            if (h == IntPtr.Zero) return null;
            IntPtr p = IntPtr.Zero;
            try
            {
                p = Native.MapViewOfFile(h, 4, 0, 0, UIntPtr.Zero);
                if (p == IntPtr.Zero) return null;
                Native.MemoryInfo info;
                if (Native.VirtualQueryEx(new IntPtr(-1), p, out info, (UIntPtr)Marshal.SizeOf(typeof(Native.MemoryInfo))).ToUInt64() == 0) return null;
                long available = info.BaseAddress.ToInt64() + (long)info.RegionSize.ToUInt64() - p.ToInt64();
                if (available < 36 || Marshal.ReadInt32(p, 0) != 0x52545353 || (uint)Marshal.ReadInt32(p, 4) < 0x20000) return null;
                int size = Marshal.ReadInt32(p, 8), offset = Marshal.ReadInt32(p, 12), count = Marshal.ReadInt32(p, 16);
                if (size < 284 || size > 1048576 || count < 0 || count > 1024 || offset < 36 || (long)offset + (long)size * count > available) return null;
                for (int i = 0; i < count; i++)
                {
                    IntPtr e = new IntPtr(p.ToInt64() + offset + (long)i * size);
                    if (Marshal.ReadInt32(e) != pid) continue;
                    byte[] sample = new byte[284]; Marshal.Copy(e, sample, 0, sample.Length);
                    if (BitConverter.ToInt32(sample, 0) != pid) return null;
                    string name = Encoding.Default.GetString(sample, 4, 260).Split('\0')[0];
                    if (!String.Equals(Path.GetFileName(name), "NexusAnima.exe", StringComparison.OrdinalIgnoreCase)) return null;
                    uint t0 = BitConverter.ToUInt32(sample, 268), t1 = BitConverter.ToUInt32(sample, 272), frames = BitConverter.ToUInt32(sample, 276);
                    uint duration = unchecked(t1 - t0), age = unchecked(Native.GetTickCount() - t1);
                    if (t0 == 0 || duration == 0 || duration > 10000 || age > 4000) return null;
                    double fps = 1000.0 * frames / duration;
                    return fps >= 0 && fps < 5000 ? (double?)fps : null;
                }
                return null;
            }
            catch (Exception) { return null; }
            finally { if (p != IntPtr.Zero) Native.UnmapViewOfFile(p); Native.CloseHandle(h); }
        }
    }
}
