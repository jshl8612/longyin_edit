
// =====================================================================================
// 区域模式：读取 Save 文件，编辑城市 / 驻地 / 村镇的可建造地块
//   · 点「城墙圈 / 墙外 / 残垣」把它变成空地；再点一次还原
//   · 城墙会自动沿着新边界往外推（墙外格子变成城墙圈并补地面贴图）
//   · 保存时只替换被改动的地块对象，存档其余内容一字不动
// =====================================================================================
const BUILDING_NAMES = __BNAMES__;
const AREA_TYPE_NAMES = { 0: '城市', 1: '村镇', 2: '门派' };

const area = {
  file: null,       // { name, size }
  text: null,       // 原始文本
  data: null,       // JSON.parse 结果
  spans: null,      // spans[areaIdx][tileIdx] = [start, end)  (文本中的位置)
  sel: -1,          // 选中的区域下标
  edits: new Map(), // areaIdx -> Set(tileIdx)  用户改成空地的格子
  bedits: new Map(), // areaIdx -> Map(tileIdx -> 建筑对象 | null)  新建 / 修改 / 删除的建筑
  pick: -1,         // 正在编辑的地块
  filter: 'all',
  search: '',
};

// ---------- 文本扫描：找出 Areas[*].areaTiles[*] 在原文中的位置 ----------
function scanAreaTileSpans(text) {
  let i = 0;
  const n = text.length;
  const ws = () => { while (i < n) { const c = text.charCodeAt(i); if (c === 32 || c === 10 || c === 13 || c === 9) i++; else break; } };
  const readStr = () => {
    if (text[i] !== '"') throw new Error('扫描存档失败 @' + i);
    let j = i + 1, s = '';
    while (true) {
      const k = text.indexOf('"', j);
      if (k < 0) throw new Error('字符串未闭合');
      let b = 0, p = k - 1;
      while (p >= j && text[p] === '\\') { b++; p--; }
      if (b % 2 === 0) { s = text.slice(i + 1, k); i = k + 1; return s; }
      j = k + 1;
    }
  };
  const skip = () => {
    ws();
    const c = text[i];
    if (c === '"') { readStr(); return; }
    if (c === '{' || c === '[') {
      let depth = 0;
      while (i < n) {
        const ch = text[i];
        if (ch === '"') { readStr(); continue; }
        if (ch === '{' || ch === '[') depth++;
        else if (ch === '}' || ch === ']') { depth--; if (depth === 0) { i++; return; } }
        i++;
      }
      throw new Error('括号未闭合');
    }
    while (i < n && ',}] \n\r\t'.indexOf(text[i]) < 0) i++;
  };
  const eachInObject = (fn) => {
    ws(); if (text[i] !== '{') throw new Error('预期对象 @' + i); i++;
    ws(); if (text[i] === '}') { i++; return; }
    while (true) {
      ws(); const k = readStr(); ws();
      if (text[i] !== ':') throw new Error('预期冒号 @' + i); i++;
      ws(); fn(k);
      ws();
      if (text[i] === ',') { i++; continue; }
      if (text[i] === '}') { i++; return; }
      throw new Error('对象格式错误 @' + i);
    }
  };
  const eachInArray = (fn) => {
    ws(); if (text[i] !== '[') throw new Error('预期数组 @' + i); i++;
    ws(); if (text[i] === ']') { i++; return; }
    let idx = 0;
    while (true) {
      ws(); fn(idx++);
      ws();
      if (text[i] === ',') { i++; continue; }
      if (text[i] === ']') { i++; return; }
      throw new Error('数组格式错误 @' + i);
    }
  };
  if (text.charCodeAt(0) === 0xFEFF) i = 1;
  const spans = [];
  eachInObject(key => {
    if (key !== 'Areas') { skip(); return; }
    eachInArray(ai => {
      const tiles = [];
      eachInObject(k2 => {
        if (k2 !== 'areaTiles') { skip(); return; }
        eachInArray(() => { ws(); const s = i; skip(); tiles.push([s, i]); });
      });
      spans.push(tiles);
    });
  });
  return spans;
}

