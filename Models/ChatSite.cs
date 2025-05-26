// ChatSites.cs
using Tomlet.Attributes;

namespace AIChatHelper.Models;

[TomlDoNotInlineObjectAttribute]
public class ChatSite
{
	public string Name { get; set; }
	public string Url { get; set; }

	public ChatSite(string name, string url)
	{
		Name = name ?? throw new ArgumentNullException(nameof(name));
		Url = url ?? throw new ArgumentNullException(nameof(url));
	}

	// パラメータなしのコンストラクタ（シリアライズ用）
	public ChatSite()
	{
		Name = string.Empty;
		Url = string.Empty;
	}
}
