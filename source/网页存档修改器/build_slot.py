# 追加在 build.py 之后执行：在 save_editor.html 的基础上加入文件夹 / 槽位模式
import sys
OUT = 'save_editor_template.html' if '--template' in sys.argv else 'save_editor.html'
src=open(OUT,encoding='utf-8').read()
def rep(old,new,count=1):
    global src
    n=src.count(old); assert n==count,(old[:70],n); src=src.replace(old,new)
# 1) 抽出人物载入代码 -> applyHeroData
s0=src.index('    state.rawData = data;\n    state.heroIdMap = null;')
end_mark="    showToast(`已载入 ${validCount} 位人物`);\n"
e0=src.index(end_mark)+len(end_mark)
block=src[s0:e0]
src=src[:s0]+'    applyHeroData(file, data);\n'+src[e0:]
heroFn='\n// 人物数据载入（单文件与文件夹模式共用）\nfunction applyHeroData(file, data) {\n'+block+'}\n'
rep('  state.fileName = file.name;\n  state.fileSize = file.size;','  state.fileName = file.name;\n  state.fileSize = file.size;\n  state.slot = null;')
# 2) 顶栏
rep('    <span class="file-info" id="fileInfo"></span>',
    '    <div class="mode-switch hidden" id="modeSwitch"><button data-mode="hero">人物</button><button data-mode="area">区域</button></div>\n'
    '    <span class="file-info" id="fileInfo"></span>\n'
    '    <button class="btn btn-ghost hidden" id="btnSlots" title="回到存档列表">换存档</button>')
# 3) 拖放区 + 槽位列表
rep('<div class="drop-title">拖入 Hero 或 Save 存档</div>','<div class="drop-title">拖入 Save 存档文件夹</div>')
rep('<div class="drop-hint">Hero = 人物修改 · Save = 城市 / 驻地地块扩建<br>本地解析 · 文件不会上传 · 支持 ~30MB JSON</div>',
    '<div class="drop-hint">可以拖整个 Save 文件夹、某个 SaveSlot 文件夹，或单个 Hero / Save 文件<br>人物改 Hero · 城市驻地扩建改 Save · 改哪个写哪个，写前自动备份<br>本地解析 · 文件不会上传 · 需要 Edge 或 Chrome</div>')
rep('      <button class="btn" id="btnPick">选择文件</button>',
    '      <button class="btn btn-primary" id="btnPickDir">选择存档文件夹</button>\n      <button class="btn" id="btnPick">选择单个文件</button>')
rep('<div class="workspace hidden" id="workspace">', open('slot_picker.html',encoding='utf-8').read()+'<div class="workspace hidden" id="workspace">')
# 4) 拖入文件夹
rep("      if (handle && handle.kind === 'file') {\n        state.fileHandle = handle;",
    "      if (handle && handle.kind === 'directory') { openDirectory(handle); return; }\n      if (handle && handle.kind === 'file') {\n        state.fileHandle = handle;")
# 5) 保存 / 重新载入 / 保存按钮
rep("  if (!state.rawData && !area.data) return;\n  setLoading(true, '生成文件...');","  if (state.slot) return saveSlot();\n  if (!state.rawData && !area.data) return;\n  setLoading(true, '生成文件...');")
rep("document.getElementById('btnReload').addEventListener('click', async () => {\n","document.getElementById('btnReload').addEventListener('click', async () => {\n  if (state.slot) { await reloadSlot(); return; }\n")
rep("  if (!btn) return;\n  if (state.fileHandle) {",
    "  if (!btn) return;\n  if (state.slot) {\n    const f = slotDirtyFiles();\n    btn.textContent = '保存到存档';\n    btn.title = f.length ? '将写入 ' + state.slot.name + ' 的 ' + f.join('、') + '（写前自动备份）' : '没有改动';\n"
    "    document.querySelectorAll('#modeSwitch button').forEach(b => b.classList.toggle('dirty', b.dataset.mode === 'hero' ? state.dirtyIdx.size > 0 : areaDirty() > 0));\n    return;\n  }\n  if (state.fileHandle) {")
# 6) 样式与脚本
i=src.index('</style>'); src=src[:i]+open('slot.css',encoding='utf-8').read()+src[i:]
i=src.rindex('</script>'); src=src[:i]+heroFn+open('slot_mode.js',encoding='utf-8').read()+'\n'+open('host_shim.js',encoding='utf-8').read()+'\n'+src[i:]
open(OUT,'w',encoding='utf-8').write(src)
print('built with folder mode', len(src.encode('utf-8')), 'bytes')
