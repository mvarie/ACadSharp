using System;

namespace ACadSharp.IO.DWG;

internal static partial class DwgLZ77AC18Decompressor
{
	/// <summary>
	/// <see cref="DecompressToDest"/> over spans: same opcodes, same output, no virtual Stream call
	/// per byte. Decompresses one page from <paramref name="src"/> into <paramref name="dst"/> and
	/// returns the number of bytes written. Throws if the page does not fit in <paramref name="dst"/>.
	/// </summary>
	public static int DecompressToSpan(ReadOnlySpan<byte> src, Span<byte> dst)
	{
		int s = 0, d = 0;

		int opcode1 = src[s++];

		if (opcode1 > 0x11)
		{
			opcode1 = copySpan(opcode1 - 17, src, ref s, dst, ref d);
		}

		if ((opcode1 & 0xF0) == 0)
		{
			opcode1 = copySpan(literalCountSpan(opcode1, src, ref s) + 3, src, ref s, dst, ref d);
		}

		while (opcode1 != 0x11)
		{
			int compOffset = 0;
			int compressedBytes = 0;

			if (opcode1 < 0x10 || opcode1 >= 0x40)
			{
				compressedBytes = (opcode1 >> 4) - 1;
				byte opcode2 = src[s++];
				compOffset = ((opcode1 >> 2 & 3) | (opcode2 << 2)) + 1;
			}
			else if (opcode1 < 0x20)
			{
				compressedBytes = readCompressedBytesSpan(opcode1, 0b0111, src, ref s);
				compOffset = (opcode1 & 8) << 11;
				opcode1 = twoByteOffsetSpan(ref compOffset, 0x4000, src, ref s);
			}
			else
			{
				compressedBytes = readCompressedBytesSpan(opcode1, 0b00011111, src, ref s);
				opcode1 = twoByteOffsetSpan(ref compOffset, 1, src, ref s);
			}

			// Back-reference. The stream version copies the min(count, offset) bytes found
			// `offset` back and repeats them; a forward byte copy produces the same bytes.
			int from = d - compOffset;
			if (from < 0 || d + compressedBytes > dst.Length)
				throw new InvalidOperationException("Invalid LZ77 back-reference.");
			for (int i = 0; i < compressedBytes; i++)
			{
				dst[d + i] = dst[from + i];
			}
			d += compressedBytes;

			int litCount = opcode1 & 3;
			if (litCount == 0)
			{
				opcode1 = src[s++];
				if ((opcode1 & 0b11110000) == 0)
				{
					litCount = literalCountSpan(opcode1, src, ref s) + 3;
				}
			}

			if (litCount > 0)
			{
				opcode1 = copySpan(litCount, src, ref s, dst, ref d);
			}
		}

		return d;
	}

	private static byte copySpan(int count, ReadOnlySpan<byte> src, ref int s, Span<byte> dst, ref int d)
	{
		src.Slice(s, count).CopyTo(dst.Slice(d, count));
		s += count;
		d += count;
		return src[s++];
	}

	private static int literalCountSpan(int code, ReadOnlySpan<byte> src, ref int s)
	{
		int lowbits = code & 0b1111;
		if (lowbits == 0)
		{
			byte lastByte;
			for (lastByte = src[s++]; lastByte == 0; lastByte = src[s++])
			{
				lowbits += byte.MaxValue;
			}

			lowbits += 0xF + lastByte;
		}
		return lowbits;
	}

	private static int readCompressedBytesSpan(int opcode1, int validBits, ReadOnlySpan<byte> src, ref int s)
	{
		int compressedBytes = opcode1 & validBits;

		if (compressedBytes == 0)
		{
			byte lastByte;
			for (lastByte = src[s++]; lastByte == 0; lastByte = src[s++])
			{
				compressedBytes += byte.MaxValue;
			}

			compressedBytes += lastByte + validBits;
		}

		return compressedBytes + 2;
	}

	private static int twoByteOffsetSpan(ref int offset, int addedValue, ReadOnlySpan<byte> src, ref int s)
	{
		int firstByte = src[s++];

		offset |= firstByte >> 2;
		offset |= src[s++] << 6;
		offset += addedValue;

		return firstByte;
	}
}