// ---------- 地块状态 ----------
// kind: void(-3) gate(-2) wall(-1) empty ruin building road hall(2)
function tileKind(t) {
  switch (t.tileType) {
    case -3: return 'void';
    case -2: return 'gate';
    case -1: return 'wall';
    case 1: return 'road';
    case 2: return 'hall';
    case 0:
      if (!t.building) return 'empty';
      return t.building.buildingID === -1 ? 'ruin' : 'building';
    default: return 'other';
  }
}
const isPlayable = k => k === 'empty' || k === 'ruin' || k === 'building' || k === 'road' || k === 'hall';
const groundSprite = (r, c) => ((r + c) % 2 === 0 ? 'Tile_GrayGround' : 'Tile_WhiteGround');

// 计算当前（含修改）的整张图：返回每格 { kind, orig, changed, pushed }
function computeArea(ai) {
  const a = area.data.Areas[ai];
  const W = a.mapWidth, H = a.mapHeight, T = a.areaTiles;
  const set = area.edits.get(ai) || new Set();
  const cells = T.map((t, idx) => ({ idx, r: t.row, c: t.column, orig: tileKind(t), kind: tileKind(t), changed: false, pushed: false, t }));
  for (const idx of set) { cells[idx].kind = 'empty'; cells[idx].changed = true; }
  // 城墙外推：新改出来的空地周围（含斜角）的墙外格(-3)变成城墙圈
  for (const idx of set) {
    const src = cells[idx];
    for (let dr = -1; dr <= 1; dr++) for (let dc = -1; dc <= 1; dc++) {
      const rr = src.r + dr, cc = src.c + dc;
      if (rr < 0 || cc < 0 || rr >= H || cc >= W) continue;
      const n = cells[rr * W + cc];
      if (n.orig === 'void' && !n.changed) { n.kind = 'wall'; n.pushed = true; }
    }
  }
  const bset = area.bedits.get(ai);
  if (bset) for (const [idx, b] of bset) {
    const c = cells[idx];
    c.bedit = true; c.b = b;
    c.kind = b ? (b.buildingID === -1 ? 'ruin' : 'building') : 'empty';
  }
  return { a, W, H, cells };
}
// 当前（含修改）的建筑对象
const cellBuilding = c => (c.bedit ? c.b : (c.changed ? null : (c.t.building || null)));

function buildableCount(cells, useOrig) {
  return cells.filter(x => { const k = useOrig ? x.orig : x.kind; return k === 'empty' || k === 'ruin' || k === 'building'; }).length;
}

// 能否把这一格变成空地；返回 null 表示可以，否则返回原因
function whyNot(model, cell) {
  const { W, H, cells } = model;
  if (cell.orig === 'building') return '这里已经有建筑';
  if (cell.orig === 'empty') return '这里本来就是空地';
  if (cell.orig === 'road' || cell.orig === 'hall') return '道路不能改';
  if (cell.orig === 'gate') return '城门不能改';
  if (cell.orig === 'ruin') return null;
  // wall / void
  if (cell.r === 0 || cell.c === 0 || cell.r === H - 1 || cell.c === W - 1) return '地图最外圈要留给城墙';
  for (let dr = -1; dr <= 1; dr++) for (let dc = -1; dc <= 1; dc++) {
    const rr = cell.r + dr, cc = cell.c + dc;
    if (rr < 0 || cc < 0 || rr >= H || cc >= W) continue;
    if (cells[rr * W + cc].orig === 'gate') return '城门周围一圈要保留为城墙，才能和城门接上';
  }
  const touch = [[1, 0], [-1, 0], [0, 1], [0, -1]].some(([dr, dc]) => {
    const rr = cell.r + dr, cc = cell.c + dc;
    if (rr < 0 || cc < 0 || rr >= H || cc >= W) return false;
    return isPlayable(cells[rr * W + cc].kind);
  });
  if (!touch) return '只能紧挨着城内往外扩（上下左右至少有一格是城内）';
  return null;
}

