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
	// テンプレート表示に関する設定
	public TemplateSettings TemplateSettings { get; set; } = new TemplateSettings();
	// 左ペインのチャットタブ復元に関する設定
	public TabRestoreSettings TabRestoreSettings { get; set; } = new TabRestoreSettings();
	// チャット入力後に実行する処理の設定
	public ExecuteAfterSendSettings ExecuteAfterSendSettings { get; set; } = new ExecuteAfterSendSettings();
	// アプリの表示状態に関する設定
	public UiStateSettings UiState { get; set; } = new UiStateSettings();
}

public class TemplateSettings
{
	// テンプレートツリーで参照するディレクトリ
	public string TemplateDirectory { get; set; } = "template";
}

/// <summary>
/// 左ペインのチャットタブを次回起動時に復元する方法を表します。
/// </summary>
public class TabRestoreSettings
{
	/// <summary>
	/// 登録サイトの許可範囲内にあるタブ URL を保存して復元するかどうかを取得または設定します。
	/// </summary>
	public bool SaveAndRestoreTabUrls { get; set; } = false;

	/// <summary>
	/// 保存済みタブを使わず、登録済みチャットサイトから初期タブを作成するかどうかを取得または設定します。
	/// </summary>
	public bool AlwaysRestoreInitialTabs { get; set; } = false;
}

public class ExecuteAfterSendSettings
{
	public bool DefaultEnabled { get; set; } = false;
	public int ExecutionTimeoutMs { get; set; } = 3000;
	public int PostInputDelayMs { get; set; } = 100;
	public int RetryIntervalMs { get; set; } = 100;
	public bool EnableDomAnalysisLog { get; set; } = true;
	public string UnsupportedServiceBehavior { get; set; } = "InputOnly";
	public List<ServiceExecutorSettings> ServiceExecutors { get; set; } = new List<ServiceExecutorSettings>();
}

public class ServiceExecutorSettings
{
	public string ServiceName { get; set; } = string.Empty;
	public List<string> UrlPatterns { get; set; } = new List<string>();
	public List<string> SubmitButtonSelectors { get; set; } = new List<string>();
	public string KeyboardFallback { get; set; } = "None";
}

public class UiStateSettings
{
	public int ActiveLeftTabIndex { get; set; } = 0;
	public string PaneDisplayMode { get; set; } = "TwoPane";
	public string RightPaneSelectedTab { get; set; } = "History";
	public bool? IsDarkTheme { get; set; }
	public bool? ExecuteAfterSend { get; set; }
	public double? WindowWidth { get; set; }
	public double? WindowHeight { get; set; }
	public double? WindowLeft { get; set; }
	public double? WindowTop { get; set; }
	public bool RestoreWindowPosition { get; set; } = false;
	public List<LeftPaneTabState> LeftPaneTabs { get; set; } = new List<LeftPaneTabState>();
}

[TomlDoNotInlineObjectAttribute]
public class LeftPaneTabState
{
	/// <summary>
	/// タブの登録元となったチャットサイト名を取得または設定します。
	/// </summary>
	public string SiteName { get; set; } = string.Empty;

	/// <summary>
	/// 登録元のチャットサイト URL を取得または設定します。
	/// </summary>
	public string Url { get; set; } = string.Empty;

	/// <summary>
	/// タブ見出しに表示する名前を取得または設定します。
	/// </summary>
	public string DisplayName { get; set; } = string.Empty;

	/// <summary>
	/// WebView2 が最後に表示していた、検証済みの URL を取得または設定します。
	/// </summary>
	public string CurrentUrl { get; set; } = string.Empty;
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
