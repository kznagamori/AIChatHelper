// SettingsService.cs
using System;
using System.IO;
using System.Text;
using Tomlet;
using AIChatHelper.Core.Helper;
using AIChatHelper.Models;

namespace AIChatHelper.Core.Services;

public class SettingsService : ISettingsService
{
	private static readonly HashSet<string> UnsupportedServiceBehaviorValues = new(StringComparer.OrdinalIgnoreCase)
	{
		"InputOnly",
		"ShowWarning"
	};

	private static readonly HashSet<string> KeyboardFallbackValues = new(StringComparer.OrdinalIgnoreCase)
	{
		"None",
		"Enter",
		"CtrlEnter"
	};

	private static readonly HashSet<string> PaneDisplayModeValues = new(StringComparer.OrdinalIgnoreCase)
	{
		"TwoPane",
		"LeftPane",
		"RightPane"
	};

	private readonly string _filePath;
	public string SettingsFilePath => _filePath;

	public SettingsService()
	{
		var exeDir = AppDomain.CurrentDomain.BaseDirectory;
		_filePath = Path.Combine(exeDir, "settings.toml");
	}

	public AppConfig Load()
	{
		return Validate(ValidateRaw(ReadRaw()));
	}

	public AppConfig Validate(AppConfig config)
	{
		ArgumentNullException.ThrowIfNull(config);

		config.ChatSites ??= new List<ChatSite>();
		config.Config ??= new Config();
		config.Config.InputSelectors ??= new List<string>();
		config.Config.EditorSettings ??= new EditorSettings();
		config.Config.TemplateSettings ??= new TemplateSettings();
		config.Config.TabRestoreSettings ??= new TabRestoreSettings();
		config.Config.ExecuteAfterSendSettings ??= new ExecuteAfterSendSettings();
		config.Config.ExecuteAfterSendSettings.ServiceExecutors ??= new List<ServiceExecutorSettings>();
		config.Config.UiState ??= new UiStateSettings();
		config.Config.UiState.LeftPaneTabs ??= new List<LeftPaneTabState>();

		foreach (var chatSite in config.ChatSites)
		{
			chatSite.Name = (chatSite.Name ?? string.Empty).Trim();
			chatSite.Url = (chatSite.Url ?? string.Empty).Trim();

			if (string.IsNullOrWhiteSpace(chatSite.Name))
			{
				throw new InvalidOperationException("ChatSites の Name は空にできません。");
			}

			if (!Uri.TryCreate(chatSite.Url, UriKind.Absolute, out _))
			{
				throw new InvalidOperationException($"ChatSites の Url が絶対 URL ではありません: {chatSite.Name}");
			}
		}

		config.Config.InputSelectors = ValidateInputSelectors(config.Config.InputSelectors);

		var editorSettings = config.Config.EditorSettings;
		editorSettings.TemplateTextForEditor ??= string.Empty;

		var templateSettings = config.Config.TemplateSettings;
		templateSettings.TemplateDirectory = (templateSettings.TemplateDirectory ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(templateSettings.TemplateDirectory))
		{
			throw new InvalidOperationException("TemplateDirectory は空にできません。");
		}

		var executeSettings = config.Config.ExecuteAfterSendSettings;
		if (executeSettings.ExecutionTimeoutMs <= 0)
		{
			executeSettings.ExecutionTimeoutMs = 3000;
		}

		if (executeSettings.PostInputDelayMs < 0)
		{
			executeSettings.PostInputDelayMs = 0;
		}

		if (executeSettings.RetryIntervalMs <= 0)
		{
			executeSettings.RetryIntervalMs = 100;
		}

		executeSettings.UnsupportedServiceBehavior =
			NormalizeChoice(executeSettings.UnsupportedServiceBehavior, UnsupportedServiceBehaviorValues, "InputOnly");

		foreach (var serviceExecutor in executeSettings.ServiceExecutors)
		{
			serviceExecutor.ServiceName = (serviceExecutor.ServiceName ?? string.Empty).Trim();
			if (string.IsNullOrWhiteSpace(serviceExecutor.ServiceName))
			{
				throw new InvalidOperationException("ServiceExecutors の ServiceName は空にできません。");
			}

			serviceExecutor.UrlPatterns = ValidateStringList(
				serviceExecutor.UrlPatterns,
				$"{serviceExecutor.ServiceName} の UrlPatterns",
				requireAtLeastOne: true);

			serviceExecutor.SubmitButtonSelectors = ValidateStringList(
				serviceExecutor.SubmitButtonSelectors,
				$"{serviceExecutor.ServiceName} の SubmitButtonSelectors",
				requireAtLeastOne: true);

			serviceExecutor.KeyboardFallback =
				NormalizeChoice(serviceExecutor.KeyboardFallback, KeyboardFallbackValues, "None");
		}

		var uiState = config.Config.UiState;
		var tabRestoreSettings = config.Config.TabRestoreSettings;
		uiState.ActiveLeftTabIndex = Math.Max(0, uiState.ActiveLeftTabIndex);
		uiState.PaneDisplayMode = NormalizePaneDisplayMode(uiState.PaneDisplayMode);
		uiState.RightPaneSelectedTab = NormalizeRightPaneSelectedTab(uiState.RightPaneSelectedTab);
		uiState.WindowWidth = NormalizePositiveDouble(uiState.WindowWidth);
		uiState.WindowHeight = NormalizePositiveDouble(uiState.WindowHeight);
		uiState.WindowLeft = NormalizeFiniteDouble(uiState.WindowLeft);
		uiState.WindowTop = NormalizeFiniteDouble(uiState.WindowTop);
		uiState.LeftPaneTabs = uiState.LeftPaneTabs
			.Where(tab => tab != null)
			.Select(tab => new LeftPaneTabState
			{
				SiteName = (tab.SiteName ?? string.Empty).Trim(),
				Url = (tab.Url ?? string.Empty).Trim(),
				DisplayName = (tab.DisplayName ?? string.Empty).Trim(),
				CurrentUrl = (tab.CurrentUrl ?? string.Empty).Trim()
			})
			.Where(tab => !string.IsNullOrWhiteSpace(tab.Url))
			.ToList();

		foreach (var tab in uiState.LeftPaneTabs)
		{
			if (!tabRestoreSettings.SaveAndRestoreTabUrls || tabRestoreSettings.AlwaysRestoreInitialTabs)
			{
				tab.CurrentUrl = string.Empty;
				continue;
			}

			var site = ChatTabUrlPolicy.FindMatchingSite(tab, config.ChatSites);
			if (site == null || !ChatTabUrlPolicy.TryGetRestorableUri(site, tab.CurrentUrl, out var currentUri))
			{
				// 不正な URL だけを破棄し、他のタブ状態と設定の読み込みは継続する。
				tab.CurrentUrl = string.Empty;
				continue;
			}

			tab.CurrentUrl = currentUri.AbsoluteUri;
		}

		return config;
	}

