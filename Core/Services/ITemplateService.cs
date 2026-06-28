// Core/Services/ITemplateService.cs
using System.Collections.Generic;
using AIChatHelper.Models;

namespace AIChatHelper.Core.Services
{
	public interface ITemplateService
	{
		IEnumerable<string> GetTemplateFileNames();
		void RebuildTemplateMap();
		string LoadTemplate(string fileName);
		IReadOnlyList<TemplateTreeNode> GetTemplateTree(IntPtr ownerWindowHandle = default);
		string LoadTemplateByPath(string fullPath);
		string GetResolvedTemplateDirectory();
	}
}
