using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using AIChatHelper.Core.Controls;
using AIChatHelper.Models;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AIChatHelper.Views;

public partial class MainWindow : Window
{
	private readonly Core.Factory.IWindowFactory? _windowFactory;
	private readonly List<ChatSite> _chatSites = new();
	private readonly Dictionary<string, int> _tabNameCounts = new(StringComparer.OrdinalIgnoreCase);
	private Grid? _mainGrid;
	private AvalonTextEditor? _avalonEditor;
	private Image? _templateImage;
	private Image? _historyImage;

	public MainWindow()
	{
		InitializeComponent();
	}

	public MainWindow(Core.Factory.IWindowFactory windowFactory,
					ViewModels.MainWindowViewModel viewModel,
					Core.Services.ISettingsService settingsService)
		: this()
	{
		DataContext = viewModel;
		_windowFactory = windowFactory;

		// メインのGridを参照として保持
		_mainGrid = (Grid)Content;

		// 起動時のみOSのテーマを検出して、WebView2初期化前にViewModelへ反映
		bool isDarkMode = IsSystemInDarkMode();
		viewModel.IsDarkTheme = isDarkMode;

		TabControlMain.PreviewKeyDown += TabControlMain_PreviewKeyDown;

		var config = settingsService.Load();
		_chatSites.AddRange(config.ChatSites);
		ChatSiteComboBox.ItemsSource = _chatSites;
		AddChatTabButton.IsEnabled = _chatSites.Count > 0;
		if (_chatSites.Count > 0)
		{
			ChatSiteComboBox.SelectedIndex = 0;
		}

		foreach (var site in _chatSites)
		{
			CreateChatTab(site, selectTab: false);
		}
		if (TabControlMain.Items.Count > 0)
		{
			TabControlMain.SelectedIndex = 0;
		}

		// AvalonEditorの参照を取得
		_avalonEditor = FindName("AvalonEditor") as AvalonTextEditor;

		// アイコン画像の参照を取得
		_templateImage = FindName("TemplateImage") as Image;
		_historyImage = FindName("HistoryImage") as Image;

		// テーマに合わせてAvalonEditorのテーマも設定
		_avalonEditor?.ApplyTheme(isDarkMode);

		// ウィンドウがロードされた後に履歴ビューを最下部にスクロール
		Loaded += MainWindow_Loaded;
	}

