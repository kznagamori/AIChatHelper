// Core/Controls/HistoryCardControl.xaml.cs
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AIChatHelper.Models;
using MaterialDesignThemes.Wpf;

namespace AIChatHelper.Core.Controls
{
	public partial class HistoryCardControl : UserControl
	{
		public static readonly DependencyProperty HistoryItemProperty =
			DependencyProperty.Register(
				nameof(HistoryItem),
				typeof(HistoryItem),
				typeof(HistoryCardControl),
				new PropertyMetadata(null, OnHistoryItemChanged));

		public HistoryItem HistoryItem
		{
			get => (HistoryItem)GetValue(HistoryItemProperty);
			set => SetValue(HistoryItemProperty, value);
		}

		// ItemDoubleClickedイベントの定義
		public static readonly RoutedEvent ItemDoubleClickedEvent =
			EventManager.RegisterRoutedEvent(
				nameof(ItemDoubleClicked),
				RoutingStrategy.Bubble,
				typeof(RoutedEventHandler),
				typeof(HistoryCardControl));

		// ItemDoubleClickedイベント
		public event RoutedEventHandler ItemDoubleClicked
		{
			add { AddHandler(ItemDoubleClickedEvent, value); }
			remove { RemoveHandler(ItemDoubleClickedEvent, value); }
		}

		// 展開状態を管理
		private bool _isExpanded = false;

		public HistoryCardControl()
		{
			InitializeComponent();
			// 親コンテナの幅に合わせて自動調整
			HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
			HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch;

			// 幅を最大化するために親のサイズ変更イベントを購読
			this.Loaded += (sender, e) =>
			{
				// 修正：nullチェックを追加し、キャスト後に再度nullチェック
				if (this.Parent is FrameworkElement parent)
				{
					this.Width = parent.ActualWidth - 10; // マージンを考慮

					// 親のサイズ変更に合わせて自分自身のサイズも変更
					parent.SizeChanged += (s, args) =>
					{
						this.Width = args.NewSize.Width - 10; // マージンを考慮
					};
				}
			};
		}

		private static void OnHistoryItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			if (d is HistoryCardControl control && e.NewValue is HistoryItem item)
			{
				control.DataContext = item;
			}
		}

		private void ExpandButton_Click(object sender, RoutedEventArgs e)
		{
			ToggleExpandMode();
		}

		private void Content_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
		{
			if (e.ClickCount == 2)
			{
				// ダブルクリック時にイベント発火
				RaiseEvent(new RoutedEventArgs(ItemDoubleClickedEvent, this));
			}
		}

		private void ToggleExpandMode()
		{
			_isExpanded = !_isExpanded;

			if (_isExpanded)
			{
				// 展開モード
				ContentPreview.Visibility = Visibility.Collapsed;
				ExpandedContentScroll.Visibility = Visibility.Visible;

				// PackIconを取得してKindプロパティを変更
				if (ExpandButton.Content is PackIcon packIcon)
				{
					packIcon.Kind = PackIconKind.ChevronUp;
				}
			}
			else
			{
				// 通常モード
				ContentPreview.Visibility = Visibility.Visible;
				ExpandedContentScroll.Visibility = Visibility.Collapsed;

				// PackIconを取得してKindプロパティを変更
				if (ExpandButton.Content is PackIcon packIcon)
				{
					packIcon.Kind = PackIconKind.ChevronDown;
				}
			}
		}
	}
}