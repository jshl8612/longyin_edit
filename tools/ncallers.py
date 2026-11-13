# 找调用某个地址的位置：python -i ncallers.py GameAssembly.dll methods.tsv  然后 callers(0x180719bc0)
# 注意 IL2CPP 代码在 .text 和 il2cpp 两个段里；共享同一地址的泛型方法名字可能不准，以地址为准。
import struct, re, bisect
exec(open(__file__.replace('ncallers.py', 'ndis.py'), encoding='utf-8').read())
secs = [(data[x.PointerToRawData:x.PointerToRawData + x.SizeOfRawData], base + x.VirtualAddress)
        for x in pe.sections if x.Name.startswith((b'.text', b'il2cpp'))]
def callers(target):
    res = []
    for tb, tva in secs:
        for m in re.finditer(b'\xe8', tb):
            i = m.start()
            if i + 5 > len(tb): break
            rel = struct.unpack_from('<i', tb, i + 1)[0]
            if tva + i + 5 + rel == target:
                va = tva + i; k = bisect.bisect_right(starts, va) - 1
                res.append((hex(va), names[starts[k]][0]))
    return res
