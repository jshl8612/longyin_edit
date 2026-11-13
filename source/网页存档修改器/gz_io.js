// =====================================================================================
// 存档文件容器：旧版存档是纯 JSON；游戏 2026-11 版（1.1.3）起存档是 gzip 压缩的 JSON。
// 读取时自动识别，写回时保持原来的格式（gzip 头部字节也照抄原文件）。
// =====================================================================================
const isGzipBytes = u8 => u8.length >= 2 && u8[0] === 0x1f && u8[1] === 0x8b;

async function readSaveText(file) {
  const u8 = new Uint8Array(await file.arrayBuffer());
  if (!isGzipBytes(u8)) return { text: new TextDecoder('utf-8').decode(u8), fmt: { gz: false } };
  if (typeof DecompressionStream === 'undefined') throw new Error('这个存档是压缩格式，需要新版 Edge / Chrome 才能读取');
  const ds = new Blob([u8]).stream().pipeThrough(new DecompressionStream('gzip'));
  const text = await new Response(ds).text();
  return { text, fmt: { gz: true, head: u8.slice(0, 10) } };
}

// 返回要写入磁盘的数据（字符串或 Uint8Array）
async function encodeSaveText(text, fmt) {
  if (!fmt || !fmt.gz) return text;
  const cs = new Blob([new TextEncoder().encode(text)]).stream().pipeThrough(new CompressionStream('gzip'));
  const out = new Uint8Array(await new Response(cs).arrayBuffer());
  // 头部 MTIME/XFL/OS 照抄游戏原文件（不参与 CRC 校验）
  if (fmt.head && out.length > 10 && (out[3] & 0x1e) === 0 && (fmt.head[3] & 0x1e) === 0) out.set(fmt.head.slice(4, 10), 4);
  // 校验：解压回来必须和原文一致
  const back = await new Response(new Blob([out]).stream().pipeThrough(new DecompressionStream('gzip'))).text();
  if (back !== text) throw new Error('压缩校验失败，未写入');
  return out;
}
