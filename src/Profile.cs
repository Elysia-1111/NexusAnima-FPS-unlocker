using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace NexusAnima240
{
    internal static class Profile
    {
        internal const int MaxFps = 240;
        internal const string Hash = "91c53ce2ce0a0a6a519c453e6ab35829c64a6e80078468c6b6d26803e8bc8c2e";
        internal const string ExeHash = "71e76417daf4540cccc2eb5191fe223d254f333c83d550fa8e8cfbd55a477fcf";
        internal const string DefaultGamePath = @"C:\Program Files\Nexus Anima\games\NexusAnima Game\NexusAnima.exe";
        internal const int FpsRva = 0x2a1c15c;
        internal static readonly int[] CodeRvas = { 0x20bb10, 0x20bb20, 0x1a80d93, 0x1a81114 };
        internal static readonly byte[][] Code = {
            Hex("8b0546068102c3"),
            Hex("890d36068102c3"),
            Hex("8b05c3b3f900f30f2ac80f57d20f28c1f30fc2c2060f54c8f30f10156dc2c400"),
            Hex("e8a7fbfffff30f5ac0f2440f10053256c400f2440f5ec0")
        };
        internal static byte[] Hex(string text)
        {
            byte[] b = new byte[text.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(text.Substring(i * 2, 2), 16);
            return b;
        }
        internal static string Sha256(string path)
        {
            using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        internal static void CheckFile(string path)
        {
            if (!String.Equals(Path.GetFileName(path), "UnityPlayer.dll", StringComparison.OrdinalIgnoreCase) || Sha256(path) != Hash)
                throw new InvalidOperationException("游戏引擎版本不匹配。当前工具仅适配本机 0.75.0 版本；已拒绝修改，请重新适配。");
        }
        internal static void CheckBytes(int index, byte[] data)
        {
            if (!Code[index].SequenceEqual(data)) throw new InvalidOperationException("内存中的引擎特征不匹配，已拒绝修改。");
        }
        internal static void CheckMemory(IntPtr handle, long moduleBase)
        {
            for (int i = 0; i < CodeRvas.Length; i++) CheckBytes(i, Native.Read(handle, moduleBase + CodeRvas[i], Code[i].Length));
            // Getter and setter must both refer to exactly the same validated data RVA.
            if (CodeRvas[0] + 6 + BitConverter.ToInt32(Code[0], 2) != FpsRva ||
                CodeRvas[1] + 6 + BitConverter.ToInt32(Code[1], 2) != FpsRva)
                throw new InvalidOperationException("内部适配数据不一致。");
        }
        internal static bool PlausibleOriginal(int value)
        {
            return value == -1 || (value >= 0 && value <= MaxFps);
        }
    }
}