// ---------- 载入 ----------
// 只初始化区域数据（槽位模式下与人物数据并存）
function initAreaData(file, text, data) {
  const spans = scanAreaTileSpans(text);
  if (spans.length !== data.Areas.length) throw new Error('地块定位与区域数量不一致');
  data.Areas.forEach((a, k) => {
    if (spans[k].length !== a.areaTiles.length) throw new Error('区域 ' + a.areaName + ' 地块数量不一致');
    if (a.areaTiles.length !== a.mapWidth * a.mapHeight) throw new Error('区域 ' + a.areaName + ' 尺寸与地块数不符');
    a.areaTiles.forEach((t, i) => { if (i !== t.row * a.mapWidth + t.column) throw new Error('区域 ' + a.areaName + ' 地块排列与预期不同'); });
  });
  area.file = { name: file.name, size: file.size };
  area.text = text; area.data = data; area.spans = spans;
  area.keepNull = text.indexOf('":null') >= 0;   // 旧版存档会写出 null 字段，新版（1.1.3 起）省略
  area.edits = new Map();
  area.bedits = new Map();   // 建筑编辑：区域 → Map(地块下标 → 新建筑对象 | null=删除)
  area.pick = -1;
  if (area.sel < 0 || area.sel >= data.Areas.length) {
    // 默认选玩家门派驻地（自立门户的门派），没有就选第一个
    const f = data.Forces.find(x => x.replacedForce);
    const k = f ? data.Areas.findIndex(a => a.areaID === f.mainAreaID) : -1;
    area.sel = k >= 0 ? k : 0;
  }
}

// 单文件方式打开 Save
async function loadAreaMode(file, text, data) {
  setLoading(true, '定位地块数据...');
  await new Promise(r => setTimeout(r, 30));
  initAreaData(file, text, data);
  // Hero 模式状态清空
  state.rawData = null; state.selectedIdx = -1; state.dirtyIdx.clear();
  state.mode = 'area';
  document.getElementById('fileInfo').textContent = `${file.name} · ${formatBytes(file.size)} · ${data.Areas.length} 个区域${state.fileHandle ? ' · 可直接覆盖' : ' · 只能另存'}`;
  document.getElementById('dropzone').classList.add('hidden');
  document.getElementById('workspace').classList.add('hidden');
  document.getElementById('areaWorkspace').classList.remove('hidden');
  document.getElementById('btnReload').disabled = false;
  document.getElementById('btnSave').disabled = true;
  updateSaveButton();
  renderAreaList();
  renderAreaDetail();
  showToast(`已载入 ${data.Areas.length} 个区域（Save 存档）`);
}

function leaveAreaMode() {
  area.data = null; area.text = null; area.spans = null; area.edits = new Map(); area.bedits = new Map(); area.pick = -1; area.sel = -1;
  state.mode = 'hero';
  document.getElementById('areaWorkspace').classList.add('hidden');
}

function areaDirty() {
  let n = 0;
  for (const s of area.edits.values()) n += s.size;
  for (const s of area.bedits.values()) n += s.size;
  return n;
}
const areaHasEdits = ai => (area.edits.get(ai)?.size || 0) + (area.bedits.get(ai)?.size || 0);
function refreshAreaSaveButton() {
  const heroDirty = state.slot ? state.dirtyIdx.size > 0 : false;
  document.getElementById('btnSave').disabled = areaDirty() === 0 && !heroDirty;
  updateSaveButton();
}

// ---------- 左侧区域名册 ----------
function forceName(fid) {
  const f = area.data.Forces.find(x => x.forceID === fid);
  if (!f) return '';
  return f.forceSetName || f.forceName;
}
function areaTitle(a) { return a.setAreaName || a.areaName; }

