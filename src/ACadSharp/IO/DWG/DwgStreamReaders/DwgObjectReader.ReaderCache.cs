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

		// The merged reader only routes calls to its three readers, which are now the cached
		// instances above, so one merged reader per combination serves every object.
		private DwgMergedReader _cachedMerged;
		private IDwgStreamReader _mergedObject, _mergedText, _mergedHandles;

		private IDwgStreamReader mergedReader()
		{
			if (this._cachedMerged == null
				|| !ReferenceEquals(this._mergedObject, this._objectReader)
				|| !ReferenceEquals(this._mergedText, this._textReader)
				|| !ReferenceEquals(this._mergedHandles, this._handlesReader))
			{
				this._cachedMerged = new DwgMergedReader(this._objectReader, this._textReader, this._handlesReader);
				this._mergedObject = this._objectReader;
				this._mergedText = this._textReader;
				this._mergedHandles = this._handlesReader;
			}

			return this._cachedMerged;
		}

		private IDwgStreamReader sectionReader(ref IDwgStreamReader cached)
		{
			return cached ??= DwgStreamReaderBase.GetStreamHandler(
				this._version, HugeMemoryStream.Clone(this._memoryStream), this._reader.Encoding);
		}
	}
}
