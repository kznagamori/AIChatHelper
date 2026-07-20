// SettingsWindowViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using AIChatHelper.Core.Services;
using AIChatHelper.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using AppThemeMode = AIChatHelper.Models.ThemeMode;

namespace AIChatHelper.ViewModels;

public partial class SettingsWindowViewModel : ObservableObject
{
	private const string DefaultRepositoryUrl = "https://github.com/kznagamori/AIChatHelper";
	private const string DefaultSettingsDocumentationUrl = "https://github.com/kznagamori/AIChatHelper/blob/main/docs/settings-toml.md";

	private readonly ISettingsService _settingsService;

	[ObservableProperty]
	private string _settingsFilePath = string.Empty;

	[ObservableProperty]
	private string _statusMessage = string.Empty;

	[ObservableProperty]
	private string _repositoryUrl = DefaultRepositoryUrl;

	[ObservableProperty]
	private string _settingsDocumentationUrl = DefaultSettingsDocumentationUrl;

	[ObservableProperty]
	private ChatSiteSettingsItem? _selectedChatSite;

	[ObservableProperty]
	private EditableStringItem? _selectedInputSelector;

	[ObservableProperty]
	private bool _confirmTemplateOverwrite;

	[ObservableProperty]
	private bool _confirmHistoryOverwrite;

	[ObservableProperty]
	private bool _insertTemplateTextOnClear;

	[ObservableProperty]
	private string _templateTextForEditor = string.Empty;

	[ObservableProperty]
	private string _templateDirectory = string.Empty;

	[ObservableProperty]
	private int _executionTimeoutMs;

	[ObservableProperty]
	private int _postInputDelayMs;

	[ObservableProperty]
	private int _retryIntervalMs;

	[ObservableProperty]
	private bool _enableDomAnalysisLog;

	[ObservableProperty]
	private string _unsupportedServiceBehavior = "InputOnly";

	[ObservableProperty]
	private ServiceExecutorSettingsItem? _selectedServiceExecutor;

	[ObservableProperty]
	private int _activeLeftTabIndex;

	[ObservableProperty]
	private string _paneDisplayMode = "TwoPane";

	[ObservableProperty]
	private string _rightPaneSelectedTab = "History";

	/// <summary>
	/// 基本設定と詳細設定で共有するテーマモードを取得または設定します。
	/// </summary>
	[ObservableProperty]
	private AppThemeMode _selectedThemeMode = AppThemeMode.System;

	[ObservableProperty]
	private bool _uiStateExecuteAfterSend;

	[ObservableProperty]
	private double? _uiStateWindowWidth;

	[ObservableProperty]
	private double? _uiStateWindowHeight;

	[ObservableProperty]
	private double? _uiStateWindowLeft;

	[ObservableProperty]
	private double? _uiStateWindowTop;

	[ObservableProperty]
	private bool _uiStateRestoreWindowPosition;

	// タブ URL の保存と初期タブ復元は独立設定とし、実行時は初期タブ復元を優先する。
	[ObservableProperty]
	private bool _saveAndRestoreTabUrls;

	[ObservableProperty]
	private bool _alwaysRestoreInitialTabs;

	public ObservableCollection<ChatSiteSettingsItem> ChatSites { get; } = new();
	public ObservableCollection<EditableStringItem> InputSelectors { get; } = new();
	public ObservableCollection<ServiceExecutorSettingsItem> ServiceExecutors { get; } = new();
	public ObservableCollection<LeftPaneTabStateItem> LeftPaneTabs { get; } = new();

	public IReadOnlyList<string> UnsupportedServiceBehaviorOptions { get; } = new[] { "InputOnly", "ShowWarning" };
	public IReadOnlyList<string> KeyboardFallbackOptions { get; } = new[] { "None", "Enter", "CtrlEnter" };
	public IReadOnlyList<string> PaneDisplayModeOptions { get; } = new[] { "TwoPane", "LeftPane", "RightPane" };
	public IReadOnlyList<string> RightPaneSelectedTabOptions { get; } = new[] { "History", "Template" };

	/// <summary>
	/// 設定画面で選択できるテーマモードを表示順で取得します。
	/// </summary>
	public IReadOnlyList<ThemeModeOption> ThemeModeOptions { get; } = new[]
	{
		new ThemeModeOption(AppThemeMode.System, "Windows の設定に合わせる"),
		new ThemeModeOption(AppThemeMode.Light, "ライト"),
		new ThemeModeOption(AppThemeMode.Dark, "ダーク")
	};