	public void Save(AppConfig config)
	{
		var validatedConfig = Validate(config);
		var tomlText = BuildAppConfigToml(validatedConfig);
		ValidateRaw(tomlText);
		SaveTextAtomically(tomlText);
	}

	public void SaveUiState(UiStateSettings uiState)
	{
		ArgumentNullException.ThrowIfNull(uiState);

		var currentText = ReadRaw();
		var currentConfig = ValidateRaw(currentText);
		currentConfig.Config ??= new Config();
		currentConfig.Config.UiState = uiState;

		// UI 状態だけを保存する経路でも、設定全体と同じ URL ポリシーを適用する。
		var validatedUiState = Validate(currentConfig).Config.UiState;
		var updatedText = ReplaceUiStateSection(currentText, validatedUiState);
		ValidateRaw(updatedText);
		SaveTextAtomically(updatedText);
	}

	private string ReadRaw()
	{
		return File.ReadAllText(_filePath, Encoding.UTF8);
	}

	private AppConfig ValidateRaw(string tomlText)
	{
		return TomletMain.To<AppConfig>(tomlText);
	}

	private void SaveTextAtomically(string text)
	{
		var directory = Path.GetDirectoryName(_filePath);
		if (!string.IsNullOrWhiteSpace(directory))
		{
			Directory.CreateDirectory(directory);
		}

		var tempPath = _filePath + ".tmp";
		var backupPath = _filePath + ".bak";

		File.WriteAllText(tempPath, text, Encoding.UTF8);
		if (File.Exists(_filePath))
		{
			File.Copy(_filePath, backupPath, overwrite: true);
		}
		File.Move(tempPath, _filePath, overwrite: true);
	}

	private static List<string> ValidateStringList(IEnumerable<string>? values, string label, bool requireAtLeastOne)
	{
		var result = new List<string>();
		var index = 0;

		foreach (var value in values ?? Enumerable.Empty<string>())
		{
			index++;
			if (string.IsNullOrWhiteSpace(value))
			{
				throw new InvalidOperationException($"{label} の {index} 件目が空です。");
			}

			result.Add(value.Trim());
		}

		if (requireAtLeastOne && result.Count == 0)
		{
			throw new InvalidOperationException($"{label} は 1 件以上必要です。");
		}

		return result;
	}