function renderAreaList() {
  const list = document.getElementById('areaList');
  list.innerHTML = '';
  const q = area.search.trim();
  let shown = 0;
  area.data.Areas.forEach((a, k) => {
    if (area.filter !== 'all' && String(a.areaType) !== area.filter) return;
    const owner = forceName(a.belongForceID);
    if (q && !(areaTitle(a) + a.areaName + owner).includes(q)) return;
    shown++;
    const m = computeArea(k);
    const el = document.createElement('div');
    el.className = 'roster-item' + (k === area.sel ? ' active' : '') + (areaHasEdits(k) ? ' dirty' : '');
    const before = buildableCount(m.cells, true), after = buildableCount(m.cells, false);
    el.innerHTML = `
      <div style="flex:1;overflow:hidden">
        <div class="roster-item-name">${escapeHtml(areaTitle(a))}${a.setAreaName && a.setAreaName !== a.areaName ? `<span class="nick">${escapeHtml(a.areaName)}</span>` : ''}</div>
        <div class="roster-item-force">${AREA_TYPE_NAMES[a.areaType] || '区域'}${owner ? ' · ' + escapeHtml(owner) : ''} · ${a.mapWidth}×${a.mapHeight}</div>
      </div>
      <div class="roster-item-meta">${after !== before ? `<span style="color:var(--vermillion)">${after}</span>` : after} 格</div>`;
    el.addEventListener('click', () => { area.sel = k; area.pick = -1; renderAreaList(); renderAreaDetail(); });
    list.appendChild(el);
  });
  document.getElementById('areaCount').textContent = `${shown} / ${area.data.Areas.length} 个区域`;
}

// ---------- 右侧地块图 ----------
function cellLabel(cell) {
  const t = cell.t;
  switch (cell.kind) {
    case 'gate': return '门';
    case 'hall': return t.building ? (BUILDING_NAMES[t.building.buildingID] || '').slice(0, 2) : '中';
    case 'road': return '';
    case 'building': { const b = cellBuilding(cell); return (BUILDING_NAMES[b.buildingID] || ('#' + b.buildingID)).slice(0, 2); }
    case 'ruin': return '残' + (cellBuilding(cell).lv ?? '');
    case 'empty': return cell.changed ? '＋' : '';
    default: return '';
  }
}
function cellTitle(cell) {
  const base = `第 ${cell.r} 行 · 第 ${cell.c} 列（存档坐标）`;
  const names = { void: '墙外', wall: '城墙圈（不可建造）', gate: '城门', road: '道路', hall: '中心', empty: '空地', ruin: '残垣', building: '建筑' };
  let s = names[cell.kind] || cell.kind;
  const b = cell.kind === 'hall' ? cell.t.building : cellBuilding(cell);
  if ((cell.kind === 'building' || cell.kind === 'hall') && b) s += '：' + (BUILDING_NAMES[b.buildingID] || '') + ' ' + (b.lv ?? '') + ' 级';
  if (cell.kind === 'ruin') s += ' ' + (b.lv ?? '') + ' 级';
  if (cell.bedit) s += cell.b ? '　← 建筑已修改' : '　← 建筑已删除';
  if (cell.changed) s += '　← 由城墙 / 墙外改成可建造';
  if (cell.kind === 'empty' || cell.kind === 'building' || cell.kind === 'ruin') s += '\n点击编辑';
  if (cell.pushed) s += '　← 城墙自动外推';
  return base + '\n' + s;
}

