using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
	private readonly Core.Services.ISettingsService? _settingsService;
	private readonly List<ChatSite> _chatSites = new();
	private readonly Dictionary<string, int> _tabNameCounts = new(StringComparer.OrdinalIgnoreCase);
	private readonly List<ChatTabViewState> _chatTabStates = new();
	private readonly Dictionary<TabItem, ChatTabViewState> _chatTabStatesByItem = new();
	private readonly DispatcherTimer _uiStateSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
	private Grid? _mainGrid;
	private AvalonTextEditor? _avalonEditor;
	private ViewModels.MainWindowViewModel? _mainViewModel;
	private ChatTabViewState? _activeChatTabState;
	private bool _isEqualContentDisplayMode;
	private bool _isUpdatingSelection;
	private bool _suppressUiStateSave = true;
	private bool _isSettingsWindowOpen;
	private bool _restoreWindowPosition;

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
		_uiStateSaveTimer.Tick += UiStateSaveTimer_Tick;
		SizeChanged += MainWindow_SizeOrPositionChanged;
		LocationChanged += MainWindow_LocationChanged;
		StateChanged += MainWindow_StateChanged;
	}

	public MainWindow(Core.Factory.IWindowFactory windowFactory,
					ViewModels.MainWindowViewModel viewModel,
					Core.Services.ISettingsService settingsService)
		: this()
	{
		DataContext = viewModel;
		_windowFactory = windowFactory;
		_settingsService = settingsService;
		_mainViewModel = viewModel;

		// メインのGridを参照として保持
		_mainGrid = (Grid)Content;

		var config = settingsService.Load();
		_restoreWindowPosition = config.Config.UiState?.RestoreWindowPosition ?? false;
		RestoreWindowPlacement(config.Config.UiState);

		// 起動時のみOSのテーマを検出して、WebView2初期化前にViewModelへ反映
		bool isDarkMode = config.Config.UiState?.IsDarkTheme ?? IsSystemInDarkMode();
		viewModel.IsDarkTheme = isDarkMode;
		viewModel.UiStateChanged += MainViewModel_UiStateChanged;

		TabControlMain.PreviewKeyDown += TabControlMain_PreviewKeyDown;
		UpdateTabDisplayModeButton();

		_chatSites.AddRange(config.ChatSites);
		ChatSiteComboBox.ItemsSource = _chatSites;
		AddChatTabButton.IsEnabled = _chatSites.Count > 0;
		if (_chatSites.Count > 0)
		{
			ChatSiteComboBox.SelectedIndex = 0;
		}

		RestoreLeftPaneTabs(config.Config.UiState, _chatSites);

		// AvalonEditorの参照を取得
		_avalonEditor = FindName("AvalonEditor") as AvalonTextEditor;

		// テーマに合わせてAvalonEditorのテーマも設定
		_avalonEditor?.ApplyTheme(isDarkMode);

		// ウィンドウがロードされた後に履歴ビューを最下部にスクロール
		Loaded += MainWindow_Loaded;
		_suppressUiStateSave = false;
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
		_isEqualContentDisplayMode = !_isEqualContentDisplayMode;
		ApplyLeftPaneDisplayMode();
	}

	private void CreateChatTab(ChatSite site, bool selectTab, string? displayNameOverride = null)
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

		var displayName = string.IsNullOrWhiteSpace(displayNameOverride)
			? GetNextTabDisplayName(site)
			: RegisterRestoredTabDisplayName(site, displayNameOverride);
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

		RequestDebouncedUiStateSave();
	}

	private void RestoreLeftPaneTabs(UiStateSettings? uiState, IReadOnlyList<ChatSite> chatSites)
	{
		var restoredAny = false;
		if (uiState?.LeftPaneTabs is { Count: > 0 } savedTabs)
		{
			foreach (var savedTab in savedTabs)
			{
				var site = FindMatchingChatSite(savedTab, chatSites);
				if (site == null)
				{
					continue;
				}

				CreateChatTab(site, selectTab: false, savedTab.DisplayName);
				restoredAny = true;
			}
		}

		if (!restoredAny)
		{
			foreach (var site in chatSites)
			{
				CreateChatTab(site, selectTab: false);
			}
		}

		if (_chatTabStates.Count == 0)
		{
			SetActiveChatTab(null);
			return;
		}

		var activeIndex = Math.Clamp(uiState?.ActiveLeftTabIndex ?? 0, 0, _chatTabStates.Count - 1);
		SetActiveChatTab(_chatTabStates[activeIndex]);
	}

	private void RestoreWindowPlacement(UiStateSettings? uiState)
	{
		if (uiState == null)
		{
			return;
		}

		if (IsValidWindowDimension(uiState.WindowWidth))
		{
			Width = Math.Max(MinWidth, uiState.WindowWidth!.Value);
		}

		if (IsValidWindowDimension(uiState.WindowHeight))
		{
			Height = Math.Max(MinHeight, uiState.WindowHeight!.Value);
		}

		if (!uiState.RestoreWindowPosition ||
			!IsFinite(uiState.WindowLeft) ||
			!IsFinite(uiState.WindowTop))
		{
			return;
		}

		var left = uiState.WindowLeft!.Value;
		var top = uiState.WindowTop!.Value;
		if (!IsWindowPositionVisible(left, top, Width, Height))
		{
			return;
		}

		WindowStartupLocation = WindowStartupLocation.Manual;
		Left = left;
		Top = top;
	}

	private static bool IsWindowPositionVisible(double left, double top, double width, double height)
	{
		var screenBounds = new Rect(
			SystemParameters.VirtualScreenLeft,
			SystemParameters.VirtualScreenTop,
			SystemParameters.VirtualScreenWidth,
			SystemParameters.VirtualScreenHeight);
		var windowBounds = new Rect(left, top, Math.Max(100, width), Math.Max(100, height));

		return screenBounds.IntersectsWith(windowBounds);
	}

	private static ChatSite? FindMatchingChatSite(LeftPaneTabState savedTab, IReadOnlyList<ChatSite> chatSites)
	{
		return chatSites.FirstOrDefault(site =>
				string.Equals(site.Name, savedTab.SiteName, StringComparison.OrdinalIgnoreCase) &&
				AreSameUrl(site.Url, savedTab.Url))
			?? chatSites.FirstOrDefault(site => AreSameUrl(site.Url, savedTab.Url));
	}

	private static bool AreSameUrl(string? left, string? right)
	{
		return string.Equals(NormalizeUrlForState(left), NormalizeUrlForState(right), StringComparison.OrdinalIgnoreCase);
	}

	private static string NormalizeUrlForState(string? value)
	{
		return (value ?? string.Empty).Trim().TrimEnd('/');
	}

	private bool TryCreateSiteUri(ChatSite site, out Uri uri)
	{
		uri = null!;
		return !string.IsNullOrWhiteSpace(site.Url)
			&& Uri.TryCreate(site.Url, UriKind.Absolute, out uri!);
	}

	private string GetNextTabDisplayName(ChatSite site)
	{
		var baseName = GetTabBaseName(site);
		var nextCount = _tabNameCounts.TryGetValue(baseName, out var currentCount)
			? currentCount + 1
			: 1;
		_tabNameCounts[baseName] = nextCount;

		return nextCount == 1 ? baseName : $"{baseName} ({nextCount})";
	}

	private string RegisterRestoredTabDisplayName(ChatSite site, string displayName)
	{
		var baseName = GetTabBaseName(site);
		var restoredDisplayName = displayName.Trim();
		var restoredCount = GetRestoredDisplayNameCount(baseName, restoredDisplayName);
		if (_tabNameCounts.TryGetValue(baseName, out var currentCount))
		{
			_tabNameCounts[baseName] = Math.Max(currentCount, restoredCount);
		}
		else
		{
			_tabNameCounts[baseName] = restoredCount;
		}

		return restoredDisplayName;
	}

	private static string GetTabBaseName(ChatSite site)
	{
		return string.IsNullOrWhiteSpace(site.Name) ? "無題" : site.Name.Trim();
	}

	private static int GetRestoredDisplayNameCount(string baseName, string displayName)
	{
		if (string.Equals(baseName, displayName, StringComparison.OrdinalIgnoreCase))
		{
			return 1;
		}

		var prefix = baseName + " (";
		if (displayName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
			displayName.EndsWith(")", StringComparison.Ordinal) &&
			int.TryParse(displayName[prefix.Length..^1], out var parsedCount) &&
			parsedCount > 0)
		{
			return parsedCount;
		}

		return 1;
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
		RequestDebouncedUiStateSave();
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
		RequestDebouncedUiStateSave();
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
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Stretch,
			Tag = tabState
		};
		border.MouseLeftButtonDown += EqualTabColumn_MouseLeftButtonDown;

		var columnGrid = new Grid
		{
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Stretch,
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
			VerticalContentAlignment = VerticalAlignment.Stretch,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Stretch
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
		if (ToggleTabDisplayModeButton == null || ToggleTabDisplayModeIcon == null || ToggleTabDisplayModeText == null)
		{
			return;
		}

		ToggleTabDisplayModeButton.ToolTip = _isEqualContentDisplayMode
			? "通常のタブ表示に戻す"
			: "すべてのタブを等分表示";
		ToggleTabDisplayModeIcon.Kind = _isEqualContentDisplayMode
			? PackIconKind.ViewAgendaOutline
			: PackIconKind.ViewColumnOutline;
		ToggleTabDisplayModeText.Text = _isEqualContentDisplayMode ? "タブ表示" : "等分表示";
		ToggleTabDisplayModeButton.Foreground = _isEqualContentDisplayMode
			? GetBrushResource("MaterialDesign.Brush.Primary.Foreground", SystemColors.HighlightTextBrush)
			: GetBrushResource("MaterialDesign.Brush.Foreground", SystemColors.ControlTextBrush);
		ToggleTabDisplayModeButton.Background = _isEqualContentDisplayMode
			? GetBrushResource("MaterialDesign.Brush.Primary", SystemColors.HighlightBrush)
			: Brushes.Transparent;
		ToggleTabDisplayModeButton.BorderBrush = _isEqualContentDisplayMode
			? GetBrushResource("MaterialDesign.Brush.Primary", SystemColors.HighlightBrush)
			: GetBrushResource("MaterialDesign.Brush.Divider", Brushes.Gray);
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

	private void MainWindow_SizeOrPositionChanged(object sender, SizeChangedEventArgs e)
	{
		RequestDebouncedUiStateSave();
	}

	private void MainWindow_LocationChanged(object? sender, EventArgs e)
	{
		RequestDebouncedUiStateSave();
	}

	private void MainWindow_StateChanged(object? sender, EventArgs e)
	{
		RequestDebouncedUiStateSave();
	}

	private void MainViewModel_UiStateChanged(object? sender, EventArgs e)
	{
		RequestDebouncedUiStateSave();
	}

	private void UiStateSaveTimer_Tick(object? sender, EventArgs e)
	{
		_uiStateSaveTimer.Stop();
		SaveCurrentUiStateImmediately();
	}

	private void RequestDebouncedUiStateSave()
	{
		if (_suppressUiStateSave || _isSettingsWindowOpen)
		{
			return;
		}

		_uiStateSaveTimer.Stop();
		_uiStateSaveTimer.Start();
	}

	private void SaveCurrentUiStateImmediately(bool showErrorMessage = false)
	{
		if (_settingsService == null)
		{
			return;
		}

		try
		{
			_uiStateSaveTimer.Stop();
			_settingsService.SaveUiState(CaptureUiState());
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"UI状態の保存に失敗しました: {ex}");
			if (showErrorMessage)
			{
				MessageBox.Show(
					$"現在の状態を settings.toml に保存できませんでした。\n{ex.Message}",
					"設定保存エラー",
					MessageBoxButton.OK,
					MessageBoxImage.Warning);
			}
		}
	}

	private UiStateSettings CaptureUiState()
	{
		var viewModel = _mainViewModel ?? DataContext as ViewModels.MainWindowViewModel;
		var activeIndex = _activeChatTabState == null
			? 0
			: Math.Max(0, _chatTabStates.IndexOf(_activeChatTabState));
		var windowBounds = GetRestorableWindowBounds();

		return new UiStateSettings
		{
			ActiveLeftTabIndex = activeIndex,
			PaneDisplayMode = viewModel?.GetPaneDisplayMode() ?? "TwoPane",
			RightPaneSelectedTab = NormalizeRightPaneSelectedTab(viewModel?.RightPaneSelectedTab),
			IsDarkTheme = viewModel?.IsDarkTheme,
			ExecuteAfterSend = viewModel?.ExecuteAfterSend,
			WindowWidth = IsValidWindowDimension(windowBounds.Width) ? windowBounds.Width : null,
			WindowHeight = IsValidWindowDimension(windowBounds.Height) ? windowBounds.Height : null,
			WindowLeft = IsFinite(windowBounds.Left) ? windowBounds.Left : null,
			WindowTop = IsFinite(windowBounds.Top) ? windowBounds.Top : null,
			RestoreWindowPosition = _restoreWindowPosition,
			LeftPaneTabs = _chatTabStates.Select(tabState => new LeftPaneTabState
			{
				SiteName = tabState.Site.Name,
				Url = tabState.Site.Url,
				DisplayName = tabState.DisplayName
			}).ToList()
		};
	}

	private Rect GetRestorableWindowBounds()
	{
		if (WindowState == WindowState.Normal)
		{
			return new Rect(Left, Top, Width, Height);
		}

		return RestoreBounds;
	}

	private static bool IsValidWindowDimension(double? value)
	{
		return value.HasValue && IsValidWindowDimension(value.Value);
	}

	private static bool IsValidWindowDimension(double value)
	{
		return IsFinite(value) && value > 0;
	}

	private static bool IsFinite(double? value)
	{
		return value.HasValue && IsFinite(value.Value);
	}

	private static bool IsFinite(double value)
	{
		return !double.IsNaN(value) && !double.IsInfinity(value);
	}

	private static string NormalizeRightPaneSelectedTab(string? value)
	{
		return string.Equals(value, "Template", StringComparison.OrdinalIgnoreCase)
			? "Template"
			: "History";
	}

	private void OpenSettingsButton_Click(object sender, RoutedEventArgs e)
	{
		SaveCurrentUiStateImmediately(showErrorMessage: true);

		if (_windowFactory == null)
		{
			return;
		}

		_isSettingsWindowOpen = true;
		_uiStateSaveTimer.Stop();
		try
		{
			var dlg = _windowFactory.CreateWindow<SettingsWindow>();
			dlg.Owner = this;
			dlg.ShowDialog();
		}
		finally
		{
			_isSettingsWindowOpen = false;
			ReloadUiStateOptions();
			SaveCurrentUiStateImmediately();
		}
	}

	private void ReloadUiStateOptions()
	{
		if (_settingsService == null)
		{
			return;
		}

		try
		{
			var uiState = _settingsService.Load().Config.UiState;
			_restoreWindowPosition = uiState.RestoreWindowPosition;
			_mainViewModel?.ApplyPaneDisplayMode(uiState.PaneDisplayMode);
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"UI状態設定の再読み込みに失敗しました: {ex.Message}");
		}
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

	private void TemplateTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
	{
		if (DataContext is ViewModels.MainWindowViewModel viewModel)
		{
			viewModel.SelectedTemplateNode = e.NewValue as TemplateTreeNode;
		}
	}

	private void TemplateTreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
	{
		if (DataContext is not ViewModels.MainWindowViewModel viewModel)
		{
			return;
		}

		var treeViewItem = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
		if (treeViewItem?.DataContext is not TemplateTreeNode node || node.IsDirectory)
		{
			return;
		}

		if (viewModel.ApplyTemplateNodeCommand.CanExecute(node))
		{
			viewModel.ApplyTemplateNodeCommand.Execute(node);
			e.Handled = true;
		}
	}

	private void TemplateTreeView_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key != Key.Enter || DataContext is not ViewModels.MainWindowViewModel viewModel)
		{
			return;
		}

		var node = viewModel.SelectedTemplateNode;
		if (node == null || node.IsDirectory)
		{
			return;
		}

		if (viewModel.ApplyTemplateNodeCommand.CanExecute(node))
		{
			viewModel.ApplyTemplateNodeCommand.Execute(node);
			e.Handled = true;
		}
	}

	private static T? FindAncestor<T>(DependencyObject? current)
		where T : DependencyObject
	{
		while (current != null)
		{
			if (current is T target)
			{
				return target;
			}

			current = VisualTreeHelper.GetParent(current);
		}

		return null;
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
		SaveCurrentUiStateImmediately();
		_suppressUiStateSave = true;
		_uiStateSaveTimer.Stop();

		if (_mainViewModel != null)
		{
			_mainViewModel.UiStateChanged -= MainViewModel_UiStateChanged;
		}

		foreach (var tabState in _chatTabStates.ToList())
		{
			DisposeChatTab(tabState.TabItem);
		}
		ClearEqualContentColumns();
		TabControlMain.Items.Clear();
		TabControlMain.PreviewKeyDown -= TabControlMain_PreviewKeyDown;
		_uiStateSaveTimer.Tick -= UiStateSaveTimer_Tick;
		ToggleTabDisplayModeButton.Click -= ToggleTabDisplayModeButton_Click;
		TabControlMain.SelectionChanged -= TabControlMain_SelectionChanged;
		SizeChanged -= MainWindow_SizeOrPositionChanged;
		LocationChanged -= MainWindow_LocationChanged;
		StateChanged -= MainWindow_StateChanged;
		base.OnClosed(e);
	}
}
