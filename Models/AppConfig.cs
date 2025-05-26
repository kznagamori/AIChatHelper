// AppConfig.cs
using System.Collections.Generic;
using Tomlet.Attributes;
namespace AIChatHelper.Models;


[TomlDoNotInlineObjectAttribute]
public class AppConfig
{
	public List<ChatSite> ChatSites { get; set; } = new List<ChatSite>();

	// Config テーブル全体をマッピング
	public Config Config { get; set; } = new Config();
}

public class Config
{
	public List<string> InputSelectors { get; set; } = new List<string>();
	// エディタの確認ダイアログに関する設定
	public EditorSettings EditorSettings { get; set; } = new EditorSettings();
}

// エディタ設定クラスを新規作成
public class EditorSettings
{
	// テンプレート選択時に確認ダイアログを表示するか
	public bool ConfirmTemplateOverwrite { get; set; } = false;

	// 履歴選択時に確認ダイアログを表示するか
	public bool ConfirmHistoryOverwrite { get; set; } = false;

	// TextEditor にテンプレートテキストを挿入するかどうか
	public bool InsertTemplateTextOnClear { get; set; } = false;

	// 挿入するテンプレートテキスト
	public string TemplateTextForEditor { get; set; } = string.Empty;
}