function renderAreaDetail() {
  const box = document.getElementById('areaDetail');
  if (area.sel < 0) { box.innerHTML = '<div class="detail-empty">从左侧选择一个区域</div>'; return; }
  const m = computeArea(area.sel);
  const { a, W, H, cells } = m;
  const before = buildableCount(cells, true), after = buildableCount(cells, false);
  const nEdits = areaHasEdits(area.sel);
  const owner = forceName(a.belongForceID);
  let grid = '';
  for (let r = H - 1; r >= 0; r--) {           // 与游戏画面一致：高行号在上
    for (let c = 0; c < W; c++) {
      const cell = cells[r * W + c];
      const editable = cell.kind === 'empty' || cell.kind === 'building' || cell.kind === 'ruin';
      const ok = editable || cell.changed || !whyNot(m, cell);
      const cls = ['tile', 'k-' + cell.kind];
      if (cell.changed) cls.push('changed');
      if (cell.bedit) cls.push('bedited');
      if (cell.idx === area.pick) cls.push('picked');
      if (cell.pushed) cls.push('pushed');
      if (ok) cls.push('clickable');
      grid += `<div class="${cls.join(' ')}" data-idx="${cell.idx}" title="${escapeHtml(cellTitle(cell))}">${escapeHtml(cellLabel(cell))}</div>`;
    }
  }
  box.innerHTML = `
    <div class="detail-head">
      <div class="detail-name">${escapeHtml(areaTitle(a))}</div>
      ${a.setAreaName && a.setAreaName !== a.areaName ? `<div class="detail-nick">原名 ${escapeHtml(a.areaName)}</div>` : ''}
      <div class="detail-tags">
        <span class="detail-tag">${AREA_TYPE_NAMES[a.areaType] || '区域'}</span>
        ${owner ? `<span class="detail-tag">${escapeHtml(owner)}</span>` : ''}
        <span class="detail-tag">${W} × ${H}</span>
        <span class="detail-tag">区域 #${a.areaID}</span>
      </div>
    </div>
    <div class="area-body">
      <div class="area-mapwrap">
        <div class="area-grid" style="grid-template-columns:repeat(${W}, var(--tile))">${grid}</div>
        <div class="area-orient">↑ 与游戏画面方向一致</div>
      </div>
      <div class="area-side">
        <div class="area-stat">
          <div class="area-stat-num">${before}${after !== before ? ` → <b>${after}</b>` : ''}</div>
          <div class="area-stat-label">可建造格（含已有建筑与残垣）${after !== before ? `　<span style="color:var(--vermillion)">+${after - before}</span>` : ''}</div>
        </div>
        ${renderTilePanel(m)}
        <div class="area-actions">
          <button class="btn btn-mini" id="aExpandRing" title="把城墙圈里所有能改的格子变成空地，城墙整体外推一格">城墙内圈 → 空地</button>
          <button class="btn btn-mini" id="aExpandAll" title="在规则允许的范围内，把整张地图尽量扩成可建造">尽量扩满整张图</button>
          <button class="btn btn-mini" id="aClearRuins" title="把所有残垣直接清成空地">清除全部残垣</button>
          <button class="btn btn-mini btn-ghost" id="aResetArea" ${nEdits ? '' : 'disabled'}>撤销本区域修改</button>
        </div>
        <div class="area-legend">
          <div><i class="tile k-empty"></i>空地</div>
          <div><i class="tile k-ruin"></i>残垣（点击编辑）</div>
          <div><i class="tile k-building"></i>建筑（点击编辑）</div>
          <div><i class="tile k-wall clickable"></i>城墙圈（点击变空地）</div>
          <div><i class="tile k-void clickable"></i>墙外（点击变空地）</div>
          <div><i class="tile k-road"></i>道路</div>
          <div><i class="tile k-gate"></i>城门</div>
          <div><i class="tile k-empty changed"></i>本次改成的空地</div>
          <div><i class="tile k-wall pushed"></i>城墙自动外推</div>
          <div><i class="tile k-building bedited"></i>本次修改 / 新建的建筑</div>
        </div>
        <div class="area-note">
          · 点城墙 / 墙外格子把它变成空地，再点一次还原；鼠标停在格子上可看详情。<br>
          · 点空地 / 建筑 / 残垣可以新建、修改、删除建筑。<br>
          · 地图最外圈、城门周围一圈不能改：城墙要画在那里。<br>
          · 只能紧挨着城内往外扩，城墙会自动沿新边界外推。<br>
          · 保存前请完全关闭游戏；建议先关闭 Steam 云存档。
        </div>
      </div>
    </div>`;
  box.querySelector('.area-grid').addEventListener('click', e => {
    const el = e.target.closest('.tile'); if (!el) return;
    const idx = parseInt(el.dataset.idx, 10);
    const cell = m.cells[idx];
    if (cell.kind === 'empty' || cell.kind === 'building' || cell.kind === 'ruin') {
      area.pick = area.pick === idx ? -1 : idx; renderAreaDetail(); return;
    }
    toggleTile(idx);
  });
  bindTilePanel(m);
  box.querySelector('#aExpandRing').addEventListener('click', () => bulk('ring'));
  box.querySelector('#aExpandAll').addEventListener('click', () => bulk('all'));
  box.querySelector('#aClearRuins').addEventListener('click', () => bulk('ruins'));
  box.querySelector('#aResetArea').addEventListener('click', () => { area.edits.delete(area.sel); area.bedits.delete(area.sel); area.pick = -1; afterAreaEdit(); });
}

