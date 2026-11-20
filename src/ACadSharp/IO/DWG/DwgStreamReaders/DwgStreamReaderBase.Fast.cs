using CSUtilities.Converters;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Runtime.CompilerServices;

namespace ACadSharp.IO.DWG.DwgStreamReaders;

/// <summary>
/// Allocation-free primitives for the bit reader.
/// </summary>
/// <remarks>
/// The <see cref="CSUtilities.IO.StreamIO"/> base allocates on every read: a <c>byte[1]</c> per
/// <see cref="ReadByte"/>, a <c>byte[n]</c> plus a converter delegate per numeric read, and a new
/// converter instance per generic read. The bit reader reads every byte of the objects section
/// through these, which on a large drawing is gigabytes of garbage and most of the read time.
/// These members hide the base ones with identical results; <see cref="IDwgStreamReader"/> is
/// re-implemented by this class, so interface calls bind here too. Only little- and big-endian
/// converters have a fast path; anything else falls back to the base implementation.
/// </remarks>
internal abstract partial class DwgStreamReaderBase
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private byte readRawByte()
	{
		int b = this._stream.ReadByte();
		if (b < 0)
			throw new EndOfStreamException();
		return (byte)b;
	}

	/// <summary>Same as <c>applyShiftToArr</c>, into a span.</summary>
	private void readShifted(Span<byte> arr)
	{
		for (int i = 0; i < arr.Length; i++)
		{
			arr[i] = this.readRawByte();
		}

		if (this.BitShift == 0)
			return;

		int shift = 8 - this.BitShift;
		for (int i = 0; i < arr.Length; ++i)
		{
			byte lastByteValue = (byte)((uint)this._lastByte << this.BitShift);
			this._lastByte = arr[i];
			arr[i] = (byte)(lastByteValue | (uint)(byte)((uint)this._lastByte >> shift));
		}
	}

	private bool defaultIsLittleEndian =>
		BitConverter.IsLittleEndian
		&& (this.EndianConverter is DefaultEndianConverter || this.EndianConverter is LittleEndianConverter);

	private static int endianOf<T>() where T : IEndianConverter
	{
		if (typeof(T) == typeof(LittleEndianConverter)) return 1;
		if (typeof(T) == typeof(BigEndianConverter)) return 2;
		if (typeof(T) == typeof(DefaultEndianConverter)) return BitConverter.IsLittleEndian ? 1 : 2;
		return 0;
	}

	private ulong readUInt64(bool little)
	{
		Span<byte> b = stackalloc byte[8];
		this.readShifted(b);
		return little ? BinaryPrimitives.ReadUInt64LittleEndian(b) : BinaryPrimitives.ReadUInt64BigEndian(b);
	}

	private uint readUInt32(bool little)
	{
		Span<byte> b = stackalloc byte[4];
		this.readShifted(b);
		return little ? BinaryPrimitives.ReadUInt32LittleEndian(b) : BinaryPrimitives.ReadUInt32BigEndian(b);
	}

	private ushort readUInt16(bool little)
	{
		Span<byte> b = stackalloc byte[2];
		this.readShifted(b);
		return little ? BinaryPrimitives.ReadUInt16LittleEndian(b) : BinaryPrimitives.ReadUInt16BigEndian(b);
	}

	private static float toSingle(uint bits) => Unsafe.As<uint, float>(ref bits);

	public new double ReadDouble()
		=> this.defaultIsLittleEndian ? BitConverter.Int64BitsToDouble((long)this.readUInt64(true)) : base.ReadDouble();

	public new int ReadInt()
		=> this.defaultIsLittleEndian ? (int)this.readUInt32(true) : base.ReadInt();

	public new uint ReadUInt()
		=> this.defaultIsLittleEndian ? this.readUInt32(true) : base.ReadUInt();

	public new long ReadLong()
		=> this.defaultIsLittleEndian ? (long)this.readUInt64(true) : base.ReadLong();

	public new ulong ReadULong()
		=> this.defaultIsLittleEndian ? this.readUInt64(true) : base.ReadULong();

	public new short ReadShort()
		=> this.defaultIsLittleEndian ? (short)this.readUInt16(true) : base.ReadShort();

	public new ushort ReadUShort()
		=> this.defaultIsLittleEndian ? this.readUInt16(true) : base.ReadUShort();

	public new float ReadSingle()
		=> this.defaultIsLittleEndian ? toSingle(this.readUInt32(true)) : base.ReadSingle();

	public new double ReadDouble<T>() where T : IEndianConverter, new()
	{
		int e = endianOf<T>();
		return e == 0 ? base.ReadDouble<T>() : BitConverter.Int64BitsToDouble((long)this.readUInt64(e == 1));
	}

	public new int ReadInt<T>() where T : IEndianConverter, new()
	{
		int e = endianOf<T>();
		return e == 0 ? base.ReadInt<T>() : (int)this.readUInt32(e == 1);
	}

	public new uint ReadUInt<T>() where T : IEndianConverter, new()
	{
		int e = endianOf<T>();
		return e == 0 ? base.ReadUInt<T>() : this.readUInt32(e == 1);
	}

	public new long ReadLong<T>() where T : IEndianConverter, new()
	{
		int e = endianOf<T>();
		return e == 0 ? base.ReadLong<T>() : (long)this.readUInt64(e == 1);
	}

	public new ulong ReadULong<T>() where T : IEndianConverter, new()
	{
		int e = endianOf<T>();
		return e == 0 ? base.ReadULong<T>() : this.readUInt64(e == 1);
	}

	public new short ReadShort<T>() where T : IEndianConverter, new()
	{
		int e = endianOf<T>();
		return e == 0 ? base.ReadShort<T>() : (short)this.readUInt16(e == 1);
	}

	public new ushort ReadUShort<T>() where T : IEndianConverter, new()
	{
		int e = endianOf<T>();
		return e == 0 ? base.ReadUShort<T>() : this.readUInt16(e == 1);
	}

	public new float ReadSingle<T>() where T : IEndianConverter, new()
	{
		int e = endianOf<T>();
		return e == 0 ? base.ReadSingle<T>() : toSingle(this.readUInt32(e == 1));
	}
}
