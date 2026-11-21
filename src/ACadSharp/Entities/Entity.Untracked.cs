using ACadSharp.Objects;
using ACadSharp.Tables;

namespace ACadSharp.Entities;

public abstract partial class Entity
{
	/// <summary>
	/// <see cref="AssignDocument"/> for a document with <see cref="CadDocument.TrackReferences"/> off.
	/// </summary>
	/// <remarks>
	/// Same resolution as the tracked path (each entry is replaced by the document's entry of the same
	/// name, added if missing) without the reference registration, the per-call closures, or the
	/// throwaway <see cref="Layer.Default"/>/<see cref="LineType.ByLayer"/> instances a still-unset
	/// layer or linetype would allocate. Per entity this is the difference between a handful of
	/// dictionary lookups and ten heap allocations.
	/// </remarks>
	// The setters' path when there is no document or it does not track references: the same
	// result as updateTableEntry/updateCollectionEntry (null collection -> the entry itself,
	// otherwise the collection's entry of that name), minus the closure allocated per call.
	private static T resolveUntracked<T>(T entry, Tables.Collections.Table<T> table)
		where T : TableEntry
	{
		return table == null || entry == null ? entry : table.TryAdd(entry);
	}

	private static T resolveUntracked<T>(T entry, Objects.Collections.ObjectDictionaryCollection<T> collection)
		where T : NonGraphicalObject
	{
		return collection == null || entry == null ? entry : collection.TryAdd(entry);
	}

	private void assignDocumentUntracked(CadDocument doc)
	{
		if (this._bookColor != null && doc.Colors != null)
			this._bookColor = doc.Colors.TryAdd(this._bookColor);

		if (this._material != null && doc.Materials != null)
			this._material = doc.Materials.TryAdd(this._material);

		if (doc.Layers != null)
		{
			if (this._layer != null)
				this._layer = doc.Layers.TryAdd(this._layer);
			else if (!doc.Layers.TryGetValue(Layer.DefaultName, out this._layer))
				this._layer = doc.Layers.TryAdd(Layer.Default);
		}

		if (doc.LineTypes != null)
		{
			if (this._lineType != null)
				this._lineType = doc.LineTypes.TryAdd(this._lineType);
			else if (!doc.LineTypes.TryGetValue(LineType.ByLayerName, out this._lineType))
				this._lineType = doc.LineTypes.TryAdd(LineType.ByLayer);
		}
	}
}