	public SettingsWindowViewModel(ISettingsService settingsService)
	{
		_settingsService = settingsService;
		SettingsFilePath = settingsService.SettingsFilePath;
		RepositoryUrl = GetAssemblyMetadata("PackageProjectUrl") ?? DefaultRepositoryUrl;
		SettingsDocumentationUrl = DefaultSettingsDocumentationUrl;
		LoadSettings();
	}

	[RelayCommand]
	private void Save()
	{
		try
		{
			_settingsService.Save(BuildConfig());
			StatusMessage = "settings.toml を保存しました。一部設定は再起動後に反映されます。";
		}
		catch (Exception ex)
		{
			// CurrentUrl を例外本文経由で画面へ露出させない。
			StatusMessage = $"保存できません。settings.toml の内容と書き込み権限を確認してください。({ex.GetType().Name})";
		}
	}

	[RelayCommand]
	private void Reload()
	{
		LoadSettings();
	}

	[RelayCommand]
	private void AddChatSite()
	{
		var item = new ChatSiteSettingsItem
		{
			Name = "New Site",
			Url = "https://example.com/"
		};
		ChatSites.Add(item);
		SelectedChatSite = item;
	}

	[RelayCommand]
	private void RemoveChatSite()
	{
		RemoveSelectedItem(ChatSites, SelectedChatSite, item => SelectedChatSite = item);
	}

	[RelayCommand]
	private void MoveChatSiteUp()
	{
		MoveSelectedItem(ChatSites, SelectedChatSite, -1);
	}

	[RelayCommand]
	private void MoveChatSiteDown()
	{
		MoveSelectedItem(ChatSites, SelectedChatSite, 1);
	}

	[RelayCommand]
	private void AddInputSelector()
	{
		var item = new EditableStringItem { Value = "textarea" };
		InputSelectors.Add(item);
		SelectedInputSelector = item;
	}

	[RelayCommand]
	private void RemoveInputSelector()
	{
		RemoveSelectedItem(InputSelectors, SelectedInputSelector, item => SelectedInputSelector = item);
	}

	[RelayCommand]
	private void MoveInputSelectorUp()
	{
		MoveSelectedItem(InputSelectors, SelectedInputSelector, -1);
	}

	[RelayCommand]
	private void MoveInputSelectorDown()
	{
		MoveSelectedItem(InputSelectors, SelectedInputSelector, 1);
	}

	[RelayCommand]
	private void BrowseTemplateDirectory()
	{
		var initialDirectory = Directory.Exists(TemplateDirectory)
			? TemplateDirectory
			: AppDomain.CurrentDomain.BaseDirectory;

		var dialog = new OpenFolderDialog
		{
			Title = "テンプレートフォルダーを選択",
			InitialDirectory = initialDirectory
		};

		if (dialog.ShowDialog() == true)
		{
			TemplateDirectory = dialog.FolderName;
		}
	}

	[RelayCommand]
	private void AddServiceExecutor()
	{
		var item = new ServiceExecutorSettingsItem
		{
			ServiceName = "New Service",
			KeyboardFallback = "None"
		};
		item.UrlPatterns.Add(new EditableStringItem { Value = "example.com" });
		item.SubmitButtonSelectors.Add(new EditableStringItem { Value = "button[aria-label*=\"Send\"]" });

		ServiceExecutors.Add(item);
		SelectedServiceExecutor = item;
	}

	[RelayCommand]
	private void RemoveServiceExecutor()
	{
		RemoveSelectedItem(ServiceExecutors, SelectedServiceExecutor, item => SelectedServiceExecutor = item);
	}

	[RelayCommand]
	private void MoveServiceExecutorUp()
	{
		MoveSelectedItem(ServiceExecutors, SelectedServiceExecutor, -1);
	}

	[RelayCommand]
	private void MoveServiceExecutorDown()
	{
		MoveSelectedItem(ServiceExecutors, SelectedServiceExecutor, 1);
	}

	[RelayCommand]
	private void AddUrlPattern()
	{
		if (SelectedServiceExecutor == null)
		{
			return;
		}

		var item = new EditableStringItem { Value = "example.com" };
		SelectedServiceExecutor.UrlPatterns.Add(item);
		SelectedServiceExecutor.SelectedUrlPattern = item;
	}