	private void ChatSiteComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		AddChatTabButton.IsEnabled = ChatSiteComboBox.SelectedItem is ChatSite;
	}

	private void AddChatTabButton_Click(object sender, RoutedEventArgs e)
	{
		if (ChatSiteComboBox.SelectedItem is ChatSite site)
		{
			CreateChatTab(site, selectTab: true);
		}
	}

	private void CreateChatTab(ChatSite site, bool selectTab)
	{
		if (!TryCreateSiteUri(site, out var uri))
		{
			MessageBox.Show(
				"選択したチャットサイトのURLが不正です。",
				"タブ追加エラー",
				MessageBoxButton.OK,
				MessageBoxImage.Warning);
			return;
		}

		var browser = new WebView2
		{
			Source = uri
		};

		browser.PreviewKeyDown += WebView_PreviewKeyDown;
		browser.CoreWebView2InitializationCompleted += WebView_CoreWebView2InitializationCompleted;

		var grid = new Grid();
		grid.Children.Add(browser);

		var displayName = GetNextTabDisplayName(site);
		var item = new TabItem
		{
			Content = grid,
			Tag = site
		};
		item.Header = CreateTabHeader(item, displayName);

		TabControlMain.Items.Add(item);
		if (selectTab)
		{
			TabControlMain.SelectedItem = item;
		}
	}

	private bool TryCreateSiteUri(ChatSite site, out Uri uri)
	{
		uri = null!;
		return !string.IsNullOrWhiteSpace(site.Url)
			&& Uri.TryCreate(site.Url, UriKind.Absolute, out uri!);
	}

	private string GetNextTabDisplayName(ChatSite site)
	{
		var baseName = string.IsNullOrWhiteSpace(site.Name) ? "無題" : site.Name.Trim();
		var nextCount = _tabNameCounts.TryGetValue(baseName, out var currentCount)
			? currentCount + 1
			: 1;
		_tabNameCounts[baseName] = nextCount;

		return nextCount == 1 ? baseName : $"{baseName} ({nextCount})";
	}

	private FrameworkElement CreateTabHeader(TabItem tabItem, string displayName)
	{
		var panel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			VerticalAlignment = VerticalAlignment.Center
		};

		var textBlock = new TextBlock
		{
			Text = displayName,
			TextTrimming = TextTrimming.CharacterEllipsis,
			MaxWidth = 150,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(0, 0, 4, 0)
		};

		var closeButton = new Button
		{
			Width = 22,
			Height = 22,
			Padding = new Thickness(0),
			Focusable = false,
			ToolTip = "タブを閉じる",
			Tag = tabItem,
			Content = new PackIcon
			{
				Kind = PackIconKind.Close,
				Width = 14,
				Height = 14
			}
		};

		if (TryFindResource("MaterialDesignIconButton") is Style closeButtonStyle)
		{
			closeButton.Style = closeButtonStyle;
		}
		closeButton.PreviewMouseLeftButtonDown += CloseTabButton_PreviewMouseLeftButtonDown;
		closeButton.Click += CloseTabButton_Click;

		panel.Children.Add(textBlock);
		panel.Children.Add(closeButton);
		return panel;
	}

	private void CloseTabButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		e.Handled = true;
		if (sender is Button { Tag: TabItem tabItem })
		{
			CloseChatTab(tabItem);
		}
	}

	private void CloseTabButton_Click(object sender, RoutedEventArgs e)
	{
		e.Handled = true;
		if (sender is Button { Tag: TabItem tabItem })
		{
			CloseChatTab(tabItem);
		}
	}

	private void CloseChatTab(TabItem tabItem)
	{
		var closedIndex = TabControlMain.Items.IndexOf(tabItem);
		if (closedIndex < 0)
		{
			return;
		}

		var wasSelected = ReferenceEquals(TabControlMain.SelectedItem, tabItem);
		DisposeChatTab(tabItem);
		TabControlMain.Items.Remove(tabItem);

		if (!wasSelected)
		{
			return;
		}

		if (TabControlMain.Items.Count == 0)
		{
			TabControlMain.SelectedItem = null;
			return;
		}

		var nextIndex = Math.Min(closedIndex, TabControlMain.Items.Count - 1);
		TabControlMain.SelectedIndex = nextIndex;
	}

	private void DisposeChatTab(TabItem tabItem)
	{
		if (tabItem.Header is StackPanel headerPanel)
		{
			foreach (var closeButton in headerPanel.Children.OfType<Button>())
			{
				closeButton.PreviewMouseLeftButtonDown -= CloseTabButton_PreviewMouseLeftButtonDown;
				closeButton.Click -= CloseTabButton_Click;
				closeButton.Tag = null;
			}
		}

		if (tabItem.Content is not Grid grid)
		{
			return;
		}

		foreach (var webView in grid.Children.OfType<WebView2>().ToList())
		{
			webView.PreviewKeyDown -= WebView_PreviewKeyDown;
			webView.CoreWebView2InitializationCompleted -= WebView_CoreWebView2InitializationCompleted;
			grid.Children.Remove(webView);
			try
			{
				webView.Dispose();
			}
			catch (Exception ex)
			{
				Debug.WriteLine($"WebView2破棄エラー: {ex.Message}");
			}
		}
	}

	private void WebView_CoreWebView2InitializationCompleted(object? sender, CoreWebView2InitializationCompletedEventArgs e)
	{
		if (sender is not WebView2 webView)
		{
			return;
		}

		if (!e.IsSuccess)
		{
			Debug.WriteLine($"WebView2初期化エラー: {e.InitializationException?.Message}");
			MessageBox.Show(
				"WebView2の初期化に失敗したため、タブを閉じます。",
				"タブ追加エラー",
				MessageBoxButton.OK,
				MessageBoxImage.Error);

			var failedTab = FindTabItemForWebView(webView);
			if (failedTab != null)
			{
				CloseChatTab(failedTab);
			}
			return;
		}

		if (webView.CoreWebView2 == null)
		{
			return;
		}

		try
		{
			var isDarkTheme = DataContext is ViewModels.MainWindowViewModel viewModel && viewModel.IsDarkTheme;
			webView.CoreWebView2.Profile.PreferredColorScheme =
				isDarkTheme
					? CoreWebView2PreferredColorScheme.Dark
					: CoreWebView2PreferredColorScheme.Light;
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"WebView2テーマ設定エラー: {ex.Message}");
		}
	}

	private TabItem? FindTabItemForWebView(WebView2 webView)
	{
		return TabControlMain.Items
			.OfType<TabItem>()
			.FirstOrDefault(tabItem =>
				tabItem.Content is Grid grid && grid.Children.Contains(webView));
	}

	private void MainWindow_Loaded(object sender, RoutedEventArgs e)
	{
		// ウィンドウがロードされた後に履歴ビューを最下部にスクロール
		var slackHistoryView = FindName("SlackHistoryView") as Core.Controls.SlackStyleHistoryView;
		slackHistoryView?.ScrollToLatestHistory();

		// 一度だけ実行するためにイベントハンドラを削除
		Loaded -= MainWindow_Loaded;
	}

	private void WebView_PreviewKeyDown(object sender, KeyEventArgs e)
	{
		// Ctrl+Homeが押された場合
		if (e.Key == Key.Home && Keyboard.Modifiers == ModifierKeys.Control)
		{
			if (sender is WebView2 webView && webView.CoreWebView2 != null)
			{
				// JavaScriptでページの先頭にスクロール
				webView.ExecuteScriptAsync("window.scrollTo(0, 0);");

				// イベントを処理済みとしてマーク（タブコントロールに伝播しないように）
				e.Handled = true;
			}
		}
	}

	private void TabControlMain_PreviewKeyDown(object sender, KeyEventArgs e)
	{
		// タブコントロール内でのCtrl+Homeが押された場合
		if (e.Key == Key.Home)
		{
			// 現在選択されているタブのコンテンツを取得
			if (TabControlMain.SelectedItem is TabItem tabItem &&
				tabItem.Content is Grid grid)
			{
				// GridからWebView2を検索
				var webView = grid.Children.OfType<WebView2>().FirstOrDefault();
				if (webView != null)
				{
					// WebViewのKeyDownイベントに処理を任せる
					// このイベントハンドラでは何もしない
					e.Handled = true;
				}
			}
		}
		else if (e.Key == Key.End)
		{
			// 現在選択されているタブのコンテンツを取得
			if (TabControlMain.SelectedItem is TabItem tabItem &&
				tabItem.Content is Grid grid)
			{
				// GridからWebView2を検索
				var webView = grid.Children.OfType<WebView2>().FirstOrDefault();
				if (webView != null)
				{
					// WebViewのKeyDownイベントに処理を任せる
					// このイベントハンドラでは何もしない
					e.Handled = true;
				}
			}
		}
	}

	private void GridSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
	{
		if (_mainGrid != null && DataContext is ViewModels.MainWindowViewModel viewModel)
		{
			// 現在の列幅を取得
			var leftWidth = _mainGrid.ColumnDefinitions[0].ActualWidth;
			var rightWidth = _mainGrid.ColumnDefinitions[2].ActualWidth;

			// 比率に基づいた新しいGridLengthを作成
			double totalWidth = leftWidth + rightWidth;
			var newLeftWidth = new GridLength(leftWidth / totalWidth, GridUnitType.Star);
			var newRightWidth = new GridLength(rightWidth / totalWidth, GridUnitType.Star);

			// ViewModelに保存
			viewModel.SaveColumnWidths(newLeftWidth, newRightWidth);
		}
	}

	// システムテーマを検出（起動時のみ使用）
	private bool IsSystemInDarkMode()
	{
		try
		{
			using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
			if (key != null)
			{
				var value = key.GetValue("AppsUseLightTheme");
				return value != null && (int)value == 0;
			}
		}
		catch { }
		return false;
	}

	protected override void OnClosed(EventArgs e)
	{
		foreach (var tabItem in TabControlMain.Items.OfType<TabItem>().ToList())
		{
			DisposeChatTab(tabItem);
		}
		TabControlMain.Items.Clear();
		TabControlMain.PreviewKeyDown -= TabControlMain_PreviewKeyDown;
		base.OnClosed(e);
	}
}
