using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace fNbt {
    /// <summary> BinaryReader wrapper that takes care of reading primitives from an NBT stream,
    /// while taking care of endianness, string encoding, and skipping. </summary>
    internal sealed class NbtBinaryReader : BinaryReader {
        readonly byte[] buffer = new byte[sizeof(double)];

        byte[]? seekBuffer;
        const int SeekBufferSize = 8 * 1024;
        readonly bool swapNeeded;
        readonly bool useVarInt;
        readonly byte[] stringConversionBuffer = new byte[64];


        public NbtBinaryReader(Stream input, bool bigEndian, bool varInt = false)
            : base(input) {
            swapNeeded = (BitConverter.IsLittleEndian == bigEndian);
            useVarInt = varInt;
        }


        public NbtTagType ReadTagType() {
            int type = ReadByte();
            if (type < 0) {
                throw new EndOfStreamException();
            } else if (type > (int)NbtTagType.LongArray) {
                throw new NbtFormatException("NBT tag type out of range: " + type);
            }
            return (NbtTagType)type;
        }


        public override short ReadInt16() {
            if (swapNeeded) {
                return Swap(base.ReadInt16());
            } else {
                return base.ReadInt16();
            }
        }


        public override int ReadInt32() {
            int value = 0;
            if (useVarInt) {
                value = ReadVarInt();
            } else {
                value = base.ReadInt32();
            }

            if (swapNeeded) {
                return Swap(value);
            } else {
                return value;
            }
        }


        public override long ReadInt64() {
            if (swapNeeded) {
                return Swap(base.ReadInt64());
            } else {
                return base.ReadInt64();
            }
        }


        public override float ReadSingle() {
            if (swapNeeded) {
                FillBuffer(sizeof(float));
                Array.Reverse(buffer, 0, sizeof(float));
                return BitConverter.ToSingle(buffer, 0);
            } else {
                return base.ReadSingle();
            }
        }


        public override double ReadDouble() {
            if (swapNeeded) {
                FillBuffer(sizeof(double));
                Array.Reverse(buffer);
                return BitConverter.ToDouble(buffer, 0);
            }
            return base.ReadDouble();
        }


        public override string ReadString() {
            short length = 0;
            if (useVarInt) {
                length = ReadByte();
            } else {
                length = ReadInt16();
            }
            if (length < 0) {
                throw new NbtFormatException("Negative string length given!");
            }
            if (length < stringConversionBuffer.Length) {
                int stringBytesRead = 0;
                while (stringBytesRead < length) {
                    int bytesToRead = length - stringBytesRead;
                    int bytesReadThisTime = BaseStream.Read(stringConversionBuffer, stringBytesRead, bytesToRead);
                    if (bytesReadThisTime == 0) {
                        throw new EndOfStreamException();
                    }
                    stringBytesRead += bytesReadThisTime;
                }
                return Encoding.UTF8.GetString(stringConversionBuffer, 0, length);
            } else {
                byte[] stringData = ReadBytes(length);
                if (stringData.Length < length) {
                    throw new EndOfStreamException();
                }
                return Encoding.UTF8.GetString(stringData);
            }
        }

        public int ReadVarInt() {
            int numRead = 0;
            int result = 0;
            byte read;

            do {
                int current = BaseStream.ReadByte();
                if (current == -1) {
                    throw new EndOfStreamException();
                }

                read = (byte)current;

                int value = (read & 0b0111_1111);
                result |= (value << (7 * numRead));

                numRead++;

                if (numRead > 5) {
                    throw new FormatException("VarInt is too big");
                }
            }
            while ((read & 0b1000_0000) != 0);

            return result;
        }


        public void Skip(int bytesToSkip) {
            if (bytesToSkip < 0) {
                throw new ArgumentOutOfRangeException(nameof(bytesToSkip));
            } else if (BaseStream.CanSeek) {
                BaseStream.Position += bytesToSkip;
            } else if (bytesToSkip != 0) {
                if (seekBuffer == null) seekBuffer = new byte[SeekBufferSize];
                int bytesSkipped = 0;
                while (bytesSkipped < bytesToSkip) {
                    int bytesToRead = Math.Min(SeekBufferSize, bytesToSkip - bytesSkipped);
                    int bytesReadThisTime = BaseStream.Read(seekBuffer, 0, bytesToRead);
                    if (bytesReadThisTime == 0) {
                        throw new EndOfStreamException();
                    }
                    bytesSkipped += bytesReadThisTime;
                }
            }
        }


        new void FillBuffer(int numBytes) {
            int offset = 0;
            do {
                int num = BaseStream.Read(buffer, offset, numBytes - offset);
                if (num == 0) throw new EndOfStreamException();
                offset += num;
            } while (offset < numBytes);
        }


        public void SkipString() {
            short length = 0;
            if (useVarInt) {
                length = ReadByte();
            } else {
                length = ReadInt16();
            }
            if (length < 0) {
                throw new NbtFormatException("Negative string length given!");
            }
            Skip(length);
        }


        [DebuggerStepThrough]
        static short Swap(short v) {
            unchecked {
                return (short)((v >> 8) & 0x00FF |
                               (v << 8) & 0xFF00);
            }
        }


        [DebuggerStepThrough]
        static int Swap(int v) {
            unchecked {
                var v2 = (uint)v;
                return (int)((v2 >> 24) & 0x000000FF |
                             (v2 >> 8) & 0x0000FF00 |
                             (v2 << 8) & 0x00FF0000 |
                             (v2 << 24) & 0xFF000000);
            }
        }


        [DebuggerStepThrough]
        static long Swap(long v) {
            unchecked {
                return (Swap((int)v) & uint.MaxValue) << 32 |
                       Swap((int)(v >> 32)) & uint.MaxValue;
            }
        }


        public TagSelector? Selector { get; set; }
    }
}
