// Core/Helper/ThemeAwareImageConverter.cs
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace AIChatHelper.Core.Helper
{
	public class ThemeAwareImageConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is not string baseImageName || parameter is not string imageType)
			{
				// nullの代わりにデフォルトの画像を返す
				return new BitmapImage(new Uri("pack://siteoforigin:,,,/Assets/template.png"));
			}

			// ViewModelからIsDarkThemeプロパティを取得
			bool isDarkMode = false;

			if (App.Current.MainWindow?.DataContext is ViewModels.MainWindowViewModel viewModel)
			{
				isDarkMode = viewModel.IsDarkTheme;
			}

			string themeMode = isDarkMode ? "dark" : "light";
			string imagePath = $"pack://siteoforigin:,,,/Assets/{baseImageName}_{themeMode}.{imageType}";

			try
			{
				return new BitmapImage(new Uri(imagePath));
			}
			catch
			{
				// 指定されたパスの画像が見つからない場合はフォールバック
				return new BitmapImage(new Uri($"pack://siteoforigin:,,,/Assets/{baseImageName}.{imageType}"));
			}
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			throw new NotImplementedException();
		}

		// このメソッドは不要になりました
		private bool IsSystemInDarkMode()
		{
			return false; // 使用されないため、常にfalseを返す
		}
	}
}
