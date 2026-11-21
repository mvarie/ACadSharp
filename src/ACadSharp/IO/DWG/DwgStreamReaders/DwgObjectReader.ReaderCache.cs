using ACadSharp.IO.DWG.DwgStreamReaders;

namespace ACadSharp.IO.DWG
{
	internal partial class DwgObjectReader
	{
		// The section readers getEntityType positions for each object. They used to be created
		// per object (three stream clones, three readers, three endian converters each); every
		// use starts with SetPositionInBits/SetPositionByFlag, which resets all reader state, so
		// one instance per role serves every object.
		private IDwgStreamReader _cachedObjectReader;
		private IDwgStreamReader _cachedHandlesReader;
		private IDwgStreamReader _cachedTextReader;

		private IDwgStreamReader sectionReader(ref IDwgStreamReader cached)
		{
			return cached ??= DwgStreamReaderBase.GetStreamHandler(
				this._version, HugeMemoryStream.Clone(this._memoryStream), this._reader.Encoding);
		}
	}
}
