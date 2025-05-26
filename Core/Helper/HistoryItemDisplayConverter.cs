// Core/Helper/HistoryItemDisplayConverter.cs
using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace AIChatHelper.Core.Helper
{
	public class HistoryItemDisplayConverter : IValueConverter
	{
		// テキスト表示の最大行数
		public int MaxLines { get; set; } = 4;

		// テキスト表示の最大文字数
		public int MaxChars { get; set; } = 400;

		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is string s)
			{
				// 改行を保持
				var lines = s.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

				// 最大行数まで取得し、改行を保持
				var limitedLines = lines.Take(MaxLines).ToArray();

				// 行を結合（改行を保持）
				string result = string.Join(Environment.NewLine, limitedLines);

				// 設定された行数より多い場合は「...」を追加
				if (lines.Length > MaxLines)
				{
					result += " ...";
				}

				// 設定された文字数を超える場合は切り詰め
				if (result.Length > MaxChars)
				{
					result = result.Substring(0, MaxChars - 3) + "...";
				}

				return result;
			}

			return value ?? "";
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
			=> throw new NotSupportedException();
	}
}