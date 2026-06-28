// ViewModels/MainWindowViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using AIChatHelper.Core.Services;
using AIChatHelper.Models;
using System.IO;
using System.Diagnostics;
using Microsoft.Web.WebView2.Wpf;
using System.Threading.Tasks;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MaterialDesignThemes.Wpf;

namespace AIChatHelper.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
	// TextBoxのテキスト
	[ObservableProperty]
	private string _editorText = string.Empty;

	// 履歴アイテムコレクション（DataGrid用）
	public ObservableCollection<HistoryItem> HistoryItems { get; } = new();

	// テンプレートツリー
	public ObservableCollection<TemplateTreeNode> TemplateTreeNodes { get; } = new();

	// 選択中の履歴アイテム
	[ObservableProperty]
	private HistoryItem? _selectedHistoryItem;

	// 選択中テンプレートノード
	[ObservableProperty]
	private TemplateTreeNode? _selectedTemplateNode;

	// テンプレートツリーの読み込みエラー
	[ObservableProperty]
	private string? _templateLoadErrorMessage;

	// テンプレートツリー読み込み中フラグ
	[ObservableProperty]
	private bool _isTemplateTreeLoading;

	[ObservableProperty]
	private string _rightPaneSelectedTab = "History";

	// 表示モード（0=両方表示, 1=左のみ, 2=右のみ）
	[ObservableProperty]
	private int _displayMode = 0;

	// 左ペインの幅
	[ObservableProperty]
	private GridLength _leftColumnWidth = new GridLength(1, GridUnitType.Star);

	// 右ペインの幅
	[ObservableProperty]
	private GridLength _rightColumnWidth = new GridLength(1, GridUnitType.Star);

	// スプリッタの幅
	[ObservableProperty]
	private GridLength _splitterWidth = new GridLength(5);

	// 左ペインの可視性
	[ObservableProperty]
	private Visibility _leftPaneVisibility = Visibility.Visible;

	// 右ペインの可視性
	[ObservableProperty]
	private Visibility _rightPaneVisibility = Visibility.Visible;

	// スプリッタの可視性
	[ObservableProperty]
	private Visibility _splitterVisibility = Visibility.Visible;

	[ObservableProperty]
	private bool isDarkTheme = true;

	private readonly Core.Factory.IWindowFactory? _windowFactory;
	private readonly IHistoryService _historyService;
	private readonly ITemplateService _templateService;
	private readonly AppConfig _config;

	// 確認ダイアログを表示するかどうかの設定を保持
	private bool _confirmTemplateOverwrite;
	private bool _confirmHistoryOverwrite;

	private bool _insertTemplateTextOnClear = true;
	private String _templateTextForEditor = String.Empty;

	public event EventHandler? UiStateChanged;

	// DI で IHistoryService を受け取るコンストラクタ
	public MainWindowViewModel(Core.Factory.IWindowFactory windowFactory,
		IHistoryService historyService,
		ITemplateService templateService,
		ISettingsService settingsService)
	{
		_windowFactory = windowFactory;
		_historyService = historyService;
		_templateService = templateService;
		_config = settingsService.Load();

		// 設定ファイルから確認ダイアログの設定を読み込む
		_confirmTemplateOverwrite = _config.Config.EditorSettings.ConfirmTemplateOverwrite;
		_confirmHistoryOverwrite = _config.Config.EditorSettings.ConfirmHistoryOverwrite;

		_insertTemplateTextOnClear = _config.Config.EditorSettings.InsertTemplateTextOnClear;
		_templateTextForEditor = _config.Config.EditorSettings.TemplateTextForEditor;
		ExecuteAfterSend = _config.Config.UiState?.ExecuteAfterSend
			?? _config.Config.ExecuteAfterSendSettings?.DefaultEnabled
			?? false;
		RightPaneSelectedTab = NormalizeRightPaneSelectedTab(_config.Config.UiState?.RightPaneSelectedTab);
		ApplyPaneDisplayMode(_config.Config.UiState?.PaneDisplayMode);

		// アプリ起動時に履歴を読み込む
		LoadHistories();

		// SlackStyleHistoryViewが初期化された後に最下部にスクロールさせるためのイベント
		Application.Current.Dispatcher.InvokeAsync(() =>
		{
			if (Application.Current.MainWindow is Views.MainWindow mainWindow)
			{
				var slackHistoryView = mainWindow.FindName("SlackHistoryView") as Core.Controls.SlackStyleHistoryView;
				slackHistoryView?.ScrollToLatestHistory();
			}
		}, System.Windows.Threading.DispatcherPriority.Loaded);

		_ = LoadTemplateTreeAsync();
		ClearEditor();
	}

	partial void OnIsDarkThemeChanged(bool value)
	{
		UiStateChanged?.Invoke(this, EventArgs.Empty);
	}

	partial void OnExecuteAfterSendChanged(bool value)
	{
		UiStateChanged?.Invoke(this, EventArgs.Empty);
	}

	partial void OnRightPaneSelectedTabChanged(string value)
	{
		RightPaneSelectedTab = NormalizeRightPaneSelectedTab(value);
		UiStateChanged?.Invoke(this, EventArgs.Empty);
	}

	private static string NormalizeRightPaneSelectedTab(string? value)
	{
		return string.Equals(value, "Template", StringComparison.OrdinalIgnoreCase)
			? "Template"
			: "History";
	}

	public string GetPaneDisplayMode()
	{
		return DisplayMode switch
		{
			1 => "LeftPane",
			2 => "RightPane",
			_ => "TwoPane"
		};
	}

	public void ApplyPaneDisplayMode(string? value)
	{
		DisplayMode = PaneDisplayModeToInt(value);
		UpdateLayout();
	}

	private static int PaneDisplayModeToInt(string? value)
	{
		return value?.Trim().ToUpperInvariant() switch
		{
			"LEFTPANE" => 1,
			"RIGHTPANE" => 2,
			_ => 0
		};
	}

	// 履歴データを読み込む
	private void LoadHistories()
	{
		HistoryItems.Clear();

		// 古い順にソートするため、昇順で並べ替える（CreatedAtの昇順）
		var sortedHistories = _historyService.GetHistoryRecords()
			.OrderBy(h => h.CreatedAt)
			.ToList();

		foreach (var item in sortedHistories)
		{
			HistoryItems.Add(item);
		}
	}

	[RelayCommand]
	private async Task LoadTemplateTreeAsync()
	{
		IsTemplateTreeLoading = true;
		TemplateLoadErrorMessage = null;

		try
		{
			var ownerWindowHandle = GetMainWindowHandle();
			var nodes = await Task.Run(() => _templateService.GetTemplateTree(ownerWindowHandle));

			TemplateTreeNodes.Clear();
			foreach (var node in nodes)
			{
				TemplateTreeNodes.Add(node);
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"テンプレートツリーの読み込みに失敗しました: {ex}");
			TemplateTreeNodes.Clear();
			TemplateLoadErrorMessage = $"テンプレートディレクトリを開けません。\n{_templateService.GetResolvedTemplateDirectory()}";
		}
		finally
		{
			IsTemplateTreeLoading = false;
		}
	}

	[RelayCommand]
	private void ApplyTemplateNode(TemplateTreeNode? node)
	{
		node ??= SelectedTemplateNode;
		if (node == null || node.IsDirectory)
		{
			return;
		}

		try
		{
			var templateContent = _templateService.LoadTemplateByPath(node.FullPath);
			ApplyTemplateContent(node.Name, templateContent);
			TemplateLoadErrorMessage = null;
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"テンプレートファイルの読み込みに失敗しました: {ex}");
			TemplateLoadErrorMessage = $"テンプレートファイルを読み込めません。\n{node.FullPath}";
		}
	}

	private void ApplyTemplateContent(string templateName, string templateContent)
	{
		bool showDialog = true;

		if (_confirmTemplateOverwrite)
		{
			if (string.IsNullOrEmpty(EditorText))
			{
				showDialog = false;
			}
			if (_insertTemplateTextOnClear && string.Equals(_templateTextForEditor, EditorText))
			{
				showDialog = false;
			}
			if (string.Equals(EditorText, templateContent))
			{
				showDialog = false;
			}
		}
		else
		{
			showDialog = false;
		}

		if (showDialog)
		{
			MessageBoxResult result = MessageBox.Show(
				$"現在のエディタの内容を破棄して、テンプレート「{templateName}」を適用しますか？",
				"確認",
				MessageBoxButton.YesNo,
				MessageBoxImage.Question);

			if (result != MessageBoxResult.Yes)
			{
				return;
			}
		}

		EditorText = templateContent;
	}

	private static IntPtr GetMainWindowHandle()
	{
		if (Application.Current.MainWindow == null)
		{
			return IntPtr.Zero;
		}

		return new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow).Handle;
	}

	// SelectedHistoryItem が変更された時の処理
	partial void OnSelectedHistoryItemChanged(HistoryItem? value)
	{
		// nullの場合や既に確認済みの場合はスキップ
		if (value == null)
		{
			return;
		}
		ApplyHistory(value);
	}

	// EditorTextの更新を通知するイベント
	public event EventHandler<string>? EditorTextChanged;

	// EditorTextが変更されたときにイベントを発生させる
	partial void OnEditorTextChanged(string value)
	{
		EditorTextChanged?.Invoke(this, value);
	}

	[RelayCommand]
	private void OpenHistoryManagementWindow()
	{
		var dlg = _windowFactory?.CreateWindow<Views.HistoryManagementWindow>();
		dlg?.ShowDialog();

		// 履歴管理ウィンドウが閉じられた後、履歴を再読み込み
		LoadHistories();

		// SlackStyleHistoryViewを更新
		if (Application.Current.MainWindow is Views.MainWindow mainWindow)
		{
			var slackHistoryView = mainWindow.FindName("SlackHistoryView") as Core.Controls.SlackStyleHistoryView;
			slackHistoryView?.InitialRebuildWithLatestItems();
			slackHistoryView?.ScrollToLatestHistory();
		}
	}

	[RelayCommand]
	private void HistoryClicked(HistoryItem? historyItem)
	{
		if (historyItem != null)
		{
			ApplyHistory(historyItem);
		}
	}

	// 履歴をエディタに適用する処理
	private void ApplyHistory(HistoryItem historyItem)
	{
		bool showDialog = true;

		if (_confirmHistoryOverwrite)
		{
			if (string.IsNullOrEmpty(EditorText))
			{
				// エディタが空の場合は確認不要
				showDialog = false;
			}
			if (_insertTemplateTextOnClear)
			{
				if (string.Equals(_templateTextForEditor, EditorText))
				{
					// クリア用テンプレートテキストと選択履歴が同じ場合は確認不要
					showDialog = false;
				}
			}
			if (string.Equals(EditorText, historyItem.Text))
			{
				showDialog = false; // 履歴と同じ場合は確認不要
			}
		}
		else
		{
			// 確認ダイアログを表示しない設定の場合は常に適用
			showDialog = false;
		}
		if (showDialog)
		{
			// 確認ダイアログを表示
			MessageBoxResult result = MessageBox.Show(
				"現在のエディタの内容を破棄して、選択した履歴を適用しますか？",
				"確認",
				MessageBoxButton.YesNo,
				MessageBoxImage.Question);

			if (result != MessageBoxResult.Yes)
			{
				return;
			}
		}

		// 履歴を適用し、最後に確認した項目として記録
		EditorText = historyItem.Text;
	}

	// 検索用プロパティ
	[ObservableProperty]
	private string _searchText = string.Empty;

	// 検索履歴
	public ObservableCollection<string> SearchHistories { get; } = new();

	// 検索コマンド
	[RelayCommand]
	private void SearchHistory()
	{
		if (string.IsNullOrWhiteSpace(SearchText))
			return;

		// 検索履歴に追加（重複は追加しない）
		if (!SearchHistories.Contains(SearchText))
		{
			// 検索履歴は最大10件程度に制限
			if (SearchHistories.Count >= 10)
				SearchHistories.RemoveAt(SearchHistories.Count - 1);

			SearchHistories.Insert(0, SearchText);
		}

		// 元の全履歴アイテムのコピーを保持
		var allHistoryItems = _historyService.GetHistoryRecords().ToList();

		// 検索条件に一致するアイテムをフィルタリング
		var filteredItems = allHistoryItems
			.Where(item => item.Text.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
			.OrderBy(item => item.CreatedAt)
			.ToList();

		// UIを更新
		HistoryItems.Clear();
		foreach (var item in filteredItems)
		{
			HistoryItems.Add(item);
		}

		// SlackStyleHistoryViewを更新
		if (Application.Current.MainWindow is Views.MainWindow mainWindow)
		{
			var slackHistoryView = mainWindow.FindName("SlackHistoryView") as Core.Controls.SlackStyleHistoryView;
			slackHistoryView?.InitialRebuildWithLatestItems();
			slackHistoryView?.ScrollToLatestHistory();
		}
	}

	// 検索クリアコマンド
	[RelayCommand]
	private void ClearSearch()
	{
		// 検索テキストをクリア
		SearchText = string.Empty;

		// 全履歴を再表示
		LoadHistories();

		// SlackStyleHistoryViewを更新
		if (Application.Current.MainWindow is Views.MainWindow mainWindow)
		{
			var slackHistoryView = mainWindow.FindName("SlackHistoryView") as Core.Controls.SlackStyleHistoryView;
			slackHistoryView?.InitialRebuildWithLatestItems();
			slackHistoryView?.ScrollToLatestHistory();
		}
	}

	[RelayCommand]
	private void ToggleTheme()
	{
		// MaterialDesignのテーマを変更
		var paletteHelper = new PaletteHelper();
		var theme = paletteHelper.GetTheme();
		theme.SetBaseTheme(IsDarkTheme ? BaseTheme.Dark : BaseTheme.Light);
		paletteHelper.SetTheme(theme);

		if (App.Current.MainWindow is Views.MainWindow mainWindow)
		{
			// AvalonEditorにテーマを適用
			var avalonEditor = mainWindow.FindName("AvalonEditor") as Core.Controls.AvalonTextEditor;
			avalonEditor?.ApplyTheme(IsDarkTheme);

			// TabControlのすべてのタブ内のWebView2のテーマを変更
			foreach (var grid in mainWindow.GetChatContentElements())
			{
				var webView = grid.Children.OfType<WebView2>().FirstOrDefault();
				if (webView != null && webView.CoreWebView2 != null)
				{
					// ダークモード設定を適用
					try
					{
						// WebView2のプリファレンス設定でダークモードを切り替え
						webView.CoreWebView2.Profile.PreferredColorScheme =
							IsDarkTheme
								? Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Dark
								: Microsoft.Web.WebView2.Core.CoreWebView2PreferredColorScheme.Light;
					}
					catch (Exception ex)
					{
						// CoreWebView2がまだ初期化されていない場合などに例外が発生する可能性がある
						Debug.WriteLine($"WebView2テーマ設定エラー: {ex.Message}");
					}
				}
			}
		}
	}

	// クリアボタン
	[RelayCommand]
	private void ClearEditor()
	{
		if (_insertTemplateTextOnClear && !string.IsNullOrEmpty(_templateTextForEditor))
		{
			EditorText = _templateTextForEditor;
		}
		else
		{
			EditorText = string.Empty;
		}
	}

	// 保存ボタン
	[RelayCommand]
	private void SaveHistory()
	{
		if (!string.IsNullOrWhiteSpace(EditorText))
		{
			// DB に保存
			_historyService.AddHistory(EditorText);

			// 最新の1つだけを取得して追加
			var newItem = _historyService.GetLatestHistoryItem();

			if (newItem != null && !HistoryItems.Any(h => h.Id == newItem.Id))
			{
				// 最新のアイテムを追加（オブジェクトを直接追加）
				HistoryItems.Add(newItem);

				// スクロールを最下部に移動
				if (Application.Current.MainWindow is Views.MainWindow mainWindow)
				{
					var slackHistoryView = mainWindow.FindName("SlackHistoryView") as Core.Controls.SlackStyleHistoryView;
					slackHistoryView?.ScrollToLatestHistory();
				}
			}
		}
	}

	[RelayCommand]
	private void SaveHistoryToFile()
	{
		if (!string.IsNullOrWhiteSpace(EditorText))
		{
			var templateDirectory = _templateService.GetResolvedTemplateDirectory();

			// ファイル保存ダイアログを表示
			var saveFileDialog = new SaveFileDialog()
			{
				Filter = "テキストファイル (*.txt)|*.txt|Markdown (*.md)|*.md|すべてのファイル (*.*)|*.*",
				DefaultExt = ".txt",
				AddExtension = true,
				OverwritePrompt = true,
				CreatePrompt = false,
				InitialDirectory = Directory.Exists(templateDirectory)
					? templateDirectory
					: AppDomain.CurrentDomain.BaseDirectory,
				FileName = $"{DateTime.Now:yyyyMMdd_HHmmss}.txt"
			};
			if (saveFileDialog.ShowDialog() != true)
			{
				return; // ユーザーがキャンセルした場合は何もしない
			}
			// ファイルパスを取得
			var filePath = saveFileDialog.FileName;
			// テキストをファイルに保存
			try
			{
				File.WriteAllText(filePath, EditorText);

				// テンプレートツリーを更新
				_ = LoadTemplateTreeAsync();
			}
			catch (Exception ex)
			{
				MessageBox.Show($"ファイルの保存に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
				return;
			}
		}
	}

	// In MainWindowViewModel.cs, add these properties
	[ObservableProperty]
	private string _chatButtonText = "チャットへ";

	[ObservableProperty]
	private bool _isSending = false;

	[ObservableProperty]
	private bool _executeAfterSend = false;

	// IsSendingの否定値を取得するプロパティ
	public bool IsSendingNot => !IsSending;

	// IsSendingが変更されたときにIsSendingNotも通知する
	partial void OnIsSendingChanged(bool value)
	{
		OnPropertyChanged(nameof(IsSendingNot));
		ChatToServiceCommand.NotifyCanExecuteChanged();
	}

	[RelayCommand(CanExecute = nameof(CanSendChat))]
	private async Task ChatToServiceAsync(object parameter)
	{
		try
		{
			IsSending = true;
			ChatButtonText = "送信中...";

			WebView2? webview = parameter switch
			{
				WebView2 wv => wv,
				Grid grid => grid.Children.OfType<WebView2>().FirstOrDefault(),
				_ => null
			};

			if (webview == null)
			{
				return;
			}

			await webview.EnsureCoreWebView2Async();

			var normalizedText = await Task.Run(() => (EditorText ?? string.Empty).Replace("\r\n", "\n"));
			ChatInputApplyResult inputResult;
			try
			{
				inputResult = await ApplyTextToChatInputAsync(webview, normalizedText);
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"[ChatToService] 入力欄への反映中に例外が発生しました: {ex.Message}");
				return;
			}

			Debug.WriteLine($"[ChatToService] input result: inputFound={inputResult.InputFound}, textApplied={inputResult.TextApplied}, selector={inputResult.Selector}, isEmpty={inputResult.IsEmpty}, reason={inputResult.Reason}");

			if (!inputResult.TextApplied)
			{
				Debug.WriteLine("[ChatToService] 入力欄への反映に失敗したため、履歴保存とエディタクリアを行いません。");
				return;
			}

			if (ExecuteAfterSend && !inputResult.IsEmpty)
			{
				try
				{
					var executeResult = await TryExecuteAfterSendAsync(webview, inputResult);
					var invalidSelectors = executeResult.InvalidSelectors.Count == 0
						? string.Empty
						: string.Join(", ", executeResult.InvalidSelectors);
					Debug.WriteLine($"[ChatToService] execute result: executed={executeResult.Executed}, skipped={executeResult.Skipped}, service={executeResult.ServiceName}, method={executeResult.Method}, selector={executeResult.Selector}, invalidSelectors={invalidSelectors}, reason={executeResult.Reason}");
				}
				catch (Exception ex)
				{
					Debug.WriteLine($"[ChatToService] 送信後実行中に例外が発生しました。入力反映は成功しているため履歴保存とエディタクリアは継続します: {ex.Message}");
				}
			}

			await SaveSentTextToHistoryAsync(normalizedText);
			ClearEditor();
		}
		finally
		{
			ChatButtonText = "チャットへ";
			IsSending = false;
		}
	}

	private async Task<ChatInputApplyResult> ApplyTextToChatInputAsync(WebView2 webview, string normalizedText)
	{
		string textJson = JsonSerializer.Serialize(normalizedText);
		string selectorsJson = JsonSerializer.Serialize(_config.Config.InputSelectors ?? new List<string>());
		var script = BuildApplyInputScript(textJson, selectorsJson);
		var rawResult = await webview.ExecuteScriptAsync(script);
		return DeserializeScriptResult<ChatInputApplyResult>(rawResult)
			?? ChatInputApplyResult.Failed(string.IsNullOrWhiteSpace(normalizedText), "Input script returned no result.");
	}

	private async Task<ChatExecuteResult> TryExecuteAfterSendAsync(WebView2 webview, ChatInputApplyResult inputResult)
	{
		var settings = _config.Config.ExecuteAfterSendSettings ?? new ExecuteAfterSendSettings();
		var executor = FindServiceExecutor(webview);
		if (executor == null)
		{
			var behavior = NormalizeUnsupportedServiceBehavior(settings.UnsupportedServiceBehavior);
			if (behavior == "ShowWarning")
			{
				MessageBox.Show("このチャットサービスは送信後実行に対応していません。入力のみ完了しました。", "送信後実行", MessageBoxButton.OK, MessageBoxImage.Information);
			}

			return ChatExecuteResult.InputOnly("対応するサービス設定が見つかりません。");
		}

		var postInputDelayMs = Math.Max(0, settings.PostInputDelayMs);
		if (postInputDelayMs > 0)
		{
			await Task.Delay(postInputDelayMs);
		}

		if (settings.EnableDomAnalysisLog)
		{
			await LogDomAnalysisAsync(webview, executor.ServiceName);
		}

		var timeoutMs = NormalizePositive(settings.ExecutionTimeoutMs, 3000);
		var retryIntervalMs = NormalizePositive(settings.RetryIntervalMs, 100);
		var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
		ChatExecuteResult lastResult = ChatExecuteResult.Failed(executor.ServiceName, "実行ボタンが見つかりません。");

		do
		{
			lastResult = await TryClickSubmitButtonAsync(webview, executor, inputResult.Selector);
			if (lastResult.Executed)
			{
				return lastResult;
			}

			if (DateTime.UtcNow < deadline)
			{
				await Task.Delay(retryIntervalMs);
			}
		}
		while (DateTime.UtcNow < deadline);

		var fallback = NormalizeKeyboardFallback(executor.KeyboardFallback);
		if (fallback != "None")
		{
			var fallbackResult = await TryKeyboardFallbackAsync(webview, executor, inputResult.Selector, fallback);
			if (fallbackResult.Executed)
			{
				return fallbackResult;
			}

			lastResult = fallbackResult;
		}

		return lastResult;
	}

	private async Task SaveSentTextToHistoryAsync(string normalizedText)
	{
		if (string.IsNullOrWhiteSpace(normalizedText))
		{
			return;
		}

		bool needToReloadHistory = false;
		var latestText = HistoryItems.Count > 0
			? HistoryItems.OrderByDescending(h => h.CreatedAt).FirstOrDefault()?.Text
			: null;

		await Task.Run(() =>
		{
			if (latestText != normalizedText)
			{
				_historyService.AddHistory(normalizedText);
				needToReloadHistory = true;
			}
		});

		if (!needToReloadHistory)
		{
			return;
		}

		var newItem = _historyService.GetHistoryRecords()
			.OrderByDescending(h => h.CreatedAt)
			.FirstOrDefault();

		if (newItem != null && !HistoryItems.Any(h => h.Id == newItem.Id))
		{
			HistoryItems.Add(newItem);
		}

		if (Application.Current.MainWindow is Views.MainWindow mainWindow)
		{
			var slackHistoryView = mainWindow.FindName("SlackHistoryView") as Core.Controls.SlackStyleHistoryView;
			slackHistoryView?.ScrollToLatestHistory();
		}
	}

	private ServiceExecutorSettings? FindServiceExecutor(WebView2 webview)
	{
		var source = GetWebViewSource(webview);
		if (string.IsNullOrWhiteSpace(source))
		{
			return null;
		}

		foreach (var executor in GetServiceExecutors())
		{
			if (executor.UrlPatterns.Any(pattern =>
				!string.IsNullOrWhiteSpace(pattern) &&
				source.Contains(pattern, StringComparison.OrdinalIgnoreCase)))
			{
				return executor;
			}
		}

		return null;
	}

	private IReadOnlyList<ServiceExecutorSettings> GetServiceExecutors()
	{
		var executors = _config.Config.ExecuteAfterSendSettings?.ServiceExecutors;
		return executors is { Count: > 0 } ? executors : DefaultServiceExecutors;
	}

	private static string GetWebViewSource(WebView2 webview)
	{
		var coreSource = webview.CoreWebView2?.Source;
		if (!string.IsNullOrWhiteSpace(coreSource))
		{
			return coreSource;
		}

		return webview.Source?.ToString() ?? string.Empty;
	}

	private async Task LogDomAnalysisAsync(WebView2 webview, string serviceName)
	{
		try
		{
			var rawResult = await webview.ExecuteScriptAsync(BuildDomAnalysisScript());
			Debug.WriteLine($"[ExecuteAfterSend][DOM][{serviceName}] {rawResult}");
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"[ExecuteAfterSend][DOM][{serviceName}] DOM解析ログの取得に失敗しました: {ex.Message}");
		}
	}

	private async Task<ChatExecuteResult> TryClickSubmitButtonAsync(WebView2 webview, ServiceExecutorSettings executor, string inputSelector)
	{
		string selectorsJson = JsonSerializer.Serialize(executor.SubmitButtonSelectors ?? new List<string>());
		string inputSelectorJson = JsonSerializer.Serialize(inputSelector ?? string.Empty);
		var script = BuildClickSubmitButtonScript(selectorsJson, inputSelectorJson);
		var rawResult = await webview.ExecuteScriptAsync(script);
		var result = DeserializeScriptResult<ChatExecuteResult>(rawResult)
			?? ChatExecuteResult.Failed(executor.ServiceName, "Click script returned no result.");
		result.ServiceName = executor.ServiceName;
		return result;
	}

	private async Task<ChatExecuteResult> TryKeyboardFallbackAsync(WebView2 webview, ServiceExecutorSettings executor, string inputSelector, string fallback)
	{
		string inputSelectorJson = JsonSerializer.Serialize(inputSelector ?? string.Empty);
		string fallbackJson = JsonSerializer.Serialize(fallback);
		var script = BuildKeyboardFallbackScript(inputSelectorJson, fallbackJson);
		var rawResult = await webview.ExecuteScriptAsync(script);
		var result = DeserializeScriptResult<ChatExecuteResult>(rawResult)
			?? ChatExecuteResult.Failed(executor.ServiceName, "Keyboard fallback script returned no result.");
		result.ServiceName = executor.ServiceName;
		return result;
	}

	private static T? DeserializeScriptResult<T>(string? rawResult)
	{
		if (string.IsNullOrWhiteSpace(rawResult) || rawResult == "null")
		{
			return default;
		}

		try
		{
			return JsonSerializer.Deserialize<T>(rawResult, ScriptJsonOptions);
		}
		catch (JsonException)
		{
			try
			{
				var json = JsonSerializer.Deserialize<string>(rawResult);
				return string.IsNullOrWhiteSpace(json)
					? default
					: JsonSerializer.Deserialize<T>(json, ScriptJsonOptions);
			}
			catch (JsonException)
			{
				return default;
			}
		}
	}

	private static string BuildApplyInputScript(string textJson, string selectorsJson)
	{
		return """
(function() {
  const text = __TEXT_JSON__;
  const selectors = Array.isArray(__SELECTORS_JSON__) ? __SELECTORS_JSON__ : [];
  const result = {
    inputFound: false,
    textApplied: false,
    selector: "",
    isEmpty: String(text).trim().length === 0,
    reason: ""
  };

  let input = null;
  for (const selector of selectors) {
    if (typeof selector !== "string" || selector.trim() === "") {
      continue;
    }

    try {
      input = document.querySelector(selector);
    } catch (_) {
      continue;
    }

    if (input) {
      result.inputFound = true;
      result.selector = selector;
      break;
    }
  }

  if (!input) {
    result.reason = "Input element was not found.";
    return result;
  }

  try {
    input.focus();
    const tagName = (input.tagName || "").toUpperCase();

    if (tagName === "TEXTAREA" || tagName === "INPUT") {
      const prototype = tagName === "TEXTAREA" ? window.HTMLTextAreaElement.prototype : window.HTMLInputElement.prototype;
      const valueSetter = Object.getOwnPropertyDescriptor(prototype, "value")?.set;
      if (valueSetter) {
        valueSetter.call(input, text);
      } else {
        input.value = text;
      }

      input.dispatchEvent(new InputEvent("input", { bubbles: true, inputType: "insertText", data: text }));
      input.dispatchEvent(new Event("change", { bubbles: true }));
    } else if (input.isContentEditable || input.getAttribute("contenteditable") === "true") {
      const selection = window.getSelection();
      const range = document.createRange();
      range.selectNodeContents(input);
      selection.removeAllRanges();
      selection.addRange(range);

      const inserted = document.execCommand("insertText", false, text);
      if (!inserted) {
        input.textContent = text;
      }

      input.dispatchEvent(new InputEvent("input", { bubbles: true, inputType: "insertText", data: text }));
      input.dispatchEvent(new Event("change", { bubbles: true }));
    } else {
      result.reason = "Unsupported input element type.";
      return result;
    }

    result.textApplied = true;
    result.reason = "OK";
    return result;
  } catch (error) {
    result.reason = error && error.message ? error.message : "Input application failed.";
    return result;
  }
})();
""".Replace("__TEXT_JSON__", textJson).Replace("__SELECTORS_JSON__", selectorsJson);
	}

	private static string BuildClickSubmitButtonScript(string selectorsJson, string inputSelectorJson)
	{
		return """
(function() {
  const selectors = Array.isArray(__SELECTORS_JSON__) ? __SELECTORS_JSON__ : [];
  const inputSelector = __INPUT_SELECTOR_JSON__;
  const result = {
    executed: false,
    skipped: false,
    serviceName: "",
    method: "click",
    selector: "",
    reason: "",
    invalidSelectors: []
  };

  function textOf(value) {
    return String(value || "").trim();
  }

  function lower(value) {
    return textOf(value).toLowerCase();
  }

  function isVisible(element) {
    const style = window.getComputedStyle(element);
    if (style.visibility === "hidden" || style.display === "none" || Number(style.opacity) === 0) {
      return false;
    }

    const rect = element.getBoundingClientRect();
    return rect.width > 0 && rect.height > 0;
  }

  function isDisabled(element) {
    return element.disabled === true ||
      element.getAttribute("aria-disabled") === "true" ||
      element.getAttribute("disabled") !== null;
  }

  function findInput() {
    if (!inputSelector) {
      return null;
    }

    try {
      return document.querySelector(inputSelector);
    } catch (_) {
      return null;
    }
  }

  function scoreCandidate(element, input) {
    let score = 0;
    const aria = lower(element.getAttribute("aria-label"));
    const title = lower(element.getAttribute("title"));
    const dataTestId = lower(element.getAttribute("data-testid") || element.getAttribute("data-test-id"));
    const type = lower(element.getAttribute("type"));
    const text = lower(element.innerText).slice(0, 80);

    if (dataTestId.includes("send") || dataTestId.includes("submit")) score += 100;
    if (aria.includes("send") || aria.includes("送信")) score += 90;
    if (title.includes("send") || title.includes("送信")) score += 70;
    if (type === "submit") score += 60;
    if (text === "send" || text === "送信") score += 50;

    if (input) {
      const form = input.closest("form");
      if (form && form.contains(element)) {
        score += 50;
      }

      const inputRect = input.getBoundingClientRect();
      const buttonRect = element.getBoundingClientRect();
      const distance = Math.abs(buttonRect.left - inputRect.right) + Math.abs(buttonRect.top - inputRect.top);
      score += Math.max(0, 40 - Math.min(40, distance / 20));
    }

    return score;
  }

  const input = findInput();
  const candidates = [];
  const seen = new Set();

  for (const selector of selectors) {
    if (typeof selector !== "string" || selector.trim() === "") {
      continue;
    }

    let elements = [];
    try {
      elements = Array.from(document.querySelectorAll(selector));
    } catch (error) {
      result.invalidSelectors.push(selector);
      continue;
    }

    for (const element of elements) {
      if (!element || seen.has(element) || !isVisible(element) || isDisabled(element)) {
        continue;
      }

      seen.add(element);
      candidates.push({
        element: element,
        selector: selector,
        score: scoreCandidate(element, input)
      });
    }
  }

  if (candidates.length === 0) {
    result.reason = "No enabled submit candidate found.";
    return result;
  }

  candidates.sort((a, b) => b.score - a.score);
  const candidate = candidates[0];

  try {
    candidate.element.scrollIntoView({ block: "nearest", inline: "nearest" });
    try {
      candidate.element.focus({ preventScroll: true });
    } catch (_) {
      candidate.element.focus();
    }

    candidate.element.click();
    result.executed = true;
    result.selector = candidate.selector;
    result.reason = "Clicked submit candidate.";
    return result;
  } catch (error) {
    result.reason = error && error.message ? error.message : "Click failed.";
    return result;
  }
})();
""".Replace("__SELECTORS_JSON__", selectorsJson).Replace("__INPUT_SELECTOR_JSON__", inputSelectorJson);
	}

	private static string BuildKeyboardFallbackScript(string inputSelectorJson, string fallbackJson)
	{
		return """
(function() {
  const inputSelector = __INPUT_SELECTOR_JSON__;
  const fallback = __FALLBACK_JSON__;
  const result = {
    executed: false,
    skipped: false,
    serviceName: "",
    method: fallback,
    selector: inputSelector || "",
    reason: "",
    invalidSelectors: []
  };

  let input = null;
  if (inputSelector) {
    try {
      input = document.querySelector(inputSelector);
    } catch (_) {
      input = null;
    }
  }

  if (!input) {
    result.reason = "Keyboard fallback target was not found.";
    return result;
  }

  try {
    input.focus();
    const eventInit = {
      key: "Enter",
      code: "Enter",
      keyCode: 13,
      which: 13,
      bubbles: true,
      cancelable: true,
      ctrlKey: fallback === "CtrlEnter"
    };

    input.dispatchEvent(new KeyboardEvent("keydown", eventInit));
    input.dispatchEvent(new KeyboardEvent("keypress", eventInit));
    input.dispatchEvent(new KeyboardEvent("keyup", eventInit));
    result.executed = true;
    result.reason = "Keyboard fallback dispatched.";
    return result;
  } catch (error) {
    result.reason = error && error.message ? error.message : "Keyboard fallback failed.";
    return result;
  }
})();
""".Replace("__INPUT_SELECTOR_JSON__", inputSelectorJson).Replace("__FALLBACK_JSON__", fallbackJson);
	}

	private static string BuildDomAnalysisScript()
	{
		return """
(function() {
  function trim(value, maxLength = 80) {
    const text = String(value || "").trim();
    return text.length > maxLength ? text.slice(0, maxLength) + "..." : text;
  }

  function isVisible(element) {
    const style = window.getComputedStyle(element);
    const rect = element.getBoundingClientRect();
    return style.visibility !== "hidden" &&
      style.display !== "none" &&
      Number(style.opacity) !== 0 &&
      rect.width > 0 &&
      rect.height > 0;
  }

  function describe(element, includeButtonText) {
    if (!element) {
      return null;
    }

    const description = {
      tagName: trim(element.tagName),
      id: trim(element.id),
      className: trim(element.className),
      role: trim(element.getAttribute("role")),
      type: trim(element.getAttribute("type")),
      ariaLabel: trim(element.getAttribute("aria-label")),
      title: trim(element.getAttribute("title")),
      dataTestId: trim(element.getAttribute("data-testid") || element.getAttribute("data-test-id")),
      disabled: element.disabled === true || element.getAttribute("disabled") !== null,
      ariaDisabled: trim(element.getAttribute("aria-disabled")),
      visible: isVisible(element)
    };

    if (includeButtonText) {
      description.innerText = trim(element.innerText);
    }

    return description;
  }

  return {
    href: trim(location.href, 160),
    title: trim(document.title, 120),
    activeElement: describe(document.activeElement, false),
    buttons: Array.from(document.querySelectorAll("button,[role='button']"))
      .slice(0, 80)
      .map(element => describe(element, true)),
    inputs: Array.from(document.querySelectorAll("textarea,input,[contenteditable='true']"))
      .slice(0, 40)
      .map(element => describe(element, false))
  };
})();
""";
	}

	private static int NormalizePositive(int value, int fallback)
	{
		return value > 0 ? value : fallback;
	}

	private static string NormalizeUnsupportedServiceBehavior(string? behavior)
	{
		return string.Equals(behavior, "ShowWarning", StringComparison.OrdinalIgnoreCase)
			? "ShowWarning"
			: "InputOnly";
	}

	private static string NormalizeKeyboardFallback(string? fallback)
	{
		if (string.Equals(fallback, "Enter", StringComparison.OrdinalIgnoreCase))
		{
			return "Enter";
		}

		if (string.Equals(fallback, "CtrlEnter", StringComparison.OrdinalIgnoreCase))
		{
			return "CtrlEnter";
		}

		return "None";
	}

	private static readonly JsonSerializerOptions ScriptJsonOptions = new()
	{
		PropertyNameCaseInsensitive = true
	};

	private static readonly IReadOnlyList<ServiceExecutorSettings> DefaultServiceExecutors = new List<ServiceExecutorSettings>
	{
		new()
		{
			ServiceName = "ChatGPT",
			UrlPatterns = new List<string> { "chat.openai.com", "chatgpt.com" },
			SubmitButtonSelectors = new List<string>
			{
				"button[data-testid=\"send-button\"]",
				"button[data-testid=\"composer-submit-button\"]",
				"button[aria-label*=\"Send\"]",
				"button[aria-label*=\"送信\"]"
			},
			KeyboardFallback = "None"
		},
		new()
		{
			ServiceName = "Gemini",
			UrlPatterns = new List<string> { "gemini.google.com" },
			SubmitButtonSelectors = new List<string>
			{
				"button[aria-label*=\"Send\"]",
				"button[aria-label*=\"送信\"]",
				"button[data-test-id*=\"send\"]",
				"button:has(mat-icon)"
			},
			KeyboardFallback = "Enter"
		},
		new()
		{
			ServiceName = "Claude",
			UrlPatterns = new List<string> { "claude.ai" },
			SubmitButtonSelectors = new List<string>
			{
				"button[aria-label*=\"Send\"]",
				"button[aria-label*=\"送信\"]",
				"button[data-testid*=\"send\"]"
			},
			KeyboardFallback = "None"
		}
	};

	private sealed class ChatInputApplyResult
	{
		public bool InputFound { get; set; }
		public bool TextApplied { get; set; }
		public string Selector { get; set; } = string.Empty;
		public bool IsEmpty { get; set; }
		public string Reason { get; set; } = string.Empty;

		public static ChatInputApplyResult Failed(bool isEmpty, string reason)
		{
			return new ChatInputApplyResult
			{
				IsEmpty = isEmpty,
				Reason = reason
			};
		}
	}

	private sealed class ChatExecuteResult
	{
		public bool Executed { get; set; }
		public bool Skipped { get; set; }
		public string ServiceName { get; set; } = string.Empty;
		public string Method { get; set; } = "None";
		public string Selector { get; set; } = string.Empty;
		public string Reason { get; set; } = string.Empty;
		public List<string> InvalidSelectors { get; set; } = new List<string>();

		public static ChatExecuteResult InputOnly(string reason)
		{
			return new ChatExecuteResult
			{
				Skipped = true,
				Method = "InputOnly",
				Reason = reason
			};
		}

		public static ChatExecuteResult Failed(string serviceName, string reason)
		{
			return new ChatExecuteResult
			{
				ServiceName = serviceName,
				Reason = reason
			};
		}
	}

	// ボタンがクリック可能かを制御するメソッド
	private bool CanSendChat() => !IsSending;

	// 左ペインのみ表示コマンド
	[RelayCommand]
	private void ShowLeftPaneOnly()
	{
		// 現在両方表示している場合は左のみに、それ以外は両方表示に戻す
		if (DisplayMode == 0)
		{
			DisplayMode = 1; // 左のみ表示
		}
		else
		{
			DisplayMode = 0; // 両方表示
		}

		UpdateLayout();
		UiStateChanged?.Invoke(this, EventArgs.Empty);
	}

	// 右ペインのみ表示コマンド
	[RelayCommand]
	private void ShowRightPaneOnly()
	{
		// 現在両方表示している場合は右のみに、それ以外は両方表示に戻す
		if (DisplayMode == 0)
		{
			DisplayMode = 2; // 右のみ表示
		}
		else
		{
			DisplayMode = 0; // 両方表示
		}

		UpdateLayout();
		UiStateChanged?.Invoke(this, EventArgs.Empty);
	}
	// 保存された列幅（分割バーを移動した後の状態を保持）
	private GridLength _savedLeftColumnWidth = new GridLength(1, GridUnitType.Star);
	private GridLength _savedRightColumnWidth = new GridLength(1, GridUnitType.Star);

	// GridSplitterのドラッグ完了時に呼び出すメソッド
	public void SaveColumnWidths(GridLength leftWidth, GridLength rightWidth)
	{
		_savedLeftColumnWidth = leftWidth;
		_savedRightColumnWidth = rightWidth;
	}

	// UpdateLayoutメソッドを修正
	public void UpdateLayout()
	{
		switch (DisplayMode)
		{
			case 0: // 両方表示
					// 保存された列幅を使用
				LeftColumnWidth = _savedLeftColumnWidth;
				RightColumnWidth = _savedRightColumnWidth;
				SplitterWidth = new GridLength(5);

				LeftPaneVisibility = Visibility.Visible;
				RightPaneVisibility = Visibility.Visible;
				SplitterVisibility = Visibility.Visible;
				break;

			case 1: // 左のみ
					// 左ペインを最大化
				LeftColumnWidth = new GridLength(1, GridUnitType.Star);
				RightColumnWidth = new GridLength(0);
				SplitterWidth = new GridLength(0);

				LeftPaneVisibility = Visibility.Visible;
				RightPaneVisibility = Visibility.Collapsed;
				SplitterVisibility = Visibility.Collapsed;
				break;

			case 2: // 右のみ
					// 右ペインを最大化
				LeftColumnWidth = new GridLength(0);
				RightColumnWidth = new GridLength(1, GridUnitType.Star);
				SplitterWidth = new GridLength(0);

				LeftPaneVisibility = Visibility.Collapsed;
				RightPaneVisibility = Visibility.Visible;
				SplitterVisibility = Visibility.Collapsed;
				break;
		}
	}

	[RelayCommand]
	private void OpenInformationWindow()
	{
		var dlg = _windowFactory?.CreateWindow<Views.InformationWindow>();
		dlg?.ShowDialog();
	}
}