function editsOf(ai) { if (!area.edits.has(ai)) area.edits.set(ai, new Set()); return area.edits.get(ai); }
function afterAreaEdit() {
  if (area.edits.get(area.sel)?.size === 0) area.edits.delete(area.sel);
  if (area.bedits.get(area.sel)?.size === 0) area.bedits.delete(area.sel);
  renderAreaDetail(); renderAreaList(); refreshAreaSaveButton();
}

function toggleTile(idx) {
  const set = editsOf(area.sel);
  if (set.has(idx)) { set.delete(idx); area.bedits.get(area.sel)?.delete(idx); if (area.pick === idx) area.pick = -1; afterAreaEdit(); return; }
  const m = computeArea(area.sel);
  const why = whyNot(m, m.cells[idx]);
  if (why) { showToast(why, true); return; }
  set.add(idx); afterAreaEdit();
}

function bulk(mode) {
  const set = editsOf(area.sel);
  let added = 0;
  if (mode === 'ruins') {
    computeArea(area.sel).cells.forEach(c => { if (c.orig === 'ruin' && !set.has(c.idx)) { set.add(c.idx); added++; } });
  } else {
    // 反复扩张直到没有新格子可加（ring 模式只处理原本的城墙圈）
    for (let guard = 0; guard < 64; guard++) {
      const m = computeArea(area.sel);
      let step = 0;
      for (const c of m.cells) {
        if (set.has(c.idx)) continue;
        if (mode === 'ring' && c.orig !== 'wall') continue;
        if (mode === 'all' && c.orig !== 'wall' && c.orig !== 'void') continue;
        if (!whyNot(m, c)) { set.add(c.idx); step++; }
      }
      added += step;
      if (step === 0) break;
    }
  }
  afterAreaEdit();
  showToast(added ? `新增 ${added} 块空地` : '没有可以再改的格子');
}

// ---------- 保存：只替换改动过的地块对象 ----------
function buildAreaText() {
  const patches = [];
  const touched = new Set([...area.edits.keys(), ...area.bedits.keys()]);
  touched.forEach(ai => {
    const m = computeArea(ai);
    for (const cell of m.cells) {
      const t = cell.t;
      let nt = null;
      if (cell.changed) {
        nt = Object.assign({}, t, { tileType: 0, building: null });
        if (!area.keepNull) delete nt.building;   // 新版存档不写 null 字段
        if (cell.orig === 'void') nt.spriteName = groundSprite(t.row, t.column);
      } else if (cell.pushed) {
        nt = Object.assign({}, t, { tileType: -1, spriteName: groundSprite(t.row, t.column) });
      }
      if (cell.bedit) {
        nt = Object.assign({}, nt || t);
        if (cell.b) nt.building = cell.b;
        else if (area.keepNull) nt.building = null;
        else delete nt.building;
        nt = orderTileKeys(nt);
      }
      if (nt) patches.push([area.spans[ai][cell.idx][0], area.spans[ai][cell.idx][1], JSON.stringify(nt)]);
    }
  });
  patches.sort((x, y) => y[0] - x[0]);
  let out = area.text;
  for (const [s, e, str] of patches) out = out.slice(0, s) + str + out.slice(e);
  // 校验：能解析、区域与地块数量不变、改动都在
  const chk = JSON.parse(out);
  if (chk.Areas.length !== area.data.Areas.length) throw new Error('校验失败：区域数量变化');
  touched.forEach(ai => {
    const T = chk.Areas[ai].areaTiles;
    if (T.length !== area.data.Areas[ai].areaTiles.length) throw new Error('校验失败：地块数量变化');
    const bset = area.bedits.get(ai) || new Map();
    for (const idx of (area.edits.get(ai) || [])) if (T[idx].tileType !== 0 || (T[idx].building != null && !bset.get(idx))) throw new Error('校验失败：改动未生效');
    for (const [idx, b] of bset) {
      const got = T[idx].building;
      if (b ? (!got || got.buildingID !== b.buildingID || got.lv !== b.lv) : got != null) throw new Error('校验失败：建筑改动未生效');
    }
  });
  return { text: out, count: patches.length };
}

