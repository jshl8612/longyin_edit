
// =====================================================================================
// 文件夹模式：拖入 / 选择 Save 文件夹（或单个 SaveSlotN 文件夹）
//   · 同时载入该槽位的 Hero（人物）与 Save（区域），顶栏切换
//   · 保存时只写改动过的文件；写之前在同目录自动备份 <文件名>.<时间>.bak
//   · 写之前检查磁盘文件自载入后是否被改动（防 Steam 云同步 / 游戏写入覆盖）
// =====================================================================================
state.slot = null;   // { dir, root, name, info, heroH, saveH, heroStamp, saveStamp }
state.mode = state.mode || 'hero';
let slotRoot = null; // 最近一次打开的 Save 根目录（用于「换存档」）

const fsSupported = () => !!window.showDirectoryPicker;
const stripTags = s => String(s || '').replace(/<[^>]*>/g, '');
const stampOf = f => f.lastModified + ':' + f.size;

async function ensurePerm(h, mode) {
  if (!h.queryPermission) return true;
  if ((await h.queryPermission({ mode })) === 'granted') return true;
  return (await h.requestPermission({ mode })) === 'granted';
}
async function childDir(dir, name) { try { return await dir.getDirectoryHandle(name); } catch { return null; } }
async function childFile(dir, name) { try { return await dir.getFileHandle(name); } catch { return null; } }

// 找出用户给的目录是什么：槽位目录 / Save 根目录 / 游戏目录 / Data 目录
async function classifyDir(dir) {
  if (await childFile(dir, 'Hero') && await childFile(dir, 'Save')) return { slot: dir };
  const tryRoot = async d => {
    if (!d) return null;
    for await (const [n, h] of d.entries()) if (h.kind === 'directory' && /^SaveSlot\d+$/.test(n)) return d;
    return null;
  };
  let r = await tryRoot(dir);
  if (!r) r = await tryRoot(await childDir(dir, 'Save'));
  if (!r) { const data = await childDir(dir, 'LongYinLiZhiZhuan_Data'); if (data) r = await tryRoot(await childDir(data, 'Save')); }
  return r ? { root: r } : null;
}

async function openDirectory(dir) {
  if (!(await ensurePerm(dir, 'read'))) { showToast('没有读取权限', true); return; }
  const c = await classifyDir(dir);
  if (!c) { showToast('这个文件夹里没找到存档（应包含 SaveSlot0…10，或 Hero 与 Save 文件）', true); return; }
  if (c.slot) { slotRoot = null; await openSlot(c.slot, null, dir.name); return; }
  slotRoot = c.root;
  await showSlotPicker(c.root);
}

async function readSlotInfo(dir) {
  const out = { character: '', date: '', mode: '', force: '', diff: '', time: '', version: '' };
  const ih = await childFile(dir, 'Info');
  if (ih) {
    try {
      const j = JSON.parse((await readSaveText(await ih.getFile())).text);
      const d = String(j.SaveDetail || '').split('\n');
      out.character = d[0] || ''; out.date = d[1] || ''; out.mode = d[2] || '';
      out.force = stripTags(d[3] || ''); out.diff = d[4] || '';
      out.time = j.SaveTime || ''; out.version = j.SaveVersion || '';
    } catch { }
  }
  return out;
}

async function showSlotPicker(root) {
  setLoading(true, '读取存档列表...');
  try {
    const slots = [];
    for await (const [n, h] of root.entries()) {
      const m = /^SaveSlot(\d+)$/.exec(n);
      if (!m || h.kind !== 'directory') continue;
      const sh = await childFile(h, 'Save'), hh = await childFile(h, 'Hero');
      if (!sh || !hh) continue;
      const sf = await sh.getFile();
      slots.push({ idx: +m[1], name: n, dir: h, info: await readSlotInfo(h), mtime: sf.lastModified });
    }
    slots.sort((a, b) => a.idx - b.idx);
    const list = document.getElementById('slotList');
    list.innerHTML = slots.length ? '' : '<div class="detail-empty">这个文件夹里没有完整的存档（需要同时有 Hero 和 Save）</div>';
    for (const s of slots) {
      const el = document.createElement('div');
      el.className = 'sv-card';
      const t = new Date(s.mtime);
      el.innerHTML = `
        <div class="sv-no">${s.idx}</div>
        <div class="sv-main">
          <div class="sv-name">${escapeHtml(s.info.character || s.name)}<span>${escapeHtml(s.info.date)}</span></div>
          <div class="sv-sub">${escapeHtml(s.info.force || '无门派')}${s.info.mode ? ' · ' + escapeHtml(s.info.mode) : ''}${s.info.diff ? ' · ' + escapeHtml(s.info.diff) : ''}</div>
        </div>
        <div class="sv-time">${t.getMonth() + 1}-${t.getDate()} ${String(t.getHours()).padStart(2, '0')}:${String(t.getMinutes()).padStart(2, '0')}</div>`;
      el.addEventListener('click', () => openSlot(s.dir, root, s.name, s.info));
      list.appendChild(el);
    }
    document.getElementById('slotRootName').textContent = root.name;
    document.getElementById('dropzone').classList.add('hidden');
    document.getElementById('workspace').classList.add('hidden');
    document.getElementById('areaWorkspace').classList.add('hidden');
    document.getElementById('modeSwitch').classList.add('hidden');
    document.getElementById('slotPicker').classList.remove('hidden');
    document.getElementById('fileInfo').textContent = `${root.name} · ${slots.length} 个存档`;
    document.getElementById('btnSave').disabled = true;
    document.getElementById('btnReload').disabled = true;
    document.getElementById('btnSlots').classList.add('hidden');
  } catch (e) { showToast('读取存档列表失败：' + e.message, true); }
  finally { setLoading(false); }
}

