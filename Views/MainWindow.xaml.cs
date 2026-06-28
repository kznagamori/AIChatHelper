using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using AIChatHelper.Core.Controls;
using AIChatHelper.Models;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace AIChatHelper.Views;

public partial class MainWindow : Window
{
	public static readonly DependencyProperty ActiveChatCommandTargetProperty =
		DependencyProperty.Register(
			nameof(ActiveChatCommandTarget),
			typeof(object),
			typeof(MainWindow),
			new PropertyMetadata(null));

	private readonly Core.Factory.IWindowFactory? _windowFactory;
	private readonly List<ChatSite> _chatSites = new();
	private readonly Dictionary<string, int> _tabNameCounts = new(StringComparer.OrdinalIgnoreCase);
	private readonly List<ChatTabViewState> _chatTabStates = new();
	private readonly Dictionary<TabItem, ChatTabViewState> _chatTabStatesByItem = new();
	private Grid? _mainGrid;
	private AvalonTextEditor? _avalonEditor;
	private Image? _templateImage;
	private Image? _historyImage;
	private ChatTabViewState? _activeChatTabState;
	private bool _isEqualContentDisplayMode;
	private bool _isUpdatingSelection;

	public object? ActiveChatCommandTarget
	{
		get => GetValue(ActiveChatCommandTargetProperty);
		private set => SetValue(ActiveChatCommandTargetProperty, value);
	}

	private sealed class ChatTabViewState
	{
		public ChatTabViewState(ChatSite site, string displayName, TabItem tabItem, Grid contentElement)
		{
			Site = site;
			DisplayName = displayName;
			TabItem = tabItem;
			ContentElement = contentElement;
		}

		public ChatSite Site { get; }
		public string DisplayName { get; }
		public TabItem TabItem { get; }
		public Grid ContentElement { get; }
		public Border? EqualColumnBorder { get; set; }
		public ContentControl? EqualContentHost { get; set; }
		public Button? EqualCloseButton { get; set; }
	}

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
		UpdateTabDisplayModeButton();

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

	private void ToggleTabDisplayModeButton_Click(object sender, RoutedEventArgs e)
	{
		_isEqualContentDisplayMode = ToggleTabDisplayModeButton.IsChecked == true;
		ApplyLeftPaneDisplayMode();
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
		browser.GotFocus += WebView_GotFocus;
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

		var tabState = new ChatTabViewState(site, displayName, item, grid);
		_chatTabStates.Add(tabState);
		_chatTabStatesByItem[item] = tabState;

		TabControlMain.Items.Add(item);
		if (_activeChatTabState == null || selectTab)
		{
			SetActiveChatTab(tabState);
		}

		if (_isEqualContentDisplayMode)
		{
			ApplyLeftPaneDisplayMode();
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
		var wasActive = _chatTabStatesByItem.TryGetValue(tabItem, out var closingState)
			&& ReferenceEquals(_activeChatTabState, closingState);
		DisposeChatTab(tabItem);
		TabControlMain.Items.Remove(tabItem);

		if (TabControlMain.Items.Count == 0)
		{
			SetActiveChatTab(null);
			ApplyLeftPaneDisplayMode();
			return;
		}

		if (wasSelected || wasActive || _activeChatTabState == null)
		{
			var nextIndex = Math.Min(closedIndex, TabControlMain.Items.Count - 1);
			if (TabControlMain.Items[nextIndex] is TabItem nextTabItem &&
				_chatTabStatesByItem.TryGetValue(nextTabItem, out var nextState))
			{
				SetActiveChatTab(nextState);
			}
		}

		ApplyLeftPaneDisplayMode();
	}

	private void DisposeChatTab(TabItem tabItem)
	{
		if (!_chatTabStatesByItem.TryGetValue(tabItem, out var tabState))
		{
			DisposeLegacyChatTab(tabItem);
			return;
		}

		DetachTabHeaderEvents(tabItem);
		DetachEqualTabColumn(tabState);

		if (ReferenceEquals(tabItem.Content, tabState.ContentElement))
		{
			tabItem.Content = null;
		}
		DetachContentElement(tabState.ContentElement);

		foreach (var webView in tabState.ContentElement.Children.OfType<WebView2>().ToList())
		{
			DetachAndDisposeWebView(tabState.ContentElement, webView);
		}

		_chatTabStatesByItem.Remove(tabItem);
		_chatTabStates.Remove(tabState);

		if (ReferenceEquals(_activeChatTabState, tabState))
		{
			_activeChatTabState = null;
			ActiveChatCommandTarget = null;
		}
	}

	private void DisposeLegacyChatTab(TabItem tabItem)
	{
		DetachTabHeaderEvents(tabItem);

		if (tabItem.Content is not Grid grid)
		{
			return;
		}

		foreach (var webView in grid.Children.OfType<WebView2>().ToList())
		{
			DetachAndDisposeWebView(grid, webView);
		}
	}

	private void DetachTabHeaderEvents(TabItem tabItem)
	{
		if (tabItem.Header is Panel headerPanel)
		{
			foreach (var closeButton in headerPanel.Children.OfType<Button>())
			{
				closeButton.PreviewMouseLeftButtonDown -= CloseTabButton_PreviewMouseLeftButtonDown;
				closeButton.Click -= CloseTabButton_Click;
				closeButton.Tag = null;
			}
		}
	}

	private void DetachAndDisposeWebView(Grid ownerGrid, WebView2 webView)
	{
		webView.PreviewKeyDown -= WebView_PreviewKeyDown;
		webView.GotFocus -= WebView_GotFocus;
		webView.CoreWebView2InitializationCompleted -= WebView_CoreWebView2InitializationCompleted;
		ownerGrid.Children.Remove(webView);
		try
		{
			webView.Dispose();
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"WebView2破棄エラー: {ex.Message}");
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
		return _chatTabStates
			.FirstOrDefault(state => state.ContentElement.Children.Contains(webView))
			?.TabItem;
	}

	public IReadOnlyList<Grid> GetChatContentElements()
	{
		return _chatTabStates.Select(state => state.ContentElement).ToList();
	}

	private void TabControlMain_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_isUpdatingSelection)
		{
			return;
		}