	[RelayCommand]
	private void RemoveUrlPattern()
	{
		if (SelectedServiceExecutor == null)
		{
			return;
		}

		RemoveSelectedItem(
			SelectedServiceExecutor.UrlPatterns,
			SelectedServiceExecutor.SelectedUrlPattern,
			item => SelectedServiceExecutor.SelectedUrlPattern = item);
	}

	[RelayCommand]
	private void MoveUrlPatternUp()
	{
		if (SelectedServiceExecutor != null)
		{
			MoveSelectedItem(SelectedServiceExecutor.UrlPatterns, SelectedServiceExecutor.SelectedUrlPattern, -1);
		}
	}

	[RelayCommand]
	private void MoveUrlPatternDown()
	{
		if (SelectedServiceExecutor != null)
		{
			MoveSelectedItem(SelectedServiceExecutor.UrlPatterns, SelectedServiceExecutor.SelectedUrlPattern, 1);
		}
	}

	[RelayCommand]
	private void AddSubmitButtonSelector()
	{
		if (SelectedServiceExecutor == null)
		{
			return;
		}

		var item = new EditableStringItem { Value = "button[aria-label*=\"Send\"]" };
		SelectedServiceExecutor.SubmitButtonSelectors.Add(item);
		SelectedServiceExecutor.SelectedSubmitButtonSelector = item;
	}

	[RelayCommand]
	private void RemoveSubmitButtonSelector()
	{
		if (SelectedServiceExecutor == null)
		{
			return;
		}

		RemoveSelectedItem(
			SelectedServiceExecutor.SubmitButtonSelectors,
			SelectedServiceExecutor.SelectedSubmitButtonSelector,
			item => SelectedServiceExecutor.SelectedSubmitButtonSelector = item);
	}

	[RelayCommand]
	private void MoveSubmitButtonSelectorUp()
	{
		if (SelectedServiceExecutor != null)
		{
			MoveSelectedItem(SelectedServiceExecutor.SubmitButtonSelectors, SelectedServiceExecutor.SelectedSubmitButtonSelector, -1);
		}
	}

	[RelayCommand]
	private void MoveSubmitButtonSelectorDown()
	{
		if (SelectedServiceExecutor != null)
		{
			MoveSelectedItem(SelectedServiceExecutor.SubmitButtonSelectors, SelectedServiceExecutor.SelectedSubmitButtonSelector, 1);
		}
	}

	[RelayCommand]
	private void OpenRepositoryUrl()
	{
		OpenUrl(RepositoryUrl);
	}

	[RelayCommand]
	private void OpenSettingsDocumentationUrl()
	{
		OpenUrl(SettingsDocumentationUrl);
	}

	[RelayCommand]
	private void CloseWindow(Window? window)
	{
		window?.Close();
	}

	private void LoadSettings()
	{
		try
		{
			LoadFromConfig(_settingsService.Load());
			StatusMessage = "settings.toml を読み込みました。";
		}
		catch (Exception ex)
		{
			StatusMessage = $"settings.toml を読み込めません。ファイルの内容を確認してください。({ex.GetType().Name})";
		}
	}

