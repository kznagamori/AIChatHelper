using System.Collections.ObjectModel;

namespace AIChatHelper.Models;

public sealed class TemplateTreeNode
{
	public string Name { get; init; } = string.Empty;
	public string FullPath { get; init; } = string.Empty;
	public bool IsDirectory { get; init; }
	public string? ErrorMessage { get; set; }
	public ObservableCollection<TemplateTreeNode> Children { get; } = new();
}
