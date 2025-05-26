// Core/Services/ITemplateService.cs
using System.Collections.Generic;

namespace AIChatHelper.Core.Services
{
	public interface ITemplateService
	{
		IEnumerable<string> GetTemplateFileNames();
		void RebuildTemplateMap();
		string LoadTemplate(string fileName);
	}
}
