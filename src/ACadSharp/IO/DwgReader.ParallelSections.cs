using ACadSharp.IO.DWG;
using ACadSharp.IO.DWG.DwgStreamReaders;
using ACadSharp.IO.DWG.FileHeaders;
using System;
using System.IO;
using System.Threading.Tasks;

namespace ACadSharp.IO;

public partial class DwgReader
{
	/// <summary>
	/// <c>getSectionBuffer18</c> with the pages decompressed in parallel, used when the file is
	/// held in memory (a <see cref="MemoryStream"/> whose buffer is visible) and
	/// <see cref="DwgReaderConfiguration.ParallelObjectDecode"/> asks for more than one thread.
	/// </summary>
	/// <remarks>
	/// <para>
	/// R2004+ section pages are compressed independently. The page headers are decrypted
	/// serially, as before, since they are 32 bytes each. Every page is then decompressed from the
	/// file buffer straight into its slot of the section buffer, with a span decompressor that
	/// produces the same bytes as the stream one.
	/// </para>
	/// <para>
	/// Upstream appends pages one after another. If any page other than the last comes out shorter
	/// than a full page, the slots are compacted afterwards, so the layout is identical either way.
	/// </para>
	/// <para>
	/// Returns null when anything does not fit this path, and the caller then runs the serial
	/// code. A page that fails here is therefore read by the original implementation, with its
	/// original error behaviour.
	/// </para>
	/// </remarks>
	private Stream tryGetSectionBuffer18Parallel(DwgFileHeaderAC18 fileheader, string sectionName)
	{
		int workers = this.Configuration.ParallelObjectDecode;
		if (workers == 0)
			workers = Environment.ProcessorCount;
		if (workers <= 1)
			return null;

		if (!(this._fileStream.Stream is MemoryStream fileMemory) || !fileMemory.TryGetBuffer(out ArraySegment<byte> file))
			return null;

		if (!fileheader.Descriptors.TryGetValue(sectionName, out DwgSectionDescriptor descriptor) || !descriptor.IsCompressed)
			return null;

		var pages = descriptor.LocalSections;
		long pageSize = (long)descriptor.DecompressedSize;
		long total = pageSize * pages.Count;
		if (pages.Count < 2 || total > int.MaxValue)
			return null;

		long originalPosition = this._fileStream.Stream.Position;
		try
		{
			// Serial: decrypt every page header, noting where its compressed data starts.
			var dataStart = new long[pages.Count];
			for (int i = 0; i < pages.Count; i++)
			{
				var section = pages[i];
				if (section.IsEmpty)
				{
					if ((long)section.DecompressedSize != pageSize)
						return null;
					continue;
				}

				IDwgStreamReader sreader = DwgStreamReaderBase.GetStreamHandler(fileheader.AcadVersion, this._fileStream.Stream);
				sreader.Position = section.Seeker;
				this.decryptDataSection(section, sreader);
				dataStart[i] = sreader.Position;
			}

			var buffer = new byte[total];
			var written = new int[pages.Count];

			Parallel.For(0, pages.Count, new ParallelOptions { MaxDegreeOfParallelism = workers }, i =>
			{
				var section = pages[i];
				if (section.IsEmpty)
				{
					written[i] = (int)pageSize; // already zero
					return;
				}

				long available = file.Count - dataStart[i];
				var src = new ReadOnlySpan<byte>(file.Array, file.Offset + (int)dataStart[i], (int)available);
				var dst = new Span<byte>(buffer, (int)(i * pageSize), (int)pageSize);
				written[i] = DwgLZ77AC18Decompressor.DecompressToSpan(src, dst);
			});

			// Compact if a page other than the last was short, as the serial append would have been.
			long end = 0;
			for (int i = 0; i < pages.Count; i++)
			{
				long slot = i * pageSize;
				if (end != slot)
				{
					Buffer.BlockCopy(buffer, (int)slot, buffer, (int)end, written[i]);
				}
				end += written[i];
			}
			if (end < total)
			{
				Array.Clear(buffer, (int)end, (int)(total - end));
			}

			return new MemoryStream(buffer, 0, buffer.Length, true, true);
		}
		catch
		{
			this._fileStream.Stream.Position = originalPosition;
			return null;
		}
	}
}