	private static List<string> ValidateInputSelectors(IEnumerable<string>? values)
	{
		var result = new List<string>();
		var index = 0;

		foreach (var value in values ?? Enumerable.Empty<string>())
		{
			index++;
			if (value == null || value.Length == 0)
			{
				continue;
			}

			if (string.IsNullOrWhiteSpace(value))
			{
				throw new InvalidOperationException($"InputSelectors の {index} 件目が空白のみです。");
			}

			result.Add(value.Trim());
		}

		return result;
	}

	private static string NormalizeChoice(string? value, HashSet<string> candidates, string fallback)
	{
		var candidate = (value ?? string.Empty).Trim();
		return candidates.Contains(candidate)
			? candidates.First(item => item.Equals(candidate, StringComparison.OrdinalIgnoreCase))
			: fallback;
	}

	private static double? NormalizePositiveDouble(double? value)
	{
		if (!value.HasValue || !IsFinite(value.Value) || value.Value <= 0)
		{
			return null;
		}

		return value.Value;
	}

	private static double? NormalizeFiniteDouble(double? value)
	{
		if (!value.HasValue || !IsFinite(value.Value))
		{
			return null;
		}

		return value.Value;
	}

	private static string BuildAppConfigToml(AppConfig config)
	{
		var builder = new StringBuilder();

		foreach (var chatSite in config.ChatSites)
		{
			builder.AppendLine("[[ChatSites]]");
			builder.AppendLine($"Name = \"{EscapeTomlString(chatSite.Name)}\"");
			builder.AppendLine($"Url = \"{EscapeTomlString(chatSite.Url)}\"");
			builder.AppendLine();
		}

		builder.AppendLine("[Config]");
		builder.AppendLine("# テキスト入力欄の検索条件（優先度が上の順に並べる）");
		AppendStringArray(builder, "InputSelectors", config.Config.InputSelectors);
		builder.AppendLine();

		builder.AppendLine("[Config.EditorSettings]");
		builder.AppendLine($"ConfirmTemplateOverwrite = {ToTomlBoolean(config.Config.EditorSettings.ConfirmTemplateOverwrite)}");
		builder.AppendLine($"ConfirmHistoryOverwrite = {ToTomlBoolean(config.Config.EditorSettings.ConfirmHistoryOverwrite)}");
		builder.AppendLine("# TextEditor がクリア状態になった時にテンプレートテキストを挿入するかどうか");
		builder.AppendLine($"InsertTemplateTextOnClear = {ToTomlBoolean(config.Config.EditorSettings.InsertTemplateTextOnClear)}");
		builder.AppendLine("# 挿入するテンプレートテキスト");
		builder.AppendLine("TemplateTextForEditor = \"\"\"");
		builder.AppendLine(EscapeTomlMultilineString(config.Config.EditorSettings.TemplateTextForEditor));
		builder.AppendLine("\"\"\"");
		builder.AppendLine();

		builder.AppendLine("[Config.TemplateSettings]");
		builder.AppendLine("# 相対パスの場合は実行フォルダー基準。絶対パス、ドライブパス、UNC パスも指定可能。");
		builder.AppendLine($"TemplateDirectory = \"{EscapeTomlString(config.Config.TemplateSettings.TemplateDirectory)}\"");
		builder.AppendLine();

		builder.AppendLine("[Config.TabRestoreSettings]");
		builder.AppendLine("# true: 許可された登録サイト URL をタブごとに保存し、次回起動時に復元する");
		builder.AppendLine($"SaveAndRestoreTabUrls = {ToTomlBoolean(config.Config.TabRestoreSettings.SaveAndRestoreTabUrls)}");
		builder.AppendLine("# true: 保存済みタブより優先して、ChatSites を初期 URL で各 1 タブ開く");
		builder.AppendLine($"AlwaysRestoreInitialTabs = {ToTomlBoolean(config.Config.TabRestoreSettings.AlwaysRestoreInitialTabs)}");
		builder.AppendLine();

		builder.AppendLine("[Config.ExecuteAfterSendSettings]");
		builder.AppendLine("# 実行ボタン探索の最大待ち時間");
		builder.AppendLine($"ExecutionTimeoutMs = {config.Config.ExecuteAfterSendSettings.ExecutionTimeoutMs}");
		builder.AppendLine("# 入力反映後、実行ボタン探索を始めるまでの待ち時間");
		builder.AppendLine($"PostInputDelayMs = {config.Config.ExecuteAfterSendSettings.PostInputDelayMs}");
		builder.AppendLine("# 実行ボタン探索のリトライ間隔");
		builder.AppendLine($"RetryIntervalMs = {config.Config.ExecuteAfterSendSettings.RetryIntervalMs}");
		builder.AppendLine("# DOM解析ログをDebug出力するかどうか。入力本文は出力しない。");
		builder.AppendLine($"EnableDomAnalysisLog = {ToTomlBoolean(config.Config.ExecuteAfterSendSettings.EnableDomAnalysisLog)}");
		builder.AppendLine("# 未対応サービス時の動作: InputOnly または ShowWarning");
		builder.AppendLine($"UnsupportedServiceBehavior = \"{EscapeTomlString(config.Config.ExecuteAfterSendSettings.UnsupportedServiceBehavior)}\"");

		foreach (var serviceExecutor in config.Config.ExecuteAfterSendSettings.ServiceExecutors)
		{
			builder.AppendLine();
			builder.AppendLine("[[Config.ExecuteAfterSendSettings.ServiceExecutors]]");
			builder.AppendLine($"ServiceName = \"{EscapeTomlString(serviceExecutor.ServiceName)}\"");
			builder.AppendLine($"UrlPatterns = {BuildInlineStringArray(serviceExecutor.UrlPatterns)}");
			AppendStringArray(builder, "SubmitButtonSelectors", serviceExecutor.SubmitButtonSelectors);
			builder.AppendLine($"KeyboardFallback = \"{EscapeTomlString(serviceExecutor.KeyboardFallback)}\"");
		}

		builder.AppendLine();
		builder.Append(BuildUiStateToml(config.Config.UiState));

		return builder.ToString();
	}