		if (TabControlMain.SelectedItem is TabItem tabItem &&
			_chatTabStatesByItem.TryGetValue(tabItem, out var tabState))
		{
			SetActiveChatTab(tabState, updateSelection: false);
		}
	}

	private void WebView_GotFocus(object sender, RoutedEventArgs e)
	{
		if (sender is WebView2 webView)
		{
			SetActiveChatTab(FindTabStateForWebView(webView));
		}
	}

	private ChatTabViewState? FindTabStateForWebView(WebView2 webView)
	{
		return _chatTabStates.FirstOrDefault(state => state.ContentElement.Children.Contains(webView));
	}

	private void SetActiveChatTab(ChatTabViewState? tabState, bool updateSelection = true)
	{
		if (tabState != null && !_chatTabStates.Contains(tabState))
		{
			tabState = null;
		}

		_activeChatTabState = tabState;
		ActiveChatCommandTarget = tabState?.ContentElement;

		if (updateSelection)
		{
			_isUpdatingSelection = true;
			try
			{
				TabControlMain.SelectedItem = tabState?.TabItem;
			}
			finally
			{
				_isUpdatingSelection = false;
			}
		}

		UpdateEqualColumnActiveStates();
	}

	private void EnsureActiveChatTab()
	{
		if (_activeChatTabState != null && _chatTabStates.Contains(_activeChatTabState))
		{
			ActiveChatCommandTarget = _activeChatTabState.ContentElement;
			return;
		}

		if (TabControlMain.SelectedItem is TabItem selectedTabItem &&
			_chatTabStatesByItem.TryGetValue(selectedTabItem, out var selectedState))
		{
			SetActiveChatTab(selectedState);
			return;
		}

		SetActiveChatTab(_chatTabStates.FirstOrDefault());
	}

	private void ApplyLeftPaneDisplayMode()
	{
		EnsureActiveChatTab();
		UpdateTabDisplayModeButton();

		if (_isEqualContentDisplayMode)
		{
			ShowEqualContentDisplay();
			return;
		}

		ShowNormalTabDisplay();
	}

	private void ShowEqualContentDisplay()
	{
		ClearEqualContentColumns();
		EqualTabContentHost.ColumnDefinitions.Clear();

		foreach (var tabState in _chatTabStates)
		{
			if (ReferenceEquals(tabState.TabItem.Content, tabState.ContentElement))
			{
				tabState.TabItem.Content = null;
			}
			DetachContentElement(tabState.ContentElement);

			EqualTabContentHost.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(1, GridUnitType.Star)
			});

			var column = CreateEqualTabColumn(tabState);
			Grid.SetColumn(column, EqualTabContentHost.ColumnDefinitions.Count - 1);
			EqualTabContentHost.Children.Add(column);
		}

		TabControlMain.Visibility = Visibility.Collapsed;
		EqualTabContentHost.Visibility = Visibility.Visible;
		UpdateEqualColumnActiveStates();
	}

	private void ShowNormalTabDisplay()
	{
		ClearEqualContentColumns();

		foreach (var tabState in _chatTabStates)
		{
			if (!ReferenceEquals(tabState.TabItem.Content, tabState.ContentElement))
			{
				DetachContentElement(tabState.ContentElement);
				tabState.TabItem.Content = tabState.ContentElement;
			}
		}

		EqualTabContentHost.Visibility = Visibility.Collapsed;
		TabControlMain.Visibility = Visibility.Visible;

		if (_activeChatTabState != null)
		{
			SetActiveChatTab(_activeChatTabState);
		}
	}

	private Border CreateEqualTabColumn(ChatTabViewState tabState)
	{
		var border = new Border
		{
			BorderThickness = new Thickness(1),
			BorderBrush = GetBrushResource("MaterialDesign.Brush.Divider", Brushes.Gray),
			Background = Brushes.Transparent,
			Tag = tabState
		};
		border.MouseLeftButtonDown += EqualTabColumn_MouseLeftButtonDown;

		var columnGrid = new Grid
		{
			Tag = tabState
		};
		columnGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		columnGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

		var headerGrid = new Grid
		{
			MinHeight = 36,
			Margin = new Thickness(4, 2, 4, 2),
			Tag = tabState
		};
		headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

		var title = new TextBlock
		{
			Text = tabState.DisplayName,
			TextTrimming = TextTrimming.CharacterEllipsis,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(6, 0, 4, 0)
		};
		Grid.SetColumn(title, 0);

		var closeButton = new Button
		{
			Width = 28,
			Height = 28,
			Padding = new Thickness(0),
			Focusable = false,
			ToolTip = "タブを閉じる",
			Tag = tabState,
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
		closeButton.Click += EqualTabCloseButton_Click;
		Grid.SetColumn(closeButton, 1);

		headerGrid.Children.Add(title);
		headerGrid.Children.Add(closeButton);
		Grid.SetRow(headerGrid, 0);

		var contentHost = new ContentControl
		{
			Content = tabState.ContentElement,
			HorizontalContentAlignment = HorizontalAlignment.Stretch,
			VerticalContentAlignment = VerticalAlignment.Stretch
		};
		Grid.SetRow(contentHost, 1);

		columnGrid.Children.Add(headerGrid);
		columnGrid.Children.Add(contentHost);
		border.Child = columnGrid;

		tabState.EqualColumnBorder = border;
		tabState.EqualContentHost = contentHost;
		tabState.EqualCloseButton = closeButton;

		return border;
	}

	private void EqualTabColumn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (sender is FrameworkElement { Tag: ChatTabViewState tabState })
		{
			SetActiveChatTab(tabState);
		}
	}

	private void EqualTabCloseButton_Click(object sender, RoutedEventArgs e)
	{
		e.Handled = true;
		if (sender is Button { Tag: ChatTabViewState tabState })
		{
			CloseChatTab(tabState.TabItem);
		}
	}

	private void ClearEqualContentColumns()
	{
		foreach (var tabState in _chatTabStates)
		{
			DetachEqualTabColumn(tabState);
		}

		EqualTabContentHost.Children.Clear();
		EqualTabContentHost.ColumnDefinitions.Clear();
	}

	private void DetachEqualTabColumn(ChatTabViewState tabState)
	{
		if (tabState.EqualCloseButton != null)
		{
			tabState.EqualCloseButton.Click -= EqualTabCloseButton_Click;
			tabState.EqualCloseButton.Tag = null;
		}

		if (tabState.EqualColumnBorder != null)
		{
			tabState.EqualColumnBorder.MouseLeftButtonDown -= EqualTabColumn_MouseLeftButtonDown;
			if (tabState.EqualColumnBorder.Parent is Panel parentPanel)
			{
				parentPanel.Children.Remove(tabState.EqualColumnBorder);
			}
			tabState.EqualColumnBorder.Tag = null;
			tabState.EqualColumnBorder.Child = null;
		}

		if (tabState.EqualContentHost != null)
		{
			if (ReferenceEquals(tabState.EqualContentHost.Content, tabState.ContentElement))
			{
				tabState.EqualContentHost.Content = null;
			}
			tabState.EqualContentHost = null;
		}

		tabState.EqualColumnBorder = null;
		tabState.EqualCloseButton = null;
	}

	private void DetachContentElement(FrameworkElement contentElement)
	{
		if (contentElement.Parent is Panel panel)
		{
			panel.Children.Remove(contentElement);
			return;
		}

		if (contentElement.Parent is ContentControl contentControl &&
			ReferenceEquals(contentControl.Content, contentElement))
		{
			contentControl.Content = null;
			return;
		}

		if (contentElement.Parent is ContentPresenter contentPresenter &&
			ReferenceEquals(contentPresenter.Content, contentElement))
		{
			contentPresenter.Content = null;
			return;
		}

		if (contentElement.Parent is Decorator decorator &&
			ReferenceEquals(decorator.Child, contentElement))
		{
			decorator.Child = null;
			return;
		}

		var logicalParent = LogicalTreeHelper.GetParent(contentElement);
		if (logicalParent is ContentControl logicalContentControl &&
			ReferenceEquals(logicalContentControl.Content, contentElement))
		{
			logicalContentControl.Content = null;
		}
		else if (logicalParent is ContentPresenter logicalContentPresenter &&
			ReferenceEquals(logicalContentPresenter.Content, contentElement))
		{
			logicalContentPresenter.Content = null;
		}
		else if (logicalParent is TabItem tabItem &&
			ReferenceEquals(tabItem.Content, contentElement))
		{
			tabItem.Content = null;
		}
	}

	private void UpdateEqualColumnActiveStates()
	{
		foreach (var tabState in _chatTabStates)
		{
			if (tabState.EqualColumnBorder == null)
			{
				continue;
			}

			var isActive = ReferenceEquals(tabState, _activeChatTabState);
			tabState.EqualColumnBorder.BorderThickness = isActive
				? new Thickness(2)
				: new Thickness(1);
			tabState.EqualColumnBorder.BorderBrush = isActive
				? GetBrushResource("MaterialDesign.Brush.Primary", SystemColors.HighlightBrush)
				: GetBrushResource("MaterialDesign.Brush.Divider", Brushes.Gray);
		}
	}

	private void UpdateTabDisplayModeButton()
	{
		if (ToggleTabDisplayModeButton == null || ToggleTabDisplayModeIcon == null)
		{
			return;
		}

		ToggleTabDisplayModeButton.IsChecked = _isEqualContentDisplayMode;
		ToggleTabDisplayModeButton.ToolTip = _isEqualContentDisplayMode
			? "通常のタブ表示に戻す"
			: "すべてのタブを等分表示";
		ToggleTabDisplayModeIcon.Kind = _isEqualContentDisplayMode
			? PackIconKind.ViewAgendaOutline
			: PackIconKind.ViewColumnOutline;
		ToggleTabDisplayModeButton.Foreground = _isEqualContentDisplayMode
			? GetBrushResource("MaterialDesign.Brush.Primary", SystemColors.HighlightBrush)
			: GetBrushResource("MaterialDesign.Brush.Foreground", SystemColors.ControlTextBrush);
	}

	private Brush GetBrushResource(string resourceKey, Brush fallback)
	{
		return TryFindResource(resourceKey) as Brush ?? fallback;
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

		if (_isEqualContentDisplayMode)
		{
			ApplyLeftPaneDisplayMode();
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
		foreach (var tabState in _chatTabStates.ToList())
		{
			DisposeChatTab(tabState.TabItem);
		}
		ClearEqualContentColumns();
		TabControlMain.Items.Clear();
		TabControlMain.PreviewKeyDown -= TabControlMain_PreviewKeyDown;
		ToggleTabDisplayModeButton.Click -= ToggleTabDisplayModeButton_Click;
		TabControlMain.SelectionChanged -= TabControlMain_SelectionChanged;
		base.OnClosed(e);
	}
}
