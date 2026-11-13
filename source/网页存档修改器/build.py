import os, sys
# 两种产物：
#   python build.py            → save_editor.html：网页版，游戏数据（game_dict.json、bnames.json，仅本地）直接内嵌
#   python build.py --template → save_editor_template.html：给 exe 用，保留 __GAME_DICT__ / __BNAMES__ 占位，exe 运行时从游戏文件读取后填入
TEMPLATE = '--template' in sys.argv
OUT = 'save_editor_template.html' if TEMPLATE else 'save_editor.html'
if not TEMPLATE:
    for need in ('game_dict.json', 'bnames.json'):
        if not os.path.exists(need): sys.exit('缺少 ' + need + '（游戏数据，只在本地项目文件夹里，不在 Git 仓库中）')
src=open('hero_editor.html',encoding='utf-8').read()
assert src.count('__GAME_DICT__') == 1
if not TEMPLATE:
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
js=open('gz_io.js',encoding='utf-8').read()+'\n'+(open('area_mode.js',encoding='utf-8').read() if TEMPLATE else open('area_mode.js',encoding='utf-8').read().replace('__BNAMES__',open('bnames.json',encoding='utf-8').read()))
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

# 游戏 1.1.3 起武功的「基础属性」「招式效果」（speEquipData / speUseData）不再存档，读档时按武学表和等级重新计算：
# 新存档里改成只读显示（按游戏规则算出来），突破属性 extraAddData 仍可编辑
rep("""// 武功属性编辑
function openAttrModalForKungfu(hero, sk, kfIdx) {""", """// 按武学表计算武功效果（与游戏一致：数值 ×(1+0.1×等级)；击退、拉近不随等级变化；运功效果里的内力上限再 ×2）
const ATTR_ID_BY_NAME = (() => { const m = {}; for (const [id, e] of Object.entries(ATTR_DICT)) if (!(e.n in m)) m[e.n] = id; return m; })();
function calcKungfuEffect(skillID, lv, which) {
  const e = KF_DICT[String(skillID)] || {};
  const src = which === 'eq' ? e.eq : e.us;
  const out = {};
  if (!src) return out;
  for (const part of src.split(';')) {
    const m = part.match(/^(.+?)([+-]?[\\d.]+)$/);
    if (!m) continue;
    const id = ATTR_ID_BY_NAME[m[1]];
    if (id === undefined) continue;
    let v = parseFloat(m[2]);
    if (m[1] !== '击退' && m[1] !== '拉近') v *= 1 + 0.1 * (lv || 0);
    if (which === 'eq' && m[1] === '内力上限') v *= 2;
    out[id] = Math.round(v * 10000) / 10000;
  }
  return out;
}
// 武功属性编辑
function openAttrModalForKungfu(hero, sk, kfIdx) {
  const derived = sk.speEquipData === undefined && sk.speUseData === undefined;   // 新版存档：这两项由游戏计算""")
rep("""      {
        key: 'base',
        label: '基础属性 (随等级提升)',
        en: 'speEquipData',
        ensure: () => {
          if (!sk.speEquipData) sk.speEquipData = { heroSpeAddData: {} };
          if (!sk.speEquipData.heroSpeAddData) sk.speEquipData.heroSpeAddData = {};
        },
        getDict: () => sk.speEquipData.heroSpeAddData,
      },""", """      derived ? {
        key: 'base', readOnly: true,
        label: '基础属性 (随等级提升，游戏自动计算)',
        en: 'speEquipData',
        getDict: () => calcKungfuEffect(sk.skillID, sk.lv, 'eq'),
      } : {
        key: 'base',
        label: '基础属性 (随等级提升)',
        en: 'speEquipData',
        ensure: () => {
          if (!sk.speEquipData) sk.speEquipData = { heroSpeAddData: {} };
          if (!sk.speEquipData.heroSpeAddData) sk.speEquipData.heroSpeAddData = {};
        },
        getDict: () => sk.speEquipData.heroSpeAddData,
      },""")