	private static void AppendStringArray(StringBuilder builder, string key, IEnumerable<string> values)
	{
		builder.AppendLine($"{key} = [");
		foreach (var value in values)
		{
			builder.AppendLine($"\t\"{EscapeTomlString(value)}\",");
		}
		builder.AppendLine("]");
	}

	private static string BuildInlineStringArray(IEnumerable<string> values)
	{
		return "[" + string.Join(", ", values.Select(value => $"\"{EscapeTomlString(value)}\"")) + "]";
	}

	private static string ReplaceUiStateSection(string currentText, UiStateSettings uiState)
	{
		var normalizedText = currentText.Replace("\r\n", "\n").Replace('\r', '\n');
		var lines = normalizedText.Split('\n');
		var builder = new StringBuilder();
		var skippingUiState = false;

		foreach (var line in lines)
		{
			var trimmedLine = line.Trim();
			if (IsTomlHeader(trimmedLine))
			{
				var headerName = GetTomlHeaderName(trimmedLine);
				skippingUiState = IsUiStateHeader(headerName);
			}

			if (!skippingUiState)
			{
				builder.AppendLine(line);
			}
		}

		var result = builder.ToString().TrimEnd();
		if (result.Length > 0)
		{
			result += Environment.NewLine + Environment.NewLine;
		}
		result += BuildUiStateToml(uiState);

		return result;
	}

	private static bool IsTomlHeader(string line)
	{
		return line.StartsWith("[", StringComparison.Ordinal)
			&& line.EndsWith("]", StringComparison.Ordinal);
	}

	private static string GetTomlHeaderName(string headerLine)
	{
		return headerLine.Trim('[', ']').Trim();
	}

	private static bool IsUiStateHeader(string headerName)
	{
		return headerName.Equals("Config.UiState", StringComparison.OrdinalIgnoreCase)
			|| headerName.StartsWith("Config.UiState.", StringComparison.OrdinalIgnoreCase);
	}

