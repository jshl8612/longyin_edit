# 分析工具（游戏更新后补丁不兼容时用）

1. 编译 `dump_methods.cs`（引用 合欢宗修改器.exe），导出方法表：
   `dump_methods.exe GameAssembly.dll global-metadata.dat methods.tsv`
2. `pip install pefile capstone`，然后
   `python -i ndis.py GameAssembly.dll methods.tsv`
   - `va_of('HeroData.HaveForceFunction')` → 地址
   - `dis(0x...)` → 反汇编并标注调用的方法名
3. `python -i ncallers.py GameAssembly.dll methods.tsv` → `callers(0x...)` 查谁调用了某函数。
4. 对照 `docs\03_技术说明` 里的特征码，更新 `Patches.cs` 中的 `DllSig / CmpJne / Epilogue / CsSigOld`，重新编译。

数据表可以用 UnityPy 从 resources.assets 导出（TextAsset）。
