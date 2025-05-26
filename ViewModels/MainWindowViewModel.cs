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

	// テンプレート一覧
	public ObservableCollection<string> Templates { get; } = new();

	// 履歴アイテムコレクション（DataGrid用）
	public ObservableCollection<HistoryItem> HistoryItems { get; } = new();

	// 選択中の履歴アイテム
	[ObservableProperty]
	private HistoryItem? _selectedHistoryItem;

	// 選択中テンプレート
	[ObservableProperty]
	private string _selectedTemplate = string.Empty;

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

		// 起動時にテンプレート一覧をロード
		foreach (var t in _templateService.GetTemplateFileNames())
		{
			Templates.Add(t);
		}
		ClearEditor();
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

	// テンプレート選択時にファイル内容をロード
	partial void OnSelectedTemplateChanged(string value)
	{
		if (!string.IsNullOrEmpty(value))
		{
			ApplyTemplate(value);
		}
	}

	// テンプレートをエディタに適用する処理
	private void ApplyTemplate(string templateName)
	{
		string templateContent = _templateService.LoadTemplate(templateName);


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
			if (string.Equals(EditorText, templateContent))
			{
				showDialog = false; // テンプレートと同じ場合は確認不要
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
				$"現在のエディタの内容を破棄して、テンプレート「{templateName}」を適用しますか？",
				"確認",
				MessageBoxButton.YesNo,
				MessageBoxImage.Question);

			if (result != MessageBoxResult.Yes)
			{
				return;
			}
		}

		// テンプレートを適用し、最後に確認したテンプレートとして記録
		EditorText = templateContent;
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
	private void OpenTemplateFolder()
	{
		var exeDir = AppDomain.CurrentDomain.BaseDirectory;
		var templateDir = Path.Combine(exeDir, "template");
		if (!Directory.Exists(templateDir))
		{
			Directory.CreateDirectory(templateDir);
		}
		Process.Start(new ProcessStartInfo("explorer.exe", $"\"{templateDir}\"") { UseShellExecute = true });

		// 起動時にテンプレート一覧をロード
		_templateService.RebuildTemplateMap();
		Templates.Clear();
		foreach (var t in _templateService.GetTemplateFileNames())
		{
			Templates.Add(t);
		}
	}

	[RelayCommand]
	private void TemplateDropDownClosed(string value)
	{
		if (!string.IsNullOrEmpty(value))
		{
			ApplyTemplate(value);
		}
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
			var tabControl = mainWindow.FindName("TabControlMain") as TabControl;
			if (tabControl != null)
			{
				foreach (TabItem tabItem in tabControl.Items)
				{
					if (tabItem.Content is Grid grid)
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
			// ファイル保存ダイアログを表示
			var saveFileDialog = new SaveFileDialog()
			{
				Filter = "テキストファイル (*.txt)|*.txt|Markdown (*.md)|*.md|すべてのファイル (*.*)|*.*",
				DefaultExt = ".txt",
				AddExtension = true,
				OverwritePrompt = true,
				CreatePrompt = false,
				InitialDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "template"),
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

				// テンプレート一覧を更新
				_templateService.RebuildTemplateMap();
				Templates.Clear();
				foreach (var t in _templateService.GetTemplateFileNames())
				{
					Templates.Add(t);
				}
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

	// IsSendingの否定値を取得するプロパティ
	public bool IsSendingNot => !IsSending;

	// IsSendingが変更されたときにIsSendingNotも通知する
	partial void OnIsSendingChanged(bool value)
	{
		OnPropertyChanged(nameof(IsSendingNot));
	}

	// Update the ChatToServiceCommand
	[RelayCommand(CanExecute = nameof(CanSendChat))]
	private async Task ChatToServiceAsync(object parameter)
	{
		try
		{
			// Set sending state
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

			// UIスレッドで作業を続行
			var normalizedText = await Task.Run(() => EditorText.Replace("\r\n", "\n"));
			// JSONのシリアライズを別スレッドで実行
			string textJson = await Task.Run(() => JsonSerializer.Serialize(normalizedText));
			string selectorsJson = await Task.Run(() => JsonSerializer.Serialize(_config.Config.InputSelectors));

			// スクリプトの構築
			var script = $@"(function(){{
          const text      = {textJson};
          const selectors = {selectorsJson};

          let input = null;
          for (const sel of selectors) {{
            if (sel.startsWith('#')) {{
              input = document.getElementById(sel.slice(1));
            }} else {{
              input = document.querySelector(sel);
            }}
            if (input) break;
          }}

          if (!input) {{
            console.warn('▶ No chat input found');
            return false;
          }}

          input.focus();
          if (input.tagName === 'TEXTAREA') {{
            input.value = text;
            input.dispatchEvent(new Event('input', {{ bubbles: true }}));
            input.dispatchEvent(new Event('change', {{ bubbles: true }}));
          }} else {{
            document.execCommand('selectAll', false, null);
            document.execCommand('insertText', false, text);
            input.dispatchEvent(new InputEvent('input', {{ bubbles: true }}));
          }}

          return true;
        }})();";

			// UIスレッドでWebViewのスクリプト実行
			var result = await webview.ExecuteScriptAsync(script);
			Debug.WriteLine($"[ChatToService] inject result: {result}");

			if (!string.IsNullOrWhiteSpace(normalizedText))
			{
				// データベース操作を別スレッドで実行
				bool needToReloadHistory = false;

				await Task.Run(() =>
				{
					// 最新の履歴アイテムと比較
					HistoryItem? latestItem = HistoryItems.Count > 0 ?
						HistoryItems.OrderByDescending(h => h.CreatedAt).FirstOrDefault() : null;
					if (latestItem == null || latestItem.Text != normalizedText)
					{
						// 最新エントリと異なる場合のみ追加
						_historyService.AddHistory(normalizedText);
						needToReloadHistory = true; // 履歴を再読み込みする必要あり
					}
				});

				// 必要なら新しいアイテムだけを追加（全部を再ロードしない）
				if (needToReloadHistory)
				{
					// 最新の1つだけを取得
					var newItem = _historyService.GetHistoryRecords()
						.OrderByDescending(h => h.CreatedAt)
						.FirstOrDefault();

					if (newItem != null && !HistoryItems.Any(h => h.Id == newItem.Id))
					{
						HistoryItems.Add(newItem);
					}

					// スクロールを最下部に移動
					if (Application.Current.MainWindow is Views.MainWindow mainWindow)
					{
						var slackHistoryView = mainWindow.FindName("SlackHistoryView") as Core.Controls.SlackStyleHistoryView;
						slackHistoryView?.ScrollToLatestHistory();
					}
				}
			}

			// エディタテキストをクリア
			ClearEditor();
		}
		finally
		{
			// ボタンの状態をリセット
			ChatButtonText = "チャットへ";
			IsSending = false;
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