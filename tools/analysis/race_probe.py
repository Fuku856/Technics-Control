"""Read-only RACE probe over Windows RFCOMM (Winsock, connect by service UUID)."""
import ctypes, socket, struct, sys, time, uuid

AF_BTH, BTHPROTO_RFCOMM = 32, 3

class SOCKADDR_BTH(ctypes.Structure):
    _pack_ = 1
    _fields_ = [("addressFamily", ctypes.c_ushort), ("btAddr", ctypes.c_ulonglong),
                ("serviceClassId", ctypes.c_byte * 16), ("port", ctypes.c_ulong)]

def connect(addr_hex, svc_uuid):
    s = socket.socket(AF_BTH, socket.SOCK_STREAM, BTHPROTO_RFCOMM)
    sa = SOCKADDR_BTH()
    sa.addressFamily = AF_BTH
    sa.btAddr = int(addr_hex, 16)
    sa.serviceClassId[:] = list(struct.unpack('16b', uuid.UUID(svc_uuid).bytes_le))
    sa.port = 0
    ws2 = ctypes.WinDLL('ws2_32')
    if ws2.connect(ctypes.c_size_t(s.fileno()), ctypes.byref(sa), ctypes.sizeof(sa)) != 0:
        raise OSError(f"connect failed WSAError={ws2.WSAGetLastError()}")
    return s

def race(id_, payload=b'', typ=0x5A):
    return struct.pack('<BBHH', 0x05, typ, len(payload) + 2, id_) + payload

def hexs(b): return ' '.join(f'{x:02X}' for x in b)

def main():
    addr = sys.argv[1]
    ids = [(int(x.split(":")[0], 16), bytes.fromhex(x.split(":")[1]) if ":" in x else b"") for x in sys.argv[2:]]
    s = connect(addr, '00000000-0000-0000-0099-aabbccddeeff')
    s.settimeout(0.5)
    print("connected")
    for i, pl in ids:
        pkt = race(i, pl)
        s.send(pkt)
        print("TX", hexs(pkt))
        end = time.time() + 2.5
        while time.time() < end:
            try:
                d = s.recv(4096)
            except socket.timeout:
                continue
            if not d:
                break
            print("RX", hexs(d), "|", ''.join(chr(c) if 32 <= c < 127 else '.' for c in d))
    s.close()
    print("closed")

main()
