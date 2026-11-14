// LongYinMod - core binary helpers: Unity SerializedFile (v22), IL2CPP metadata (v27), PE image.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LongYinMod
{
    public class ModException : Exception
    {
        public ModException(string msg) : base(msg) { }
    }

    public static class Bin
    {
        public static int I32(byte[] b, long o) { return BitConverter.ToInt32(b, (int)o); }
        public static uint U32(byte[] b, long o) { return BitConverter.ToUInt32(b, (int)o); }
        public static long I64(byte[] b, long o) { return BitConverter.ToInt64(b, (int)o); }
        public static ushort U16(byte[] b, long o) { return BitConverter.ToUInt16(b, (int)o); }
        public static uint BeU32(byte[] b, int o) { return (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]); }
        public static long BeI64(byte[] b, int o)
        {
            long v = 0;
            for (int i = 0; i < 8; i++) v = (v << 8) | b[o + i];
            return v;
        }
        public static void PutBeI64(byte[] b, int o, long v)
        {
            for (int i = 7; i >= 0; i--) { b[o + i] = (byte)(v & 0xFF); v >>= 8; }
        }
        public static void PutI32(byte[] b, long o, int v) { Array.Copy(BitConverter.GetBytes(v), 0, b, o, 4); }
        public static void PutU32(byte[] b, long o, uint v) { Array.Copy(BitConverter.GetBytes(v), 0, b, o, 4); }
        public static void PutI64(byte[] b, long o, long v) { Array.Copy(BitConverter.GetBytes(v), 0, b, o, 8); }

        public static int IndexOf(byte[] hay, byte[] needle, int start = 0, int end = -1)
        {
            if (end < 0) end = hay.Length;
            int last = end - needle.Length;
            byte f = needle[0];
            for (int i = start; i <= last; i++)
            {
                if (hay[i] != f) continue;
                int k = 1;
                while (k < needle.Length && hay[i + k] == needle[k]) k++;
                if (k == needle.Length) return i;
            }
            return -1;
        }

        // pattern with -1 as wildcard
        public static int IndexOfPattern(byte[] hay, int[] pat, int start, int end)
        {
            int last = end - pat.Length;
            for (int i = start; i <= last; i++)
            {
                int k = 0;
                while (k < pat.Length && (pat[k] < 0 || hay[i + k] == pat[k])) k++;
                if (k == pat.Length) return i;
            }
            return -1;
        }

        public static string CString(byte[] b, int o, out int next)
        {
            int e = o;
            while (b[e] != 0) e++;
            next = e + 1;
            return Encoding.UTF8.GetString(b, o, e - o);
        }

        public static int Align4(int o) { return (o + 3) & ~3; }
        public static long Align8(long o) { return (o + 7) & ~7L; }

        public static string Md5(byte[] data)
        {
            using (var m = System.Security.Cryptography.MD5.Create())
            {
                var h = m.ComputeHash(data);
                var sb = new StringBuilder();
                foreach (var x in h) sb.Append(x.ToString("x2"));
                return sb.ToString();
            }
        }

        public static bool Equal(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // Unity aligned string: int32 length + bytes + pad to 4
        public static string ReadAlignedString(byte[] b, ref int o)
        {
            int len = I32(b, o);
            string s = Encoding.UTF8.GetString(b, o + 4, len);
            o = Align4(o + 4 + len);
            return s;
        }

        public static byte[] AlignedString(string s)
        {
            var raw = Encoding.UTF8.GetBytes(s);
            int total = Align4(4 + raw.Length);
            var outb = new byte[total];
            PutI32(outb, 0, raw.Length);
            Array.Copy(raw, 0, outb, 4, raw.Length);
            return outb;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Unity serialized file (format version 22, no type tree), as used by Unity 2020.3.
    // Supports replacing one object's raw bytes with a different length; later objects are moved
    // and re-aligned to 8 bytes, the object table and header file size are fixed up.
    // ---------------------------------------------------------------------------------------------
    public class SerializedFile
    {
        public class ObjInfo
        {
            public long PathId;
            public long Start;      // absolute
            public uint Size;
            public int TypeIndex;
            public int ClassId;
            public int EntryOffset; // offset of this entry in the file (pathId field)
        }

        public byte[] Data;
        public int Version;
        public long DataOffset;
        public int MetaSize;
        public string UnityVersion;
        public List<int> TypeClassIds = new List<int>();
        public List<ObjInfo> Objects = new List<ObjInfo>();
        public List<string> Externals = new List<string>();

        public SerializedFile(byte[] data)
        {
            Data = data;
            Parse();
        }

        void Parse()
        {
            if (Data.Length < 48) throw new ModException("文件太小，不是 Unity 资源文件");
            Version = (int)Bin.BeU32(Data, 8);
            if (Version != 22) throw new ModException("不支持的资源文件格式版本 " + Version + "（本工具只支持 22，游戏引擎可能已升级）");
            if (Data[16] != 0) throw new ModException("不支持大端序资源文件");
            MetaSize = (int)Bin.BeU32(Data, 20);
            long fileSize = Bin.BeI64(Data, 24);
            DataOffset = Bin.BeI64(Data, 32);
            if (fileSize != Data.Length) throw new ModException("文件长度与文件头记录不一致，文件可能已损坏");
            int o = 48;
            UnityVersion = Bin.CString(Data, o, out o);
            o += 4; // target platform
            bool typeTree = Data[o] != 0; o += 1;
            if (typeTree) throw new ModException("资源文件带类型树，格式与预期不同");
            int typeCount = Bin.I32(Data, o); o += 4;
            for (int i = 0; i < typeCount; i++)
            {
                int classId = Bin.I32(Data, o); o += 4;
                o += 1; // stripped
                short scriptIdx = BitConverter.ToInt16(Data, o); o += 2;
                if (classId == 114) o += 16; // script id
                o += 16; // old type hash
                TypeClassIds.Add(classId);
            }
            int objCount = Bin.I32(Data, o); o += 4;
            for (int i = 0; i < objCount; i++)
            {
                o = Bin.Align4(o);
                var oi = new ObjInfo();
                oi.EntryOffset = o;
                oi.PathId = Bin.I64(Data, o); o += 8;
                oi.Start = Bin.I64(Data, o) + DataOffset; o += 8;
                oi.Size = Bin.U32(Data, o); o += 4;
                oi.TypeIndex = Bin.I32(Data, o); o += 4;
                oi.ClassId = (oi.TypeIndex >= 0 && oi.TypeIndex < TypeClassIds.Count) ? TypeClassIds[oi.TypeIndex] : -1;
                Objects.Add(oi);
            }
            int scriptCount = Bin.I32(Data, o); o += 4;
            for (int i = 0; i < scriptCount; i++)
            {
                o += 4;
                o = Bin.Align4(o);
                o += 8;
            }
            int extCount = Bin.I32(Data, o); o += 4;
            for (int i = 0; i < extCount; i++)
            {
                Bin.CString(Data, o, out o);
                o += 16 + 4;
                string path = Bin.CString(Data, o, out o);
                Externals.Add(path);
            }
            if (o > 48 + MetaSize) throw new ModException("资源文件元数据解析越界");
            foreach (var x in Objects)
                if (x.Start < DataOffset || x.Start + x.Size > Data.Length)
                    throw new ModException("资源文件对象表异常");
        }

        public ObjInfo Find(long pathId)
        {
            foreach (var x in Objects) if (x.PathId == pathId) return x;
            return null;
        }

        public byte[] Raw(ObjInfo oi)
        {
            var r = new byte[oi.Size];
            Array.Copy(Data, oi.Start, r, 0, oi.Size);
            return r;
        }

        public string ObjName(ObjInfo oi)
        {
            int o = (int)oi.Start;
            int len = Bin.I32(Data, o);
            if (len < 0 || len > 4096 || o + 4 + len > Data.Length) return null;
            return Encoding.UTF8.GetString(Data, o + 4, len);
        }

        public List<ObjInfo> OfClass(int classId)
        {
            var l = new List<ObjInfo>();
            foreach (var x in Objects) if (x.ClassId == classId) l.Add(x);
            return l;
        }

        // Returns a new file image with the object's raw data replaced.
        public byte[] WithObjectReplaced(long pathId, byte[] newRaw)
        {
            var target = Find(pathId);
            if (target == null) throw new ModException("找不到对象 " + pathId);
            // deterministic, stable order: by start, zero-size objects first, then table order
            // (several objects can share a start offset; an unstable sort would move them around)
            var sorted = Objects.Select((o, i) => new { o, i })
                                .OrderBy(x => x.o.Start).ThenBy(x => x.o.Size).ThenBy(x => x.i)
                                .Select(x => x.o).ToList();

            var ms = new MemoryStream(Data.Length + newRaw.Length + 64);
            ms.Write(Data, 0, (int)target.Start);
            ms.Write(newRaw, 0, newRaw.Length);
            var newStart = new Dictionary<long, long>();
            var newSize = new Dictionary<long, uint>();
            newStart[target.PathId] = target.Start;
            newSize[target.PathId] = (uint)newRaw.Length;
            int ti = sorted.IndexOf(target);
            foreach (var x in sorted.Skip(ti + 1))   // everything laid out after the target moves
            {
                long pos = ms.Length;
                long aligned = Bin.Align8(pos);
                for (long p = pos; p < aligned; p++) ms.WriteByte(0);
                newStart[x.PathId] = aligned;
                newSize[x.PathId] = x.Size;
                ms.Write(Data, (int)x.Start, (int)x.Size);
            }
            var outb = ms.ToArray();
            foreach (var x in Objects)
            {
                if (!newStart.ContainsKey(x.PathId)) continue;
                Bin.PutI64(outb, x.EntryOffset + 8, newStart[x.PathId] - DataOffset);
                Bin.PutU32(outb, x.EntryOffset + 16, newSize[x.PathId]);
            }
            Bin.PutBeI64(outb, 24, outb.Length);
            return outb;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // PE image (x64) - sections and address translation
    // ---------------------------------------------------------------------------------------------
    public class PeImage
    {
        public byte[] Data;
        public ulong ImageBase;
        public struct Sec { public string Name; public uint VA, VSize, RawPtr, RawSize; }
        public List<Sec> Sections = new List<Sec>();

        public PeImage(byte[] data)
        {
            Data = data;
            if (data.Length < 0x400 || data[0] != 'M' || data[1] != 'Z') throw new ModException("GameAssembly.dll 不是有效的 PE 文件");
            int pe = Bin.I32(data, 0x3C);
            if (Bin.U32(data, pe) != 0x4550) throw new ModException("PE 签名错误");
            int nsec = Bin.U16(data, pe + 6);
            int optSize = Bin.U16(data, pe + 20);
            int opt = pe + 24;
            if (Bin.U16(data, opt) != 0x20B) throw new ModException("不是 64 位程序");
            ImageBase = (ulong)Bin.I64(data, opt + 24);
            int s = opt + optSize;
            for (int i = 0; i < nsec; i++, s += 40)
            {
                var sec = new Sec();
                sec.Name = Encoding.ASCII.GetString(data, s, 8).TrimEnd('\0');
                sec.VSize = Bin.U32(data, s + 8);
                sec.VA = Bin.U32(data, s + 12);
                sec.RawSize = Bin.U32(data, s + 16);
                sec.RawPtr = Bin.U32(data, s + 20);
                Sections.Add(sec);
            }
        }

        // 导出函数地址（例如 il2cpp_string_new）
        public ulong Export(string name)
        {
            int pe = Bin.I32(Data, 0x3C);
            int opt = pe + 24;
            uint rva = Bin.U32(Data, opt + 112), size = Bin.U32(Data, opt + 116);
            long dir = VaToOff(ImageBase + rva);
            if (rva == 0 || dir < 0) throw new ModException("GameAssembly.dll 没有导出表");
            uint nNames = Bin.U32(Data, dir + 24);
            long funcs = VaToOff(ImageBase + Bin.U32(Data, dir + 28));
            long names = VaToOff(ImageBase + Bin.U32(Data, dir + 32));
            long ords = VaToOff(ImageBase + Bin.U32(Data, dir + 36));
            var want = Encoding.ASCII.GetBytes(name + "\0");
            for (uint i = 0; i < nNames; i++)
            {
                long np = VaToOff(ImageBase + Bin.U32(Data, names + i * 4));
                bool eq = true;
                for (int k = 0; k < want.Length && eq; k++) if (Data[np + k] != want[k]) eq = false;
                if (!eq) continue;
                int ord = Bin.U16(Data, ords + i * 2);
                return ImageBase + Bin.U32(Data, funcs + ord * 4);
            }
            throw new ModException("GameAssembly.dll 里找不到导出函数 " + name);
        }

        // 在文件末尾新增一个可执行节，返回新文件；caveVa = 新节的虚拟地址
        public static byte[] AddSection(byte[] data, string name, int size, out ulong caveVa)
        {
            return AddSection(data, name, size, 0x60000020u, out caveVa);
        }

        // characteristics：0x60000020 = 代码 | 可执行 | 可读；0xE0000020 再加可写（代码里带变量时用）
        public static byte[] AddSection(byte[] data, string name, int size, uint characteristics, out ulong caveVa)
        {
            var img = new PeImage(data);
            int pe = Bin.I32(data, 0x3C);
            int nsec = Bin.U16(data, pe + 6);
            int optSize = Bin.U16(data, pe + 20);
            int opt = pe + 24;
            uint secAlign = Bin.U32(data, opt + 32), fileAlign = Bin.U32(data, opt + 36), hdrSize = Bin.U32(data, opt + 60);
            int hdr = opt + optSize + nsec * 40;
            uint firstRaw = uint.MaxValue;
            foreach (var s in img.Sections) if (s.RawSize > 0) firstRaw = Math.Min(firstRaw, s.RawPtr);
            if (hdr + 40 > Math.Min(hdrSize, firstRaw)) throw new ModException("PE 头没有空间再加一个节");
            for (int i = hdr; i < hdr + 40; i++) if (data[i] != 0) throw new ModException("PE 头节表后面不是空的");
            var last = img.Sections[img.Sections.Count - 1];
            Func<long, uint, long> align = (v, a) => (v + a - 1) / a * a;
            uint va = (uint)align(Math.Max(last.VA + Math.Max(last.VSize, 1u), last.VA + last.RawSize), secAlign);
            foreach (var s in img.Sections) va = Math.Max(va, (uint)align(s.VA + Math.Max(s.VSize, s.RawSize), secAlign));
            uint rawPtr = (uint)align(data.Length, fileAlign);
            uint rawSize = (uint)align(size, fileAlign);
            var o = new byte[rawPtr + rawSize];
            Array.Copy(data, o, data.Length);
            var nb = Encoding.ASCII.GetBytes(name);
            Array.Copy(nb, 0, o, hdr, Math.Min(8, nb.Length));
            Array.Copy(BitConverter.GetBytes((uint)size), 0, o, hdr + 8, 4);
            Array.Copy(BitConverter.GetBytes(va), 0, o, hdr + 12, 4);
            Array.Copy(BitConverter.GetBytes(rawSize), 0, o, hdr + 16, 4);
            Array.Copy(BitConverter.GetBytes(rawPtr), 0, o, hdr + 20, 4);
            Array.Copy(BitConverter.GetBytes(characteristics), 0, o, hdr + 36, 4);
            Array.Copy(BitConverter.GetBytes((ushort)(nsec + 1)), 0, o, pe + 6, 2);
            Array.Copy(BitConverter.GetBytes((uint)align(va + size, secAlign)), 0, o, opt + 56, 4);   // SizeOfImage
            caveVa = img.ImageBase + va;
            return o;
        }

        public long VaToOff(ulong va)
        {
            if (va < ImageBase) return -1;
            ulong rva = va - ImageBase;
            foreach (var s in Sections)
            {
                uint size = Math.Max(s.VSize, s.RawSize);
                if (rva >= s.VA && rva < s.VA + size)
                {
                    ulong off = s.RawPtr + (rva - s.VA);
                    return off < (ulong)Data.Length ? (long)off : -1;
                }
            }
            return -1;
        }

        public ulong OffToVa(long off)
        {
            foreach (var s in Sections)
                if (off >= s.RawPtr && off < s.RawPtr + s.RawSize)
                    return ImageBase + s.VA + (ulong)(off - s.RawPtr);
            return 0;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // IL2CPP global-metadata.dat (version 27) - just enough to find methods by name
    // ---------------------------------------------------------------------------------------------
    public class Il2CppMeta
    {
        public byte[] D;
        int strOff, strSize, methOff, methSize, parOff, typeOff, typeSize, imgOff, imgSize;
        const int MSZ = 32, TSZ = 88, PSZ = 12, ISZ = 40;

        public Il2CppMeta(byte[] d)
        {
            D = d;
            if (Bin.U32(d, 0) != 0xFAB11BAF) throw new ModException("global-metadata.dat 签名错误（可能被加密或已更换格式）");
            int ver = Bin.I32(d, 4);
            if (ver != 27) throw new ModException("不支持的 IL2CPP 元数据版本 " + ver + "（本工具只支持 27）");
            strOff = Bin.I32(d, 24); strSize = Bin.I32(d, 28);
            methOff = Bin.I32(d, 48); methSize = Bin.I32(d, 52);
            parOff = Bin.I32(d, 88);
            typeOff = Bin.I32(d, 160); typeSize = Bin.I32(d, 164);
            imgOff = Bin.I32(d, 168); imgSize = Bin.I32(d, 172);
        }

        public string Str(int idx)
        {
            int o = strOff + idx;
            int e = o;
            while (D[e] != 0) e++;
            return Encoding.UTF8.GetString(D, o, e - o);
        }

        // 带命名空间查找（如 UnityEngine.Object 与 System.Object 同名）
        public int FindType(string ns, string name)
        {
            int n = typeSize / TSZ;
            for (int i = 0; i < n; i++)
                if (Str(Bin.I32(D, typeOff + i * TSZ)) == name && Str(Bin.I32(D, typeOff + i * TSZ + 4)) == ns) return i;
            return -1;
        }

        // 字符串常量（metadata usage 解码后的下标）
        public string Literal(int index)
        {
            int lo = Bin.I32(D, 8), lsz = Bin.I32(D, 12), dataOff = Bin.I32(D, 16);
            if (index < 0 || index >= lsz / 8) return null;
            int len = Bin.I32(D, lo + index * 8), di = Bin.I32(D, lo + index * 8 + 4);
            return Encoding.UTF8.GetString(D, dataOff + di, len);
        }

        public int FindType(string name)
        {
            int n = typeSize / TSZ;
            for (int i = 0; i < n; i++)
                if (Str(Bin.I32(D, typeOff + i * TSZ)) == name) return i;
            return -1;
        }

        public class MethodRef { public string Name; public uint Token; public List<string> Params = new List<string>(); }

        public List<MethodRef> Methods(int typeIndex)
        {
            var l = new List<MethodRef>();
            int b = typeOff + typeIndex * TSZ;
            int ms = Bin.I32(D, b + 36);
            int mc = Bin.U16(D, b + 64);
            for (int i = ms; i < ms + mc; i++)
            {
                int mb = methOff + i * MSZ;
                var m = new MethodRef();
                m.Name = Str(Bin.I32(D, mb));
                m.Token = Bin.U32(D, mb + 20);
                int ps = Bin.I32(D, mb + 12);
                int pc = Bin.U16(D, mb + 30);
                for (int k = 0; k < pc; k++) m.Params.Add(Str(Bin.I32(D, parOff + (ps + k) * PSZ)));
                l.Add(m);
            }
            return l;
        }

        public MethodRef FindMethod(string type, string method, string[] paramNames)
        {
            return FindMethodIn(FindType(type), type, method, paramNames);
        }

        public MethodRef FindMethod(string ns, string type, string method, string[] paramNames)
        {
            return FindMethodIn(FindType(ns, type), ns + "." + type, method, paramNames);
        }

        MethodRef FindMethodIn(int t, string type, string method, string[] paramNames)
        {
            if (t < 0) throw new ModException("代码中找不到类 " + type);
            foreach (var m in Methods(t))
            {
                if (m.Name != method) continue;
                if (paramNames == null) return m;
                if (m.Params.Count != paramNames.Length) continue;
                bool ok = true;
                for (int i = 0; i < paramNames.Length; i++) if (m.Params[i] != paramNames[i]) ok = false;
                if (ok) return m;
            }
            throw new ModException("代码中找不到方法 " + type + "." + method + "(" + (paramNames == null ? "" : string.Join(",", paramNames)) + ")");
        }
    }

    // Locates method entry points of Assembly-CSharp inside GameAssembly.dll
    public class Il2CppCode
    {
        public PeImage Pe;
        long methodPointersOff;
        uint methodCount;

        public Il2CppCode(PeImage pe, string moduleName)
        {
            Pe = pe;
            var name = Encoding.ASCII.GetBytes(moduleName + "\0");
            int pos = 0;
            while (true)
            {
                int p = Bin.IndexOf(pe.Data, name, pos);
                if (p < 0) break;
                pos = p + 1;
                ulong va = pe.OffToVa(p);
                if (va == 0) continue;
                var needle = BitConverter.GetBytes(va);
                int q = 0;
                while (true)
                {
                    int r = Bin.IndexOf(pe.Data, needle, q);
                    if (r < 0) break;
                    q = r + 1;
                    if (r % 8 != 0) continue;
                    uint cnt = Bin.U32(pe.Data, r + 8);
                    ulong mp = (ulong)Bin.I64(pe.Data, r + 16);
                    long mpo = pe.VaToOff(mp);
                    if (cnt > 100 && cnt < 200000 && mpo > 0 && mpo + cnt * 8 <= pe.Data.Length)
                    {
                        methodCount = cnt; methodPointersOff = mpo;
                        return;
                    }
                }
            }
            throw new ModException("在 GameAssembly.dll 中找不到 " + moduleName + " 的代码表");
        }

        public ulong MethodVa(uint token)
        {
            uint rid = token & 0xFFFFFF;
            if (rid == 0 || rid > methodCount) throw new ModException("方法编号越界");
            ulong va = (ulong)Bin.I64(Pe.Data, methodPointersOff + (rid - 1) * 8);
            if (va == 0) throw new ModException("方法没有编译代码");
            return va;
        }
    }
}
