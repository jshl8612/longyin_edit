// 导出 GameAssembly.dll 里所有方法的地址和名字（需要引用 合欢宗修改器.exe 里的 Il2CppMeta / Il2CppCode）
// 编译：csc /r:合欢宗修改器.exe dump_methods.cs    运行：dump_methods.exe GameAssembly.dll global-metadata.dat methods.tsv
using System; using System.IO; using System.Reflection; using LongYinMod;
class DumpMethods { static void Main(string[] a) {
  var dll = File.ReadAllBytes(a[0]); var meta = File.ReadAllBytes(a[1]);
  var m = new Il2CppMeta(meta); var code = new Il2CppCode(new PeImage(dll), "Assembly-CSharp.dll");
  int typeOff = (int)typeof(Il2CppMeta).GetField("typeOff", BindingFlags.NonPublic|BindingFlags.Instance).GetValue(m);
  int typeSize = (int)typeof(Il2CppMeta).GetField("typeSize", BindingFlags.NonPublic|BindingFlags.Instance).GetValue(m);
  var w = new StreamWriter(a[2]);
  for (int i = 0; i < typeSize / 88; i++) {
    string tn = m.Str(BitConverter.ToInt32(meta, typeOff + i*88));
    foreach (var mr in m.Methods(i)) {
      ulong va = 0; try { va = code.MethodVa(mr.Token); } catch {}
      w.WriteLine(va.ToString("x") + "\t" + tn + "." + mr.Name + "(" + string.Join(",", mr.Params) + ")");
    }
  }
  w.Close();
}}