function parseHeroArray(text) {
  let data = JSON.parse(text);
  if (!Array.isArray(data)) {
    const k = data && typeof data === 'object' ? Object.keys(data).find(x => Array.isArray(data[x]) && data[x][0]?.heroName !== undefined) : null;
    if (!k) throw new Error('Hero 文件格式与预期不同');
    data = data[k];
  }
  const first = data.find(h => h !== null);
  if (!first || (first.heroName === undefined && first.heroID === undefined)) throw new Error('Hero 文件里没有有效人物');
  return data;
}

async function openSlot(dir, root, name, info) {
  setLoading(true, `读取 ${name} ...`);
  try {
    if (!info) info = await readSlotInfo(dir);
    const heroH = await dir.getFileHandle('Hero'), saveH = await dir.getFileHandle('Save');
    const hf = await heroH.getFile(), sf = await saveH.getFile();
    setLoading(true, '解析人物数据...');
    await new Promise(r => setTimeout(r, 20));
    const heroRd = await readSaveText(hf);
    const heroData = parseHeroArray(heroRd.text);
    setLoading(true, '解析区域数据...');
    await new Promise(r => setTimeout(r, 20));
    const saveRd = await readSaveText(sf);
    const saveText = saveRd.text;
    const saveData = JSON.parse(saveText);
    if (!Array.isArray(saveData.Areas) || !Array.isArray(saveData.Forces)) throw new Error('Save 文件格式与预期不同');
    initAreaData({ name: 'Save', size: sf.size }, saveText, saveData);
    state.fileHandle = null;
    state.slot = { dir, root, name, info, heroH, saveH, heroStamp: stampOf(hf), saveStamp: stampOf(sf), heroFmt: heroRd.fmt, saveFmt: saveRd.fmt };
    applyHeroData({ name: 'Hero', size: hf.size }, heroData);
    document.getElementById('slotPicker').classList.add('hidden');
    document.getElementById('modeSwitch').classList.remove('hidden');
    document.getElementById('btnSlots').classList.toggle('hidden', !root);
    document.getElementById('btnReload').disabled = false;
    showMode(state.lastMode || 'hero');
    renderAreaList(); renderAreaDetail();
    updateSlotInfo();
    showToast(`已打开 ${name}：${info.character || ''} ${info.force || ''}`);
  } catch (e) { showToast('打开存档失败：' + e.message, true); console.error(e); }
  finally { setLoading(false); }
}

function updateSlotInfo() {
  if (!state.slot) return;
  const s = state.slot;
  document.getElementById('fileInfo').textContent = `${s.name} · ${s.info.character || ''} · ${s.info.force || '无门派'} · 改哪个文件写哪个`;
}

function showMode(m) {
  state.mode = m;
  if (state.slot) state.lastMode = m;
  document.getElementById('workspace').classList.toggle('hidden', m !== 'hero');
  document.getElementById('areaWorkspace').classList.toggle('hidden', m !== 'area');
  document.querySelectorAll('#modeSwitch button').forEach(b => {
    b.classList.toggle('active', b.dataset.mode === m);
    const dirty = b.dataset.mode === 'hero' ? state.dirtyIdx.size > 0 : areaDirty() > 0;
    b.classList.toggle('dirty', dirty);
  });
  updateSaveButton();
}

function slotDirtyFiles() {
  const l = [];
  if (state.dirtyIdx.size > 0) l.push('Hero');
  if (areaDirty() > 0) l.push('Save');
  return l;
}

function timeStamp() {
  const d = new Date(), p = n => String(n).padStart(2, '0');
  return `${d.getFullYear()}${p(d.getMonth() + 1)}${p(d.getDate())}_${p(d.getHours())}${p(d.getMinutes())}${p(d.getSeconds())}`;
}

