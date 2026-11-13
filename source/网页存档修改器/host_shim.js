// =====================================================================================
// exe 模式（龙胤立志传存档修改器.exe）：页面由 exe 内置的本地服务提供，
//   · 游戏数据（武功、天赋、门派、建筑等）由 exe 从游戏文件里读取后填入
//   · 存档通过本地服务读写（模拟文件夹句柄，沿用文件夹模式的全部逻辑：只写改动文件、自动备份、外部改动检测）
//   · 打开时直接显示存档列表
// 普通网页版（没有 window.__HOST__）时这段不起作用。
// =====================================================================================
(function () {
  const H = window.__HOST__;
  if (!H) return;
  const api = (path, opt) => fetch(path, Object.assign({ headers: { 'X-Token': H.token } }, opt || {}));
  const q = p => encodeURIComponent(p);

  function fileHandle(path, name) {
    return {
      kind: 'file', name,
      queryPermission: async () => 'granted', requestPermission: async () => 'granted',
      async getFile() {
        const r = await api('/api/file?path=' + q(path));
        if (!r.ok) throw new Error('读取失败：' + path);
        const buf = await r.arrayBuffer();
        return new File([buf], name, { lastModified: +r.headers.get('X-Mtime') || Date.now() });
      },
      async createWritable() {
        const parts = [];
        return {
          write: async d => { parts.push(d); },
          close: async () => {
            const body = await new Blob(parts).arrayBuffer();
            const r = await api('/api/write?path=' + q(path), { method: 'POST', body });
            if (!r.ok) throw new Error('写入失败：' + (await r.text()));
          },
        };
      },
    };
  }
  function dirHandle(path, name) {
    const list = async () => {
      const r = await api('/api/list?path=' + q(path));
      if (!r.ok) throw new Error('读取目录失败');
      return r.json();
    };
    const join = n => (path ? path + '/' : '') + n;
    return {
      kind: 'directory', name,
      queryPermission: async () => 'granted', requestPermission: async () => 'granted',
      async *entries() { for (const e of await list()) yield [e.name, e.kind === 'directory' ? dirHandle(join(e.name), e.name) : fileHandle(join(e.name), e.name)]; },
      async getDirectoryHandle(n) {
        const e = (await list()).find(x => x.name === n && x.kind === 'directory');
        if (!e) { const err = new Error('不存在'); err.name = 'NotFoundError'; throw err; }
        return dirHandle(join(n), n);
      },
      async getFileHandle(n, opt) {
        const e = (await list()).find(x => x.name === n && x.kind === 'file');
        if (!e && !(opt && opt.create)) { const err = new Error('不存在'); err.name = 'NotFoundError'; throw err; }
        return fileHandle(join(n), n);
      },
    };
  }

  // 告诉 exe 页面还开着；窗口关掉后 exe 自动退出
  setInterval(() => { api('/api/ping', { method: 'POST' }).catch(() => {}); }, 4000);
  window.addEventListener('beforeunload', () => { try { navigator.sendBeacon('/api/bye?t=' + H.token); } catch (e) {} });

  window.addEventListener('DOMContentLoaded', () => {
    document.body.classList.add('host-mode');
    document.title = '龙胤立志传 存档修改器';
    const fi = document.getElementById('fileInfo');
    if (fi && H.game) fi.title = H.game;
    openDirectory(dirHandle('', 'Save'));
  });
})();