	private void LoadFromConfig(AppConfig config)
	{
		ChatSites.Clear();
		foreach (var chatSite in config.ChatSites)
		{
			ChatSites.Add(new ChatSiteSettingsItem
			{
				Name = chatSite.Name,
				Url = chatSite.Url
			});
		}
		SelectedChatSite = ChatSites.FirstOrDefault();

		InputSelectors.Clear();
		foreach (var selector in config.Config.InputSelectors)
		{
			InputSelectors.Add(new EditableStringItem { Value = selector });
		}
		SelectedInputSelector = InputSelectors.FirstOrDefault();

		var editorSettings = config.Config.EditorSettings;
		ConfirmTemplateOverwrite = editorSettings.ConfirmTemplateOverwrite;
		ConfirmHistoryOverwrite = editorSettings.ConfirmHistoryOverwrite;
		InsertTemplateTextOnClear = editorSettings.InsertTemplateTextOnClear;
		TemplateTextForEditor = editorSettings.TemplateTextForEditor ?? string.Empty;

		TemplateDirectory = config.Config.TemplateSettings.TemplateDirectory ?? string.Empty;

		var executeSettings = config.Config.ExecuteAfterSendSettings;
		ExecutionTimeoutMs = executeSettings.ExecutionTimeoutMs;
		PostInputDelayMs = executeSettings.PostInputDelayMs;
		RetryIntervalMs = executeSettings.RetryIntervalMs;
		EnableDomAnalysisLog = executeSettings.EnableDomAnalysisLog;
		UnsupportedServiceBehavior = executeSettings.UnsupportedServiceBehavior;

		ServiceExecutors.Clear();
		foreach (var serviceExecutor in executeSettings.ServiceExecutors)
		{
			var item = new ServiceExecutorSettingsItem
			{
				ServiceName = serviceExecutor.ServiceName,
				KeyboardFallback = serviceExecutor.KeyboardFallback
			};

			foreach (var urlPattern in serviceExecutor.UrlPatterns)
			{
				item.UrlPatterns.Add(new EditableStringItem { Value = urlPattern });
			}

			foreach (var submitButtonSelector in serviceExecutor.SubmitButtonSelectors)
			{
				item.SubmitButtonSelectors.Add(new EditableStringItem { Value = submitButtonSelector });
			}

			item.SelectedUrlPattern = item.UrlPatterns.FirstOrDefault();
			item.SelectedSubmitButtonSelector = item.SubmitButtonSelectors.FirstOrDefault();
			ServiceExecutors.Add(item);
		}
		SelectedServiceExecutor = ServiceExecutors.FirstOrDefault();

		var tabRestoreSettings = config.Config.TabRestoreSettings;
		SaveAndRestoreTabUrls = tabRestoreSettings.SaveAndRestoreTabUrls;
		AlwaysRestoreInitialTabs = tabRestoreSettings.AlwaysRestoreInitialTabs;

		var uiState = config.Config.UiState;
		ActiveLeftTabIndex = uiState.ActiveLeftTabIndex;
		PaneDisplayMode = uiState.PaneDisplayMode;
		RightPaneSelectedTab = uiState.RightPaneSelectedTab;
		SelectedThemeMode = uiState.IsDarkTheme.ToThemeMode();
		UiStateExecuteAfterSend = uiState.ExecuteAfterSend ?? false;
		UiStateWindowWidth = uiState.WindowWidth;
		UiStateWindowHeight = uiState.WindowHeight;
		UiStateWindowLeft = uiState.WindowLeft;
		UiStateWindowTop = uiState.WindowTop;
		UiStateRestoreWindowPosition = uiState.RestoreWindowPosition;

		LeftPaneTabs.Clear();
		foreach (var tab in uiState.LeftPaneTabs)
		{
			LeftPaneTabs.Add(new LeftPaneTabStateItem
			{
				SiteName = tab.SiteName,
				Url = tab.Url,
				DisplayName = tab.DisplayName,
				CurrentUrl = tab.CurrentUrl
			});
		}
	}

