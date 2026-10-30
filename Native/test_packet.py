#!/usr/bin/env python3
"""Receives one demo packet and checks the P3D1 layout."""

import socket
import struct
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
PORT = 5099
HEADER = 16
JOINTS = 17


def main():
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.bind(("127.0.0.1", PORT))
    sock.settimeout(3.0)
    sender = subprocess.Popen(
        [str(ROOT / "demo_walk_sender"), "--host", "127.0.0.1", "--port", str(PORT), "--count", "2", "--hz", "20"],
        cwd=str(ROOT),
    )
    try:
        data, _addr = sock.recvfrom(2048)
    finally:
        sender.wait(timeout=3)
        sock.close()

    if sender.returncode != 0:
        sys.exit("sender exited %s" % sender.returncode)
    if len(data) != HEADER + JOINTS * 16:
        sys.exit("unexpected size %s" % len(data))
    if data[0:4] != b"P3D1":
        sys.exit("bad magic")
    version, flags, timestamp, count, _reserved = struct.unpack_from("<HHIHH", data, 4)
    if version != 1 or count != 17:
        sys.exit("header %s %s" % (version, count))
    if (flags & 1) == 0:
        sys.exit("camera flag missing")

    joints = []
    for i in range(JOINTS):
        x, y, z, c = struct.unpack_from("<ffff", data, HEADER + i * 16)
        joints.append((x, y, z, c))
        if not all(map(lambda v: v == v and abs(v) < 100.0, (x, y, z, c))):
            sys.exit("non-finite joint %s" % i)
        if c < 0.5:
            sys.exit("low confidence")

    left_shoulder = joints[5]
    right_shoulder = joints[6]
    if not (left_shoulder[0] > right_shoulder[0]):
        sys.exit("person-left shoulder should have the larger camera X")
    # Camera Y is down, so a standing shoulder is above the hip and has a smaller Y.
    left_hip = joints[11]
    if not (left_shoulder[1] < left_hip[1]):
        sys.exit("shoulder should be above the hip in camera Y-down space")
    print("ok", len(data), "bytes", "t", timestamp)


if __name__ == "__main__":
    main()
