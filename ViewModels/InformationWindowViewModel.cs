//InformationWindowViewModel.cs
using System;
using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AIChatHelper.Core.Services;

namespace AIChatHelper.ViewModels
{
	public partial class InformationWindowViewModel : ObservableObject
	{
		private readonly ILicenseService _licenseService;

		[ObservableProperty]
		private string _version = string.Empty;

		[ObservableProperty]
		private string _copyright = string.Empty;

		[ObservableProperty]
		private string _projectUrl = string.Empty;

		[ObservableProperty]
		private string _licenseText = string.Empty;

		public InformationWindowViewModel(ILicenseService licenseService)
		{
			_licenseService = licenseService;
			LoadApplicationInfo();
			LoadLicenseInfo();
		}

		private void LoadApplicationInfo()
		{
			// アセンブリから情報を取得
			var assembly = Assembly.GetExecutingAssembly();

			// バージョン情報
			var versionInfo = assembly.GetName().Version;
			Version = versionInfo?.ToString() ?? "不明";

			// 著作権情報
			var copyrightAttr = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>();
			Copyright = copyrightAttr?.Copyright ?? "Copyright © 2025";

			String? projectUrl = null;
			var attributes = assembly.GetCustomAttributes<AssemblyMetadataAttribute>();
			foreach (var attr in attributes)
			{
				if (attr.Key == "PackageProjectUrl")
				{
					projectUrl = attr.Value;
					break;
				}
			}
			ProjectUrl = projectUrl ?? "https://github.com/";
		}

		private void LoadLicenseInfo()
		{
			try
			{
				// ライセンス情報をサービスから取得
				LicenseText = _licenseService.GetLicenseText();

				// ライセンステキストが取得できなかった場合
				if (string.IsNullOrEmpty(LicenseText))
				{
					LicenseText = "ライセンス情報の読み込みに失敗しました。";
				}
			}
			catch (Exception ex)
			{
				LicenseText = $"ライセンス情報の読み込み中にエラーが発生しました。\n{ex.Message}";
			}
		}

		[RelayCommand]
		private void CloseWindow(Window window)
		{
			window.Close();
		}
	}
}