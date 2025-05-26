// Core/Controls/AvalonTextEditor.xaml.cs
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using Microsoft.Win32;
using System.Xml;

namespace AIChatHelper.Core.Controls
{
	public partial class AvalonTextEditor : UserControl
	{
		private readonly TextEditor _editor;
		private bool _isDarkMode;

		public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
			nameof(Text), typeof(string), typeof(AvalonTextEditor),
			new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, TextPropertyChanged));

		public string Text
		{
			get => (string)GetValue(TextProperty);
			set => SetValue(TextProperty, value);
		}

		public event EventHandler<ExecuteCommandEventArgs>? ExecuteCommand;

		public AvalonTextEditor()
		{
			InitializeComponent();

			_editor = new TextEditor
			{
				FontFamily = new FontFamily("Consolas, Courier New, monospace"),
				FontSize = 14,
				ShowLineNumbers = true,
				WordWrap = true,
				HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
				Padding = new Thickness(5)
			};

			// エディタをコントロールに追加
			EditorContainer.Children.Add(_editor);

			// 起動時のみOSのテーマを取得して適用
			_isDarkMode = IsSystemInDarkMode();
			ApplyTheme(_isDarkMode);

			// キーイベントのハンドリング
			_editor.PreviewKeyDown += Editor_PreviewKeyDown;

			// テキスト変更イベント
			_editor.TextChanged += (s, e) =>
			{
				Text = _editor.Text;
			};
		}

		private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
		{
			// Ctrl+Enterでチャットへ送信
			if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
			{
				ExecuteCommand?.Invoke(this, new ExecuteCommandEventArgs("EXECUTE_CHAT_COMMAND"));
				e.Handled = true;
			}
		}

		// テキストプロパティが外部から変更されたとき
		private static void TextPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			if (d is AvalonTextEditor editor && e.NewValue is string newText)
			{
				if (editor._editor.Text != newText)
				{
					editor._editor.Text = newText;
				}
			}
		}

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

		public void ApplyTheme(bool isDarkMode)
		{
			_isDarkMode = isDarkMode;

			if (isDarkMode)
			{
				// ダークテーマ
				_editor.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
				_editor.Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220));
				_editor.LineNumbersForeground = new SolidColorBrush(Color.FromRgb(150, 150, 150));
			}
			else
			{
				// ライトテーマ
				_editor.Background = new SolidColorBrush(Color.FromRgb(250, 250, 250));
				_editor.Foreground = new SolidColorBrush(Color.FromRgb(30, 30, 30));
				_editor.LineNumbersForeground = new SolidColorBrush(Color.FromRgb(100, 100, 100));
			}
		}
	}

	public class ExecuteCommandEventArgs : EventArgs
	{
		public string Command { get; }

		public ExecuteCommandEventArgs(string command)
		{
			Command = command;
		}
	}
}