	private static string BuildUiStateToml(UiStateSettings uiState)
	{
		var builder = new StringBuilder();
		builder.AppendLine("[Config.UiState]");
		builder.AppendLine("# アプリが最後に保存した左ペインのアクティブタブ位置。範囲外の場合は 0 に丸める。");
		builder.AppendLine($"ActiveLeftTabIndex = {Math.Max(0, uiState.ActiveLeftTabIndex)}");
		builder.AppendLine();
		builder.AppendLine("# アプリ全体のペイン表示。TwoPane, LeftPane, RightPane のいずれか。");
		builder.AppendLine($"PaneDisplayMode = \"{EscapeTomlString(NormalizePaneDisplayMode(uiState.PaneDisplayMode))}\"");
		builder.AppendLine();
		builder.AppendLine("# 右ペインの選択タブ。History または Template。");
		builder.AppendLine($"RightPaneSelectedTab = \"{EscapeTomlString(NormalizeRightPaneSelectedTab(uiState.RightPaneSelectedTab))}\"");

		if (uiState.WindowWidth.HasValue)
		{
			builder.AppendLine();
			builder.AppendLine("# アプリケーションウィンドウの幅");
			builder.AppendLine($"WindowWidth = {FormatTomlDouble(uiState.WindowWidth.Value)}");
		}

		if (uiState.WindowHeight.HasValue)
		{
			builder.AppendLine();
			builder.AppendLine("# アプリケーションウィンドウの高さ");
			builder.AppendLine($"WindowHeight = {FormatTomlDouble(uiState.WindowHeight.Value)}");
		}

		if (uiState.WindowLeft.HasValue)
		{
			builder.AppendLine();
			builder.AppendLine("# アプリケーションウィンドウの左位置。RestoreWindowPosition が true の場合だけ復元に使う。");
			builder.AppendLine($"WindowLeft = {FormatTomlDouble(uiState.WindowLeft.Value)}");
		}

		if (uiState.WindowTop.HasValue)
		{
			builder.AppendLine();
			builder.AppendLine("# アプリケーションウィンドウの上位置。RestoreWindowPosition が true の場合だけ復元に使う。");
			builder.AppendLine($"WindowTop = {FormatTomlDouble(uiState.WindowTop.Value)}");
		}

		builder.AppendLine();
		builder.AppendLine("# true: 保存した WindowLeft / WindowTop を起動時に復元する");
		builder.AppendLine($"RestoreWindowPosition = {ToTomlBoolean(uiState.RestoreWindowPosition)}");

		builder.AppendLine();
		builder.AppendLine("# true: ダーク固定、false: ライト固定。キー省略時は Windows の設定に合わせる。");
		if (uiState.IsDarkTheme.HasValue)
		{
			builder.AppendLine($"IsDarkTheme = {ToTomlBoolean(uiState.IsDarkTheme.Value)}");
		}

		if (uiState.ExecuteAfterSend.HasValue)
		{
			builder.AppendLine();
			builder.AppendLine("# 右ペインの「送信後実行」チェック状態");
			builder.AppendLine($"ExecuteAfterSend = {ToTomlBoolean(uiState.ExecuteAfterSend.Value)}");
		}

		foreach (var tab in uiState.LeftPaneTabs ?? new List<LeftPaneTabState>())
		{
			if (string.IsNullOrWhiteSpace(tab.Url))
			{
				continue;
			}

			builder.AppendLine();
			builder.AppendLine("[[Config.UiState.LeftPaneTabs]]");
			builder.AppendLine($"SiteName = \"{EscapeTomlString(tab.SiteName)}\"");
			builder.AppendLine($"Url = \"{EscapeTomlString(tab.Url)}\"");
			builder.AppendLine($"DisplayName = \"{EscapeTomlString(tab.DisplayName)}\"");
			if (!string.IsNullOrWhiteSpace(tab.CurrentUrl))
			{
				builder.AppendLine($"CurrentUrl = \"{EscapeTomlString(tab.CurrentUrl)}\"");
			}
		}

		return builder.ToString();
	}

	private static string NormalizeRightPaneSelectedTab(string? value)
	{
		return string.Equals(value, "Template", StringComparison.OrdinalIgnoreCase)
			? "Template"
			: "History";
	}

	private static string NormalizePaneDisplayMode(string? value)
	{
		return NormalizeChoice(value, PaneDisplayModeValues, "TwoPane");
	}

	private static string ToTomlBoolean(bool value)
	{
		return value.ToString().ToLowerInvariant();
	}

	private static string FormatTomlDouble(double value)
	{
		return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
	}

	private static bool IsFinite(double value)
	{
		return !double.IsNaN(value) && !double.IsInfinity(value);
	}

	private static string EscapeTomlString(string? value)
	{
		return (value ?? string.Empty)
			.Replace("\\", "\\\\", StringComparison.Ordinal)
			.Replace("\"", "\\\"", StringComparison.Ordinal)
			.Replace("\t", "\\t", StringComparison.Ordinal)
			.Replace("\r", "\\r", StringComparison.Ordinal)
			.Replace("\n", "\\n", StringComparison.Ordinal);
	}

	private static string EscapeTomlMultilineString(string? value)
	{
		return (value ?? string.Empty)
			.Replace("\\", "\\\\", StringComparison.Ordinal)
			.Replace("\"", "\\\"", StringComparison.Ordinal)
			.Replace("\r\n", "\n", StringComparison.Ordinal)
			.Replace("\r", "\n", StringComparison.Ordinal);
	}
}