	private AppConfig BuildConfig()
	{
		return new AppConfig
		{
			ChatSites = ChatSites
				.Select(item => new ChatSite
				{
					Name = item.Name ?? string.Empty,
					Url = item.Url ?? string.Empty
				})
				.ToList(),
			Config = new Config
			{
				InputSelectors = InputSelectors
					.Select(item => item.Value ?? string.Empty)
					.ToList(),
				EditorSettings = new EditorSettings
				{
					ConfirmTemplateOverwrite = ConfirmTemplateOverwrite,
					ConfirmHistoryOverwrite = ConfirmHistoryOverwrite,
					InsertTemplateTextOnClear = InsertTemplateTextOnClear,
					TemplateTextForEditor = TemplateTextForEditor ?? string.Empty
				},
				TemplateSettings = new TemplateSettings
				{
					TemplateDirectory = TemplateDirectory ?? string.Empty
				},
				TabRestoreSettings = new TabRestoreSettings
				{
					SaveAndRestoreTabUrls = SaveAndRestoreTabUrls,
					AlwaysRestoreInitialTabs = AlwaysRestoreInitialTabs
				},
				ExecuteAfterSendSettings = new ExecuteAfterSendSettings
				{
					ExecutionTimeoutMs = ExecutionTimeoutMs,
					PostInputDelayMs = PostInputDelayMs,
					RetryIntervalMs = RetryIntervalMs,
					EnableDomAnalysisLog = EnableDomAnalysisLog,
					UnsupportedServiceBehavior = UnsupportedServiceBehavior ?? string.Empty,
					ServiceExecutors = ServiceExecutors
						.Select(item => new ServiceExecutorSettings
						{
							ServiceName = item.ServiceName ?? string.Empty,
							KeyboardFallback = item.KeyboardFallback ?? string.Empty,
							UrlPatterns = item.UrlPatterns.Select(value => value.Value ?? string.Empty).ToList(),
							SubmitButtonSelectors = item.SubmitButtonSelectors.Select(value => value.Value ?? string.Empty).ToList()
						})
						.ToList()
				},
				UiState = new UiStateSettings
				{
					ActiveLeftTabIndex = ActiveLeftTabIndex,
					PaneDisplayMode = PaneDisplayMode,
					RightPaneSelectedTab = RightPaneSelectedTab,
					IsDarkTheme = SelectedThemeMode.ToNullableBoolean(),
					ExecuteAfterSend = UiStateExecuteAfterSend,
					WindowWidth = UiStateWindowWidth,
					WindowHeight = UiStateWindowHeight,
					WindowLeft = UiStateWindowLeft,
					WindowTop = UiStateWindowTop,
					RestoreWindowPosition = UiStateRestoreWindowPosition,
					LeftPaneTabs = LeftPaneTabs
						.Select(item => new LeftPaneTabState
						{
							SiteName = item.SiteName ?? string.Empty,
							Url = item.Url ?? string.Empty,
							DisplayName = item.DisplayName ?? string.Empty,
							CurrentUrl = SaveAndRestoreTabUrls && !AlwaysRestoreInitialTabs
								? item.CurrentUrl ?? string.Empty
								: string.Empty
						})
						.ToList()
				}
			}
		};
	}

	private void OpenUrl(string url)
	{
		try
		{
			Process.Start(new ProcessStartInfo(url)
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			StatusMessage = $"リンクを開けません: {ex.Message}";
		}
	}

	private static void RemoveSelectedItem<T>(ObservableCollection<T> items, T? selectedItem, Action<T?> updateSelection)
		where T : class
	{
		if (selectedItem == null)
		{
			return;
		}

		var index = items.IndexOf(selectedItem);
		if (index < 0)
		{
			return;
		}

		items.RemoveAt(index);
		var nextIndex = Math.Min(index, items.Count - 1);
		updateSelection(nextIndex >= 0 ? items[nextIndex] : null);
	}

	private static void MoveSelectedItem<T>(ObservableCollection<T> items, T? selectedItem, int direction)
		where T : class
	{
		if (selectedItem == null)
		{
			return;
		}

		var oldIndex = items.IndexOf(selectedItem);
		var newIndex = oldIndex + direction;
		if (oldIndex < 0 || newIndex < 0 || newIndex >= items.Count)
		{
			return;
		}

		items.Move(oldIndex, newIndex);
	}

	private static string? GetAssemblyMetadata(string key)
	{
		var assembly = Assembly.GetExecutingAssembly();
		return assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
			.FirstOrDefault(attribute => attribute.Key == key)
			?.Value;
	}
}

public partial class ChatSiteSettingsItem : ObservableObject
{
	[ObservableProperty]
	private string _name = string.Empty;

	[ObservableProperty]
	private string _url = string.Empty;
}

public partial class EditableStringItem : ObservableObject
{
	[ObservableProperty]
	private string _value = string.Empty;
}

public partial class ServiceExecutorSettingsItem : ObservableObject
{
	[ObservableProperty]
	private string _serviceName = string.Empty;

	[ObservableProperty]
	private string _keyboardFallback = "None";

	[ObservableProperty]
	private EditableStringItem? _selectedUrlPattern;

	[ObservableProperty]
	private EditableStringItem? _selectedSubmitButtonSelector;

	public ObservableCollection<EditableStringItem> UrlPatterns { get; } = new();
	public ObservableCollection<EditableStringItem> SubmitButtonSelectors { get; } = new();
}

public partial class LeftPaneTabStateItem : ObservableObject
{
	[ObservableProperty]
	private string _siteName = string.Empty;

	[ObservableProperty]
	private string _url = string.Empty;

	[ObservableProperty]
	private string _displayName = string.Empty;

	[ObservableProperty]
	private string _currentUrl = string.Empty;
}
