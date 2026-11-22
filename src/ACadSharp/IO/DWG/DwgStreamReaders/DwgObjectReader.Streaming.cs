using ACadSharp.IO.Templates;

namespace ACadSharp.IO.DWG
{
	internal partial class DwgObjectReader
	{
		// See DwgReaderConfiguration.ModelSpaceEntityDecoded. Model space entities are the ones
		// with entity mode 2: they carry no owner handle, the mode itself says where they live.
		private void notifyDecoded(CadTemplate template)
		{
			var callback = this._builder.Configuration.ModelSpaceEntityDecoded;
			if (callback == null)
				return;

			if (template is CadEntityTemplate entityTemplate && entityTemplate.EntityMode == 2)
			{
				try
				{
					callback(entityTemplate.CadObject);
				}
				catch
				{
					// A preview consumer must never be able to break the read.
				}
			}
		}
	}
}
