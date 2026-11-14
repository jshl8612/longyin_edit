# 分析工具（游戏更新后补丁不兼容时用）

1. 导出全部方法表（含 mscorlib 等所有程序集，按各自的代码表定位）：
   `python dump_methods.py GameAssembly.dll global-metadata.dat methods.tsv`
2. `pip install pefile capstone`（反汇编）；然后
   `python -i ndis.py GameAssembly.dll methods.tsv`
   - `va_of('HeroData.HaveForceFunction')` → 地址
   - `dis(0x...)` → 反汇编并标注调用的方法名
3. `python -i ncallers.py GameAssembly.dll methods.tsv` → `callers(0x...)` 查谁调用了某函数。
4. 对照 `docs\03_技术说明` 里的特征码，更新 `Patches.cs` 中的 `DllSig / CmpJne / Epilogue / CsSigOld`，重新编译。

`bookcnt\` 是「秘籍持有数量提示」注入代码的汇编源码（cave.asm）、机器码模板生成脚本（gen.py）和模拟测试（test_emu.py），用法见 docs\03。
`readskip\` 是「首次研读也可跳过」注入代码的汇编源码、模板生成脚本和模拟测试，用法见 docs\03。

数据表可以用 UnityPy 从 resources.assets 导出（TextAsset）。
