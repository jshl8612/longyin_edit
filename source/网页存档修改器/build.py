import os, sys
# 游戏数据（game_dict.json 武学/天赋/门派名等，bnames.json 建筑名）从游戏里整理而来，只保存在本地，不放进公开仓库
for need in ('game_dict.json', 'bnames.json'):
    if not os.path.exists(need): sys.exit('缺少 ' + need + '（游戏数据，只在本地项目文件夹里，不在 Git 仓库中）')
src=open('hero_editor.html',encoding='utf-8').read()
assert src.count('__GAME_DICT__') == 1
src=src.replace('__GAME_DICT__', open('game_dict.json',encoding='utf-8').read().strip())
def rep(old,new,count=1):
    global src
    n=src.count(old); assert n==count,(old[:60],n); src=src.replace(old,new)
rep('<title>龙胤立志传 · 人物修改器</title>','<title>龙胤立志传 · 存档修改器</title>')
rep('<div class="brand-en">Hero Editor</div>','<div class="brand-en">Save Editor</div>')
rep('<div class="drop-title">拖入 Hero 存档</div>','<div class="drop-title">拖入 Hero 或 Save 存档</div>')
rep('<div class="drop-hint">本地解析 · 文件不会上传 · 支持 ~30MB JSON</div>','<div class="drop-hint">Hero = 人物修改 · Save = 城市 / 驻地地块扩建<br>本地解析 · 文件不会上传 · 支持 ~30MB JSON</div>')
rep("types: [{ description: '龙胤立志传 Hero 存档', accept: { '*/*': [''] } }],","types: [{ description: '龙胤立志传 存档（Hero / Save）', accept: { '*/*': [''] } }],")
markup=open('markup.html',encoding='utf-8').read()
rep('<div class="loading hidden" id="loading">', markup+'<div class="loading hidden" id="loading">')
css=open('area.css',encoding='utf-8').read()
i=src.index('</style>'); src=src[:i]+css+src[i:]
rep('''    if (!Array.isArray(data)) {
      let arr = null;''','''    if (data && !Array.isArray(data) && Array.isArray(data.Areas) && Array.isArray(data.Forces)) {
      await loadAreaMode(file, text, data);
      return;
    }
    if (state.mode === 'area') leaveAreaMode();
    if (!Array.isArray(data)) {
      let arr = null;''')
rep("  if (!state.rawData) return;\n  setLoading(true, '生成文件...');","  if (!state.rawData && !area.data) return;\n  setLoading(true, '生成文件...');")
rep('''  const onSaved = (msg) => {
    state.dirtyIdx.clear();''','''  let areaOut = null;
  const onSaved = (msg) => {
    if (areaOut) afterAreaSaved(areaOut.text);
    state.dirtyIdx.clear();''')
rep('    const text = JSON.stringify(state.rawData);',"    if (state.mode === 'area') areaOut = buildAreaText();\n    const text = areaOut ? areaOut.text : JSON.stringify(state.rawData);")
rep("if (!confirm(`将直接覆盖源文件:\\n${state.fileHandle.name}\\n\\n请确认已备份。继续？`)) {","if (!confirm(`将直接覆盖源文件:\\n${state.fileHandle.name}\\n${areaOut ? `\\n改动 ${areaOut.count} 个地块。请确认游戏已完全关闭（建议先关闭 Steam 云存档）。\\n` : ''}\\n请确认已备份。继续？`)) {")
rep("    btn.textContent = '另存为 Hero';","    btn.textContent = state.mode === 'area' ? '另存为 Save' : '另存为 Hero';")
rep("    if (state.rawData) return;\n    dropzone.classList.add('dragging');","    if (state.rawData || area.data) return;\n    dropzone.classList.add('dragging');")
rep("  e.preventDefault();\n  if (state.rawData) return;\n  const item","  e.preventDefault();\n  if (state.rawData || area.data) return;\n  const item")
rep("if (state.dirtyIdx.size > 0 && !confirm('将丢弃所有未保存的修改，从磁盘重新读取原文件？')) return;","if ((state.dirtyIdx.size > 0 || (area.data && areaDirty() > 0)) && !confirm('将丢弃所有未保存的修改，从磁盘重新读取原文件？')) return;")
rep("  if (state.dirtyIdx.size > 0 && !confirm('有未保存的修改，确定返回？')) return;\n  state.rawData = null;","  if ((state.dirtyIdx.size > 0 || (area.data && areaDirty() > 0)) && !confirm('有未保存的修改，确定返回？')) return;\n  if (state.mode === 'area') leaveAreaMode();\n  state.rawData = null;")
js=open('gz_io.js',encoding='utf-8').read()+'\n'+open('area_mode.js',encoding='utf-8').read().replace('__BNAMES__',open('bnames.json',encoding='utf-8').read())
i=src.rindex('</script>'); src=src[:i]+js+'\n'+src[i:]

# 压缩存档（游戏 1.1.3 起 Hero/Save/Info 为 gzip）：读入自动解压，写回保持原格式
rep("    const text = await file.text();\n    setLoading(true, 'parsing JSON...');","    const rd = await readSaveText(file);\n    const text = rd.text;\n    state.srcFmt = rd.fmt;\n    setLoading(true, 'parsing JSON...');")
rep("        await writable.write(text);", "        await writable.write(await encodeSaveText(text, state.srcFmt));", 2)
rep("    const blob = new Blob([text], { type: 'application/octet-stream' });", "    const blob = new Blob([await encodeSaveText(text, state.srcFmt)], { type: 'application/octet-stream' });")

# 游戏 1.1.3 起这些派生数值不再写入存档（读档时由游戏重新计算）：没有就不显示，避免误改、避免「满血满内」把气血设成 0
rep("const COMBAT_FIELDS = [", "const DERIVED_KEYS = new Set(['maxhp','realMaxHp','maxPower','realMaxPower','maxMana','realMaxMana','armor','fightScore']);\nconst COMBAT_FIELDS = [")
rep("  for (const f of BASIC_FIELDS) body += renderInfoRow(hero, f);", "  for (const f of BASIC_FIELDS) { if (DERIVED_KEYS.has(f.key) && hero[f.key] === undefined) continue; body += renderInfoRow(hero, f); }")
rep("""  let body = '<div class="info-grid">';
  for (const f of COMBAT_FIELDS) {""", """  let body = '<div class="info-grid">';
  let derivedHidden = false;
  for (const f of COMBAT_FIELDS) {
    if (DERIVED_KEYS.has(f.key) && hero[f.key] === undefined) { derivedHidden = true; continue; }""")
rep("""  body += '</div>';
  return `
    <div class="section">
      <div class="section-head">
        <div class="section-mark"></div>
        <div class="section-title">气血 / 体力 / 内力 / 伤势</div>
        <div class="section-actions">
          <button class="btn btn-mini" id="btnFullHeal">满血满内</button>""", """  body += '</div>';
  if (derivedHidden) body += '<div class="info-note" style="margin-top:10px;font-size:12px;opacity:.65">气血 / 体力 / 内力上限和护甲由游戏读档时自动计算（新版存档不再保存），这里不显示。</div>';
  return `
    <div class="section">
      <div class="section-head">
        <div class="section-mark"></div>
        <div class="section-title">气血 / 体力 / 内力 / 伤势</div>
        <div class="section-actions">
          ${hero.realMaxHp === undefined ? '' : '<button class="btn btn-mini" id="btnFullHeal">满血满内</button>'}""")
open('save_editor.html','w',encoding='utf-8').write(src)
print('built', len(src.encode('utf-8')), 'bytes')
