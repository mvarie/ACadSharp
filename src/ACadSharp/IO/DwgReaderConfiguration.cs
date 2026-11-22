using System;

namespace ACadSharp.IO;

/// <summary>
/// Configuration for reading DWG files.
/// </summary>
public class DwgReaderConfiguration : CadReaderConfiguration
{
	/// <summary>
	/// Use the Standard Cycling Redundancy Check to verify the integrity of the file, default value is set to false.
	/// </summary>
	/// <remarks>
	/// DWG file format uses a modification of a standard Cyclic Redundancy Check as an error detecting mechanism, 
	/// if this flag is enabled the reader will perform this verification to detect any possible error, but it will greatly increase the reading time.
	/// </remarks>
	[Obsolete("This property is obsolete and will be removed in future versions.")]
	public bool CrcCheck { get; set; } = false;

	/// <summary>
	/// If set to false the reader will skip the summary info section.
	/// </summary>
	/// <value>
	/// default: true
	/// </value>
	public bool ReadSummaryInfo { get; set; } = true;

	/// <summary>
	/// Number of threads used to decode the objects section. 1 (the default) is the serial reader;
	/// above 1 the objects are decoded concurrently and replayed in the serial reader's order, so the
	/// resulting document is the same. 0 means one thread per processor.
	/// </summary>
	public int ParallelObjectDecode { get; set; } = 1;

	/// <summary>
	/// Called with each model space entity as soon as its own data has been decoded, before the
	/// document is built. Lets a viewer draw a draft of the drawing while the read is still
	/// running.
	/// </summary>
	/// <remarks>
	/// At that point the entity's geometry and its own <see cref="Entities.Entity.Color"/> are
	/// valid. Its table references are not resolved yet: <see cref="Entities.Entity.Layer"/> and
	/// <see cref="Entities.Entity.LineType"/> are placeholders, and the entity has no document.
	/// Inserts are reported, but their blocks may not be read yet. With
	/// <see cref="ParallelObjectDecode"/> above 1 this is called concurrently from several
	/// threads, in no particular order. The consumer must only read the entity, never keep a
	/// mutable hold on it, and return quickly; exceptions it throws are swallowed.
	/// </remarks>
	public System.Action<Entities.Entity> ModelSpaceEntityDecoded { get; set; }

	/// <summary>
	/// Called with the header as soon as it is read, before any object, so a draft view can be
	/// framed (<c>$EXTMIN</c>/<c>$EXTMAX</c>) ahead of <see cref="ModelSpaceEntityDecoded"/>.
	/// </summary>
	public System.Action<Header.CadHeader> HeaderRead { get; set; }
}
