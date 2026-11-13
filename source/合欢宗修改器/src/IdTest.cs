using System; using System.IO; using System.Linq;
namespace LongYinMod {
  public static class IdTest {
    public static int Run(string[] files) {
      foreach (var f in files) {
        var d = File.ReadAllBytes(f); var sf = new SerializedFile(d);
        var dup = sf.Objects.GroupBy(o => o.Start).Count(g => g.Count() > 1);
        int ok = 0, bad = 0;
        // replace the first object, a middle one, and every object that shares a start with another
        var picks = sf.Objects.Where((o,i) => i == 0 || i == sf.Objects.Count/2 || sf.Objects.Count(p => p.Start == o.Start) > 1).ToList();
        foreach (var o in picks) { var r = sf.WithObjectReplaced(o.PathId, sf.Raw(o)); if (Bin.Equal(r, d)) ok++; else bad++; }
        Console.WriteLine(Path.GetFileName(f) + ": 对象 " + sf.Objects.Count + "，共享起点的组 " + dup + "，原样替换测试 " + ok + " 通过 / " + bad + " 失败");
      }
      return 0;
    }
  }
}