function afterAreaSaved(newText) {
  area.text = newText;
  area.data = JSON.parse(newText);
  area.spans = scanAreaTileSpans(newText);
  area.edits = new Map(); area.bedits = new Map(); area.pick = -1;
  renderAreaList(); renderAreaDetail(); refreshAreaSaveButton();
}

// ---------- 地块编辑面板（新建 / 修改 / 删除建筑）----------
// 地块对象的字段顺序，按存档里已有的「有建筑的地块」来排（新建筑写进原本没有 building 字段的空地时用）
let TILE_KEY_ORDER = null;
function orderTileKeys(nt) {
  if (!TILE_KEY_ORDER) {
    for (const a of area.data.Areas) { const t = a.areaTiles.find(x => x.building); if (t) { TILE_KEY_ORDER = Object.keys(t); break; } }
    if (!TILE_KEY_ORDER) TILE_KEY_ORDER = [];
  }
  const o = {};
  for (const k of TILE_KEY_ORDER) if (k in nt) o[k] = nt[k];
  for (const k of Object.keys(nt)) if (!(k in o)) o[k] = nt[k];
  return o;
}
// 新建筑：字段结构照抄存档里已有的建筑，数值取初始值
function newBuilding(a, buildingID, lv) {
  const rates = a.areaTiles.filter(t => t.building && t.building.buildingID >= 0).map(t => t.building.resourceStoreRate).filter(x => typeof x === 'number').sort((x, y) => x - y);
  return {
    buildingID, lv,
    buildTimeLeft: 0, upgradeTimeLeft: 0, destroyTimeLeft: 0, noCancel: false,
    shopItemList: { heroID: -1, forceID: -1, money: 0, weight: 0.0, maxWeight: -1.0, allItem: [] },
    missionDatas: [], produceRate: 1.0,
    resourceStoreRate: rates.length ? rates[rates.length >> 1] : 2.0,
    areaID: a.areaID, belongHeroID: -1, missionNumCount: 0, plotNumCount: 0, enemyMonth: 0,
  };
}
function buildingOptions(sel) {
  return Object.entries(BUILDING_NAMES).map(([id, n]) => `<option value="${id}"${+id === sel ? ' selected' : ''}>${escapeHtml(n)}（#${id}）</option>`).join('');
}
function renderTilePanel(m) {
  if (area.pick < 0 || !m.cells[area.pick]) return '';
  const cell = m.cells[area.pick];
  const b = cellBuilding(cell);
  const head = `<div class="tp-head">地块 第 ${cell.r} 行 · 第 ${cell.c} 列<button class="icon-btn" id="tpClose" title="关闭">✕</button></div>`;
  const restore = cell.bedit ? `<button class="btn btn-mini btn-ghost" id="tpRestore">还原这一格</button>` : '';
  if (!b) {
    return `<div class="tile-panel">${head}
      <div class="tp-row">空地${cell.changed ? '（本次扩建）' : ''}${cell.bedit ? '（建筑已删除）' : ''}</div>
      <div class="tp-row"><label>新建</label><select id="tpType">${buildingOptions(-99)}</select></div>
      <div class="tp-row"><label>等级</label><input type="number" id="tpLv" value="1" min="0" max="10"></div>
      <div class="tp-btns"><button class="btn btn-mini" id="tpBuild">建造</button>${restore}</div>
      <div class="tp-note">直接以成品出现（不需要建造时间）。门派特殊建筑、唯一建筑请自行注意不要重复。</div>
    </div>`;
  }
  const owned = b.belongHeroID !== undefined && b.belongHeroID !== -1;
  const isRuin = b.buildingID === -1;
  return `<div class="tile-panel">${head}
    <div class="tp-row"><label>建筑</label><select id="tpType">${isRuin ? '<option value="-1" selected>残垣（#-1）</option>' : ''}${buildingOptions(b.buildingID)}</select></div>
    <div class="tp-row"><label>等级</label><input type="number" id="tpLv" value="${b.lv ?? 0}" min="0" max="10"></div>
    <div class="tp-row"><label>产出效率</label><input type="number" id="tpProd" value="${b.produceRate ?? 1}" step="0.1"></div>
    <div class="tp-row"><label>仓储系数</label><input type="number" id="tpStore" value="${b.resourceStoreRate ?? 1}" step="0.1"></div>
    ${b.buildTimeLeft || b.upgradeTimeLeft || b.destroyTimeLeft ? `<div class="tp-row tp-dim">建造 / 升级 / 拆除剩余：${b.buildTimeLeft || 0} / ${b.upgradeTimeLeft || 0} / ${b.destroyTimeLeft || 0}<label style="width:auto;margin-left:8px"><input type="checkbox" id="tpFinish"> 立即完成</label></div>` : ''}
    ${owned ? `<div class="tp-row tp-dim">属于人物 #${b.belongHeroID}（私宅等）</div>` : ''}
    <div class="tp-btns">
      <button class="btn btn-mini" id="tpApply">应用修改</button>
      <button class="btn btn-mini" id="tpDelete" ${owned ? 'disabled title="属于人物的建筑（如私宅）不能删除"' : ''}>${isRuin ? '清除残垣' : '删除建筑'}</button>
      ${restore}
    </div>
  </div>`;
}
function setBuildingEdit(idx, b) {
  if (!area.bedits.has(area.sel)) area.bedits.set(area.sel, new Map());
  area.bedits.get(area.sel).set(idx, b);
  afterAreaEdit();
}
function bindTilePanel(m) {
  const box = document.getElementById('areaDetail');
  const q = id => box.querySelector('#' + id);
  if (!q('tpClose')) return;
  const cell = m.cells[area.pick];
  const num = (id, d) => { const v = parseFloat(q(id)?.value); return Number.isNaN(v) ? d : v; };
  q('tpClose').addEventListener('click', () => { area.pick = -1; renderAreaDetail(); });
  q('tpRestore')?.addEventListener('click', () => { area.bedits.get(area.sel)?.delete(cell.idx); afterAreaEdit(); });
  q('tpBuild')?.addEventListener('click', () => {
    const id = parseInt(q('tpType').value, 10);
    const lv = Math.max(0, Math.min(10, Math.round(num('tpLv', 1))));
    setBuildingEdit(cell.idx, newBuilding(m.a, id, lv));
    showToast('已新建：' + (BUILDING_NAMES[id] || '#' + id));
  });
  q('tpApply')?.addEventListener('click', () => {
    const cur = cellBuilding(cell);
    const nb = JSON.parse(JSON.stringify(cur));
    nb.buildingID = parseInt(q('tpType').value, 10);
    nb.lv = Math.max(0, Math.min(10, Math.round(num('tpLv', cur.lv ?? 0))));
    nb.produceRate = num('tpProd', cur.produceRate ?? 1);
    nb.resourceStoreRate = num('tpStore', cur.resourceStoreRate ?? 1);
    if (q('tpFinish')?.checked) { nb.buildTimeLeft = 0; nb.upgradeTimeLeft = 0; nb.destroyTimeLeft = 0; }
    setBuildingEdit(cell.idx, nb);
    showToast('已修改建筑');
  });
  q('tpDelete')?.addEventListener('click', () => { setBuildingEdit(cell.idx, null); showToast('已删除建筑'); });
}

// 名册筛选 / 搜索
document.getElementById('areaFilters').addEventListener('click', e => {
  const chip = e.target.closest('.filter-chip'); if (!chip) return;
  area.filter = chip.dataset.afilter;
  document.querySelectorAll('#areaFilters .filter-chip').forEach(c => c.classList.toggle('active', c === chip));
  renderAreaList();
});
document.getElementById('areaSearch').addEventListener('input', e => { area.search = e.target.value; renderAreaList(); });