rep("""      {
        key: 'use',
        label: '招式效果 (释放招式时)',
        en: 'speUseData',
        ensure: () => {
          if (!sk.speUseData) sk.speUseData = { heroSpeAddData: {} };
          if (!sk.speUseData.heroSpeAddData) sk.speUseData.heroSpeAddData = {};
        },
        getDict: () => sk.speUseData.heroSpeAddData,
      },""", """      derived ? {
        key: 'use', readOnly: true,
        label: '招式效果 (释放招式时，游戏自动计算)',
        en: 'speUseData',
        getDict: () => calcKungfuEffect(sk.skillID, sk.lv, 'us'),
      } : {
        key: 'use',
        label: '招式效果 (释放招式时)',
        en: 'speUseData',
        ensure: () => {
          if (!sk.speUseData) sk.speUseData = { heroSpeAddData: {} };
          if (!sk.speUseData.heroSpeAddData) sk.speUseData.heroSpeAddData = {};
        },
        getDict: () => sk.speUseData.heroSpeAddData,
      },""")
# lv change should refresh computed groups
rep("""    onExtraInput: (key, val) => {
      const v = parseFloat(val);
      sk[key] = Number.isNaN(v) ? 0 : v;
    },""", """    onExtraInput: (key, val) => {
      const v = parseFloat(val);
      sk[key] = Number.isNaN(v) ? 0 : v;
      if (derived && key === 'lv') refreshDerivedAttrGroups();
    },""")
# renderAttrModal: read-only groups
rep("""        return `<div class="rel-item" data-attr-row="${g.key}-${id}">""", """        if (g.readOnly) return `<div class="rel-item" data-attr-row="${g.key}-${id}">
          <span class="rid">#${id}</span>
          <span style="flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap">${escapeHtml(name)}</span>
          <span style="width:80px;text-align:right;font-size:12px">${+displayVal.toFixed(4)}</span>
          ${isPct ? '<span style="font-size:10px;color:var(--ink-soft);width:14px">%</span>' : '<span style="width:14px"></span>'}
          <span style="width:22px"></span>
        </div>`;
        return `<div class="rel-item" data-attr-row="${g.key}-${id}">""")
rep("""          <div class="section-actions">
            <button class="btn btn-mini" data-attr-add="${g.key}">+ 添加</button>
          </div>""", """          <div class="section-actions">
            ${g.readOnly ? '<span style="font-size:11px;color:var(--ink-soft)">只读：新版存档不保存，由游戏按武学和等级计算</span>' : `<button class="btn btn-mini" data-attr-add="${g.key}">+ 添加</button>`}
          </div>""")
rep("""      <div class="section" style="margin-top:12px">""", """      <div class="section" style="margin-top:12px" data-attr-group="${g.key}">""")
rep("""function closeAttrModal() {""", """// 等级改动后只重画只读的计算组（不重画整个弹窗，避免输入框失去焦点）
function refreshDerivedAttrGroups() {
  if (!attrModalCtx) return;
  const keep = document.activeElement;
  const body = document.getElementById('attrModalBody');
  renderAttrModal();
  // renderAttrModal 会重建整个弹窗，把焦点还给等级输入框
  const lv = body.querySelector('[data-attr-extra-input="lv"]');
  if (lv && keep && keep.dataset && keep.dataset.attrExtraInput === 'lv') { lv.focus(); const n = lv.value.length; try { lv.setSelectionRange(n, n); } catch (e) {} }
}
function closeAttrModal() {""")
rep("      <span>装备中: ${sk.equiped ? '✓' : '✗'}</span>\n    `,\n    onExtraInput: (key, val) => {\n      const v = parseFloat(val);", "      ${sk.equiped === undefined ? '' : `<span>装备中: ${sk.equiped ? '✓' : '✗'}</span>`}\n    `,\n    onExtraInput: (key, val) => {\n      const v = parseFloat(val);")
open(OUT,'w',encoding='utf-8').write(src)
print('built', len(src.encode('utf-8')), 'bytes')
