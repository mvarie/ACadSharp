using ACadSharp.Classes;
using ACadSharp.IO.DWG.DwgStreamReaders;
using ACadSharp.IO.Templates;
using ACadSharp.Tables.Collections;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ACadSharp.IO.DWG
{
	/// <summary>
	/// Parallel object decode (<see cref="DwgReaderConfiguration.ParallelObjectDecode"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// The serial <see cref="Read"/> is a breadth-first walk: it starts from the header handles,
	/// decodes one object, and queues every handle that object references. Decoding an object
	/// does not depend on any other object, only on its own bytes, so the walk can be split in two:
	/// </para>
	/// <list type="number">
	/// <item>Decode: every object in the handle map is decoded concurrently by workers, each with
	/// its own readers over the shared section buffer and its own throwaway builder. A worker records,
	/// per object, the template and the handles it referenced, in reference order.</item>
	/// <item>Replay: the breadth-first walk runs again over the recorded references, applying the
	/// exact queue rules of the serial reader, and hands templates to the real builder in the order
	/// the serial reader would have. The builder state is therefore identical to a serial read.</item>
	/// </list>
	/// <para>
	/// The builder side effects of a decode (table assignments, block record templates, model/paper
	/// space entity lists) are re-derived from the template at replay, in replay order, rather than
	/// taken from the worker builders. Objects the walk never reaches are decoded and discarded,
	/// exactly as the serial reader never reads them.
	/// </para>
	/// </remarks>
	internal partial class DwgObjectReader
	{
		// Non-null while decoding in worker mode: the references of the current object.
		private List<ulong> _edges;

		private sealed class Decoded
		{
			public CadTemplate Template;
			public ulong[] Edges;
		}

		private DwgObjectReader(
			ACadVersion version,
			DwgDocumentBuilder builder,
			IDwgStreamReader reader,
			Dictionary<ulong, long> sharedMap,
			Dictionary<short, DxfClass> sharedClasses,
			MemoryStream memoryStream) : base(version)
		{
			this._builder = builder;
			this._reader = reader;
			this._handles = new Queue<ulong>();
			this._map = sharedMap;
			this._classes = sharedClasses;
			this._memoryStream = memoryStream;
			this._crcReader = DwgStreamReaderBase.GetStreamHandler(this._version, this._memoryStream);
		}

		/// <summary>
		/// Same result as <see cref="Read"/>, decoded on <paramref name="workerCount"/> threads.
		/// </summary>
		public void ReadParallel(int workerCount)
		{
			// Sequential offsets per worker keeps each worker's memory access streaming.
			var handles = this._map
				.OrderBy(kv => kv.Value)
				.Select(kv => kv.Key)
				.ToArray();

			workerCount = Math.Max(1, Math.Min(workerCount, handles.Length / 512 + 1));
			var results = new Decoded[handles.Length];
			var notifyGate = new object();

			Parallel.For(0, workerCount, new ParallelOptions { MaxDegreeOfParallelism = workerCount }, w =>
			{
				int start = (int)((long)handles.Length * w / workerCount);
				int end = (int)((long)handles.Length * (w + 1) / workerCount);

				var workerBuilder = new DwgDocumentBuilder(this._version, this._builder.DocumentToBuild, this._builder.Configuration);
				workerBuilder.OnNotification += (_, e) =>
				{
					lock (notifyGate)
					{
						this._builder.Notify(e.Message, e.NotificationType, e.Exception);
					}
				};

				var stream = cloneExposable(this._memoryStream);
				var worker = new DwgObjectReader(
					this._version, workerBuilder, this._reader, this._map, this._classes, stream);

				for (int i = start; i < end; i++)
				{
					ulong handle = handles[i];
					worker._edges = new List<ulong>();
					CadTemplate template = null;

					try
					{
						ObjectType type = worker.getEntityType(this._map[handle]);
						template = worker.readObject(type);
					}
					catch (Exception ex)
					{
						if (!this._builder.Configuration.Failsafe)
							throw;

						lock (notifyGate)
						{
							this._builder.Notify($"Could not read object with handle: {handle}", NotificationType.Error, ex);
						}
					}

					results[i] = new Decoded { Template = template, Edges = worker._edges.ToArray() };
				}

				worker._edges = null;
			});

			var byHandle = new Dictionary<ulong, Decoded>(handles.Length);
			for (int i = 0; i < handles.Length; i++)
			{
				byHandle[handles[i]] = results[i];
			}

			this.replay(byHandle);
		}

		// HugeMemoryStream.Clone wraps a plain MemoryStream's buffer without making it visible again,
		// and getEntityType clones the stream it is given, so the copy must expose its buffer.
		private static MemoryStream cloneExposable(MemoryStream source)
		{
			if (source is HugeMemoryStream huge)
				return huge.Clone();

			byte[] buffer = source.GetBuffer();
			return new MemoryStream(buffer, 0, buffer.Length, false, true);
		}

		private void replay(Dictionary<ulong, Decoded> decoded)
		{
			var visited = new HashSet<ulong>();

			while (this._handles.Count > 0)
			{
				ulong handle = this._handles.Dequeue();

				if (!decoded.TryGetValue(handle, out Decoded d) || !visited.Add(handle))
				{
					continue;
				}

				foreach (ulong edge in d.Edges)
				{
					if (!visited.Contains(edge))
					{
						this._handles.Enqueue(edge);
					}
				}

				if (d.Template == null)
				{
					continue;
				}

				this.applyBuilderSideEffects(d.Template);
				this._builder.AddTemplate(d.Template);
			}
		}

		// The builder writes the serial reader performs inside readObject, re-derived from the
		// template. Keep in step with the _builder assignments in DwgObjectReader.cs.
		private void applyBuilderSideEffects(CadTemplate template)
		{
			switch (template)
			{
				case CadBlockRecordTemplate blockRecord:
					this._builder.BlockRecordTemplates.Add(blockRecord);
					return;
				case CadEntityTemplate entity:
					if (entity.EntityMode == 1)
						this._builder.PaperSpaceEntities.Add(entity.CadObject);
					else if (entity.EntityMode == 2)
						this._builder.ModelSpaceEntities.Add(entity.CadObject);
					return;
			}

			switch (template.CadObject)
			{
				case BlockRecordsTable t: this._builder.BlockRecords = t; break;
				case LayersTable t: this._builder.Layers = t; break;
				case TextStylesTable t: this._builder.TextStyles = t; break;
				case LineTypesTable t: this._builder.LineTypesTable = t; break;
				case ViewsTable t: this._builder.Views = t; break;
				case UCSTable t: this._builder.UCSs = t; break;
				case VPortsTable t: this._builder.VPorts = t; break;
				case AppIdsTable t: this._builder.AppIds = t; break;
				case DimensionStylesTable t: this._builder.DimensionStyles = t; break;
			}
		}
	}
}