async function writeFile(dir, name, data) {
  const h = await dir.getFileHandle(name, { create: true });
  const w = await h.createWritable();
  await w.write(data);
  await w.close();
  return h;
}

async function saveSlot() {
  const s = state.slot;
  const files = slotDirtyFiles();
  if (!files.length) return;
  try {
    if (!(await ensurePerm(s.dir, 'readwrite'))) throw new Error('没有写入权限');
    // 1) 磁盘上的文件自载入后有没有被改过
    const cur = {};
    for (const f of files) {
      const h = f === 'Hero' ? s.heroH : s.saveH;
      cur[f] = await h.getFile();
      const want = f === 'Hero' ? s.heroStamp : s.saveStamp;
      if (stampOf(cur[f]) !== want) throw new Error(`${s.name}/${f} 在载入后被改动过（可能是游戏或 Steam 云同步写入）。为免覆盖，请点「重新载入」后再改。`);
    }
    const detail = files.map(f => f === 'Hero' ? `Hero（${state.dirtyIdx.size} 位人物）` : `Save（${areaDirty()} 个地块）`).join('、');
    if (!confirm(`将写入 ${s.name} 的：${detail}\n\n写之前会在同一文件夹自动备份原文件（.bak）。\n请确认游戏已完全关闭（建议先关闭 Steam 云存档）。继续？`)) { showToast('已取消'); return; }
    setLoading(true, '生成文件...');
    await new Promise(r => setTimeout(r, 30));
    // 2) 先把新内容全部生成好（任何一步失败都不写盘）
    const out = {};
    const outText = {};
    if (files.includes('Save')) outText.Save = buildAreaText().text;
    if (files.includes('Hero')) outText.Hero = JSON.stringify(state.rawData);
    // 保持原文件格式（新版游戏为 gzip 压缩）
    for (const f of files) out[f] = await encodeSaveText(outText[f], f === 'Hero' ? s.heroFmt : s.saveFmt);
    // 3) 备份 + 写入
    const st = timeStamp(), backups = [];
    for (const f of files) {
      setLoading(true, `备份 ${f} ...`);
      const bak = `${f}.${st}.bak`;
      await writeFile(s.dir, bak, await cur[f].arrayBuffer());
      backups.push(bak);
      setLoading(true, `写入 ${f} ...`);
      await writeFile(s.dir, f, out[f]);
    }
    // 4) 刷新基准
    if (out.Save) { s.saveStamp = stampOf(await s.saveH.getFile()); afterAreaSaved(outText.Save); }
    if (out.Hero) {
      s.heroStamp = stampOf(await s.heroH.getFile());
      state.dirtyIdx.clear();
      document.querySelectorAll('.roster-item.dirty').forEach(el => el.classList.remove('dirty'));
    }
    document.getElementById('btnSave').disabled = true;
    showMode(state.mode);
    showToast(`已写入 ${files.join('、')}，备份：${backups.join('、')}`);
  } catch (e) { showToast('保存失败：' + e.message, true); console.error(e); }
  finally { setLoading(false); }
}

async function reloadSlot() {
  const s = state.slot;
  if (slotDirtyFiles().length && !confirm('将丢弃所有未保存的修改，从磁盘重新读取这个存档？')) return;
  state.dirtyIdx.clear(); area.edits = new Map(); area.bedits = new Map(); area.pick = -1;
  await openSlot(s.dir, s.root, s.name);
}

async function backToSlots() {
  if (!slotRoot) return;
  if (slotDirtyFiles().length && !confirm('有未保存的修改，确定换存档？')) return;
  state.slot = null; state.rawData = null; state.selectedIdx = -1; state.dirtyIdx.clear();
  area.data = null; area.text = null; area.spans = null; area.edits = new Map(); area.bedits = new Map(); area.pick = -1; area.sel = -1;
  await showSlotPicker(slotRoot);
}

async function pickFolder() {
  if (!fsSupported()) { showToast('打开文件夹需要 Edge 或 Chrome 浏览器', true); return; }
  try { await openDirectory(await window.showDirectoryPicker({ id: 'longyin-save', mode: 'readwrite' })); }
  catch (e) { if (e.name !== 'AbortError') showToast('打开文件夹失败：' + e.message, true); }
}

document.getElementById('btnPickDir').addEventListener('click', pickFolder);
document.getElementById('btnSlots').addEventListener('click', backToSlots);
document.getElementById('btnSlotBack').addEventListener('click', () => {
  slotRoot = null;
  document.getElementById('slotPicker').classList.add('hidden');
  document.getElementById('dropzone').classList.remove('hidden');
  document.getElementById('fileInfo').textContent = '';
});
document.getElementById('modeSwitch').addEventListener('click', e => {
  const b = e.target.closest('button'); if (!b) return;
  showMode(b.dataset.mode);
});
