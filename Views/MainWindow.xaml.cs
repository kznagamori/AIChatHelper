using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using System.Windows.Controls.Primitives;
using System.Text;
using System.Windows.Input;
using AIChatHelper.Core.Controls;
using System.Windows.Media.Imaging;
using System.ComponentModel;

namespace AIChatHelper.Views;

public partial class MainWindow : Window
{
	private readonly Core.Factory.IWindowFactory? _windowFactory;
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

		TabControlMain.PreviewKeyDown += TabControlMain_PreviewKeyDown;

		var config = settingsService.Load();
		foreach (var site in config.ChatSites)
		{
			var browser = new WebView2
			{
				Source = new Uri(site.Url)
			};

			// KeyDownイベントハンドラを追加
			browser.PreviewKeyDown += WebView_PreviewKeyDown;

			var grid = new Grid();
			grid.Children.Add(browser);

			var item = new TabItem
			{
				Header = site.Name,
				Content = grid
			};
			TabControlMain.Items.Add(item);
		}

		// AvalonEditorの参照を取得
		_avalonEditor = FindName("AvalonEditor") as AvalonTextEditor;

		// アイコン画像の参照を取得
		_templateImage = FindName("TemplateImage") as Image;
		_historyImage = FindName("HistoryImage") as Image;

		// 起動時のみOSのテーマを検出して適用
		bool isDarkMode = IsSystemInDarkMode();
		if (viewModel != null)
		{
			viewModel.IsDarkTheme = isDarkMode;
			// テーマに合わせてAvalonEditorのテーマも設定
			_avalonEditor?.ApplyTheme(isDarkMode);
		}

		// ウィンドウがロードされた後に履歴ビューを最下部にスクロール
		this.Loaded += MainWindow_Loaded;
	}

	private void MainWindow_Loaded(object sender, RoutedEventArgs e)
	{
		// ウィンドウがロードされた後に履歴ビューを最下部にスクロール
		var slackHistoryView = FindName("SlackHistoryView") as Core.Controls.SlackStyleHistoryView;
		slackHistoryView?.ScrollToLatestHistory();

		// 一度だけ実行するためにイベントハンドラを削除
		this.Loaded -= MainWindow_Loaded;
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

	// SystemEventsのイベント解除を削除
	protected override void OnClosed(EventArgs e)
	{
		base.OnClosed(e);
	}
}