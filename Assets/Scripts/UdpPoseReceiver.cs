using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace SkeletonPose
{
    /// <summary>
    /// Listens for COCO-17 poses. Binary layout matches Native/include/pose_stream.h.
    /// A UTF-8 JSON packet is also accepted:
    /// {"t":1000,"flags":1,"joints":[x,y,z,confidence, ...]}
    /// flags bit 0 = camera Y-down, bit 1 = millimeters. JSON defaults to camera meters.
    /// </summary>
    public sealed class UdpPoseReceiver
    {
        readonly object _gate = new object();
        readonly PoseFrame _scratch = new PoseFrame();
        readonly PoseFrame _published = new PoseFrame();
        UdpClient _client;
        Thread _thread;
        volatile bool _running;
        bool _has;
        int _packets;
        long _lastTicks;
        string _description = "";
        string _error = "";
        bool _warnedLayout;

        public string Error => _error;
        public string Description => _description;
        public int PacketCount { get { lock (_gate) return _packets; } }

        public void Start(int port)
        {
            Stop();
            _error = "";
            try
            {
                _client = new UdpClient(AddressFamily.InterNetwork);
                _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _client.Client.Bind(new IPEndPoint(IPAddress.Any, port));
                _client.Client.ReceiveTimeout = 200;
            }
            catch (Exception ex)
            {
                _error = "UDP port " + port + " is not available (" + ex.Message + "). Demo motion is still playing.";
                Debug.LogError(_error);
                return;
            }

            _running = true;
            _thread = new Thread(Listen) { IsBackground = true, Name = "PoseUdp" };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            if (_client != null)
            {
                try { _client.Close(); }
                catch (Exception) { }
                _client = null;
            }
            if (_thread != null)
            {
                try { _thread.Join(400); }
                catch (Exception) { }
                _thread = null;
            }
        }

        public bool TryCopy(PoseFrame dst, out int packets, out float ageSeconds)
        {
            lock (_gate)
            {
                packets = _packets;
                ageSeconds = _has
                    ? (float)((System.Diagnostics.Stopwatch.GetTimestamp() - _lastTicks) / (double)System.Diagnostics.Stopwatch.Frequency)
                    : 0f;
                if (!_has)
                    return false;
                dst.CopyFrom(_published);
                return true;
            }
        }

        void Listen()
        {
            while (_running)
            {
                UdpClient client = _client;
                if (client == null)
                    break;
                try
                {
                    IPEndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = client.Receive(ref remote);
                    if (data == null || data.Length < 2)
                        continue;
                    string error;
                    if (!TryParse(data, _scratch, ref _warnedLayout, out error))
                    {
                        if (!string.IsNullOrEmpty(error))
                            _error = error;
                        continue;
                    }

                    lock (_gate)
                    {
                        _published.CopyFrom(_scratch);
                        _has = true;
                        _packets++;
                        _lastTicks = System.Diagnostics.Stopwatch.GetTimestamp();
                        _description = Describe(_scratch, data.Length);
                        _error = "";
                    }
                }
                catch (SocketException)
                {
                    if (!_running)
                        break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _error = ex.Message;
                }
            }
        }

        static string Describe(PoseFrame frame, int bytes)
        {
            string space = frame.Space == PoseSpace.CameraYDown ? "camera Y-down" : "Unity Y-up";
            string unit = frame.UnitScale < 0.5f ? "millimeters" : "meters";
            return "UDP " + bytes + " bytes, " + space + ", " + unit;
        }

        public static bool TryParse(byte[] data, PoseFrame dst, ref bool warnedLayout, out string error)
        {
            error = "";
            if (data[0] == (byte)'{')
                return TryParseJson(data, dst, out error);
            return TryParseBinary(data, dst, ref warnedLayout, out error);
        }

        static bool TryParseBinary(byte[] data, PoseFrame dst, ref bool warnedLayout, out string error)
        {
            error = "";
            if (data.Length < PoseProtocol.HeaderSize)
            {
                error = "Pose packet is shorter than the header.";
                return false;
            }
            if (data[0] != PoseProtocol.Magic0 || data[1] != PoseProtocol.Magic1 ||
                data[2] != PoseProtocol.Magic2 || data[3] != PoseProtocol.Magic3)
            {
                error = "Pose packet magic is not P3D1.";
                return false;
            }

            ushort version = ReadU16(data, 4);
            ushort flags = ReadU16(data, 6);
            uint timestamp = ReadU32(data, 8);
            ushort count = ReadU16(data, 12);
            if (version != PoseProtocol.Version)
            {
                error = "Unsupported pose version " + version + ".";
                return false;
            }
            if (count != PoseProtocol.JointCount && count != PoseProtocol.Body25Count)
            {
                if (!warnedLayout)
                {
                    warnedLayout = true;
                    error = "Expected 17 COCO joints or 25 BODY_25 joints, received " + count + ".";
                }
                return false;
            }
            int need = PoseProtocol.HeaderSize + count * 16;
            if (data.Length < need)
            {
                error = "Pose packet truncated.";
                return false;
            }

            dst.Valid = true;
            dst.TimestampMs = timestamp;
            dst.Space = (flags & PoseProtocol.FlagCameraYDown) != 0 ? PoseSpace.CameraYDown : PoseSpace.UnityYUp;
            dst.UnitScale = (flags & PoseProtocol.FlagMillimeters) != 0 ? 0.001f : 1f;

            if (count == PoseProtocol.Body25Count)
                ReadBody25(data, PoseProtocol.HeaderSize, dst);
            else
                ReadCoco(data, PoseProtocol.HeaderSize, count, dst);
            return true;
        }

        static void ReadCoco(byte[] data, int offset, int count, PoseFrame dst)
        {
            int n = Mathf.Min(count, PoseProtocol.JointCount);
            for (int i = 0; i < PoseProtocol.JointCount; i++)
            {
                if (i < n)
                {
                    int o = offset + i * 16;
                    dst.Position[i] = new Vector3(ReadF32(data, o), ReadF32(data, o + 4), ReadF32(data, o + 8));
                    dst.Confidence[i] = ReadF32(data, o + 12);
                }
                else
                {
                    dst.Position[i] = Vector3.zero;
                    dst.Confidence[i] = 0f;
                }
                dst.Tracked[i] = false;
            }
        }

        static void ReadBody25(byte[] data, int offset, PoseFrame dst)
        {
            for (int i = 0; i < PoseProtocol.JointCount; i++)
            {
                dst.Position[i] = Vector3.zero;
                dst.Confidence[i] = 0f;
                dst.Tracked[i] = false;
            }
            // OpenPose BODY_25 -> COCO subject-left.
            Take(data, offset, dst, 0, JointId.Nose);
            Take(data, offset, dst, 16, JointId.LeftEye);
            Take(data, offset, dst, 15, JointId.RightEye);
            Take(data, offset, dst, 18, JointId.LeftEar);
            Take(data, offset, dst, 17, JointId.RightEar);
            Take(data, offset, dst, 5, JointId.LeftShoulder);
            Take(data, offset, dst, 2, JointId.RightShoulder);
            Take(data, offset, dst, 6, JointId.LeftElbow);
            Take(data, offset, dst, 3, JointId.RightElbow);
            Take(data, offset, dst, 7, JointId.LeftWrist);
            Take(data, offset, dst, 4, JointId.RightWrist);
            Take(data, offset, dst, 12, JointId.LeftHip);
            Take(data, offset, dst, 9, JointId.RightHip);
            Take(data, offset, dst, 13, JointId.LeftKnee);
            Take(data, offset, dst, 10, JointId.RightKnee);
            Take(data, offset, dst, 14, JointId.LeftAnkle);
            Take(data, offset, dst, 11, JointId.RightAnkle);
        }

        static void Take(byte[] data, int offset, PoseFrame dst, int srcIndex, JointId dstJoint)
        {
            int o = offset + srcIndex * 16;
            int i = (int)dstJoint;
            dst.Position[i] = new Vector3(ReadF32(data, o), ReadF32(data, o + 4), ReadF32(data, o + 8));
            dst.Confidence[i] = ReadF32(data, o + 12);
        }

        static bool TryParseJson(byte[] data, PoseFrame dst, out string error)
        {
            error = "";
            string text;
            try { text = Encoding.UTF8.GetString(data); }
            catch (Exception)
            {
                error = "JSON pose is not UTF-8.";
                return false;
            }

            int key = text.IndexOf("\"joints\"", StringComparison.Ordinal);
            if (key < 0)
            {
                error = "JSON pose has no joints array.";
                return false;
            }
            int open = text.IndexOf('[', key);
            int close = open >= 0 ? text.IndexOf(']', open) : -1;
            if (open < 0 || close < 0)
            {
                error = "JSON joints array is malformed.";
                return false;
            }

            string[] parts = text.Substring(open + 1, close - open - 1).Split(',');
            if (parts.Length != PoseProtocol.JointCount * 4 && parts.Length != PoseProtocol.Body25Count * 4)
            {
                error = "JSON joints length must be 68 (COCO) or 100 (BODY_25).";
                return false;
            }

            float[] values = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                {
                    error = "JSON joint value is not a number.";
                    return false;
                }
            }

            ushort flags = 1; // camera Y-down meters unless the packet says otherwise
            int flagKey = text.IndexOf("\"flags\"", StringComparison.Ordinal);
            if (flagKey >= 0)
            {
                int colon = text.IndexOf(':', flagKey);
                if (colon > 0)
                {
                    int end = colon + 1;
                    while (end < text.Length && (text[end] == ' ' || text[end] == '\t'))
                        end++;
                    int start = end;
                    while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '-'))
                        end++;
                    ushort parsed;
                    if (ushort.TryParse(text.Substring(start, end - start), out parsed))
                        flags = parsed;
                }
            }

            uint timestamp = 0;
            int tKey = text.IndexOf("\"t\"", StringComparison.Ordinal);
            if (tKey >= 0)
            {
                int colon = text.IndexOf(':', tKey);
                if (colon > 0)
                {
                    int end = colon + 1;
                    while (end < text.Length && (text[end] == ' ' || text[end] == '\t'))
                        end++;
                    int start = end;
                    while (end < text.Length && char.IsDigit(text[end]))
                        end++;
                    uint parsed;
                    if (uint.TryParse(text.Substring(start, end - start), out parsed))
                        timestamp = parsed;
                }
            }

            dst.Valid = true;
            dst.TimestampMs = timestamp;
            dst.Space = (flags & PoseProtocol.FlagCameraYDown) != 0 ? PoseSpace.CameraYDown : PoseSpace.UnityYUp;
            dst.UnitScale = (flags & PoseProtocol.FlagMillimeters) != 0 ? 0.001f : 1f;

            if (parts.Length == PoseProtocol.Body25Count * 4)
            {
                // Reuse the binary mapper by packing a tiny synthetic buffer.
                byte[] packed = new byte[PoseProtocol.HeaderSize + PoseProtocol.Body25Count * 16];
                for (int i = 0; i < PoseProtocol.Body25Count; i++)
                    WriteJoint(packed, PoseProtocol.HeaderSize + i * 16, values, i * 4);
                bool ignored = false;
                return TryParseBinary(Stamp(packed, flags, timestamp, PoseProtocol.Body25Count), dst, ref ignored, out error);
            }

            for (int i = 0; i < PoseProtocol.JointCount; i++)
            {
                int o = i * 4;
                dst.Position[i] = new Vector3(values[o], values[o + 1], values[o + 2]);
                dst.Confidence[i] = values[o + 3];
                dst.Tracked[i] = false;
            }
            return true;
        }

        static byte[] Stamp(byte[] packed, ushort flags, uint timestamp, ushort count)
        {
            packed[0] = PoseProtocol.Magic0;
            packed[1] = PoseProtocol.Magic1;
            packed[2] = PoseProtocol.Magic2;
            packed[3] = PoseProtocol.Magic3;
            WriteU16(packed, 4, PoseProtocol.Version);
            WriteU16(packed, 6, flags);
            WriteU32(packed, 8, timestamp);
            WriteU16(packed, 12, count);
            return packed;
        }

        static void WriteJoint(byte[] dst, int offset, float[] values, int valueIndex)
        {
            WriteF32(dst, offset, values[valueIndex]);
            WriteF32(dst, offset + 4, values[valueIndex + 1]);
            WriteF32(dst, offset + 8, values[valueIndex + 2]);
            WriteF32(dst, offset + 12, values[valueIndex + 3]);
        }

        static ushort ReadU16(byte[] b, int o)
        {
            return (ushort)(b[o] | (b[o + 1] << 8));
        }

        static uint ReadU32(byte[] b, int o)
        {
            return (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
        }

        static float ReadF32(byte[] b, int o)
        {
            if (BitConverter.IsLittleEndian)
                return BitConverter.ToSingle(b, o);
            byte[] tmp = { b[o + 3], b[o + 2], b[o + 1], b[o] };
            return BitConverter.ToSingle(tmp, 0);
        }

        static void WriteU16(byte[] b, int o, ushort v)
        {
            b[o] = (byte)(v & 0xff);
            b[o + 1] = (byte)((v >> 8) & 0xff);
        }

        static void WriteU32(byte[] b, int o, uint v)
        {
            b[o] = (byte)(v & 0xff);
            b[o + 1] = (byte)((v >> 8) & 0xff);
            b[o + 2] = (byte)((v >> 16) & 0xff);
            b[o + 3] = (byte)((v >> 24) & 0xff);
        }

        static void WriteF32(byte[] b, int o, float v)
        {
            byte[] raw = BitConverter.GetBytes(v);
            if (!BitConverter.IsLittleEndian)
                Array.Reverse(raw);
            b[o] = raw[0];
            b[o + 1] = raw[1];
            b[o + 2] = raw[2];
            b[o + 3] = raw[3];
        }
    }
}
