// Core/Controls/SlackStyleHistoryView.xaml.cs
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AIChatHelper.Models;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace AIChatHelper.Core.Controls
{
	public partial class SlackStyleHistoryView : UserControl
	{
		private readonly Dictionary<DateTime, DateSeparatorControl> _dateSeparators = new();
		private readonly Dictionary<HistoryItem, HistoryCardControl> _historyCards = new();
		private INotifyCollectionChanged? _notifyCollection;
		private bool _isUpdating = false;
		private DispatcherTimer? _scrollTimer;
		private const int VISIBLE_ITEMS_LIMIT = 20; // 一度に表示するアイテム数の制限

		public static readonly DependencyProperty HistoryItemsProperty =
			DependencyProperty.Register(
				nameof(HistoryItems),
				typeof(IEnumerable<HistoryItem>),
				typeof(SlackStyleHistoryView),
				new PropertyMetadata(null, OnHistoryItemsChanged));

		public IEnumerable<HistoryItem> HistoryItems
		{
			get => (IEnumerable<HistoryItem>)GetValue(HistoryItemsProperty);
			set => SetValue(HistoryItemsProperty, value);
		}

		public static readonly DependencyProperty SelectedHistoryItemProperty =
			DependencyProperty.Register(
				nameof(SelectedHistoryItem),
				typeof(HistoryItem),
				typeof(SlackStyleHistoryView),
				new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

		public HistoryItem SelectedHistoryItem
		{
			get => (HistoryItem)GetValue(SelectedHistoryItemProperty);
			set => SetValue(SelectedHistoryItemProperty, value);
		}

		// ヒストリーアイテムがダブルクリックされたときに発生するイベント
		public static readonly RoutedEvent HistoryItemDoubleClickedEvent =
			EventManager.RegisterRoutedEvent(
				nameof(HistoryItemDoubleClicked),
				RoutingStrategy.Bubble,
				typeof(RoutedEventHandler),
				typeof(SlackStyleHistoryView));

		public event RoutedEventHandler HistoryItemDoubleClicked
		{
			add { AddHandler(HistoryItemDoubleClickedEvent, value); }
			remove { RemoveHandler(HistoryItemDoubleClickedEvent, value); }
		}

		public SlackStyleHistoryView()
		{
			InitializeComponent();

			// コントロールがアンロードされる時にコレクション変更通知のサブスクリプションを解除
			Unloaded += (s, e) =>
			{
				if (_notifyCollection != null)
				{
					_notifyCollection.CollectionChanged -= Collection_CollectionChanged;
				}

				if (_scrollTimer != null)
				{
					_scrollTimer.Stop();
					_scrollTimer = null;
				}
			};

			// スクロールイベントを監視してアイテムを動的に追加/削除
			HistoryScrollViewer.ScrollChanged += HistoryScrollViewer_ScrollChanged;

			// 初期化時にスクロールタイマーを設定
			_scrollTimer = new DispatcherTimer
			{
				Interval = TimeSpan.FromMilliseconds(200) // 200msのスロットリング
			};
			_scrollTimer.Tick += (s, e) =>
			{
				_scrollTimer.Stop();
				UpdateVisibleItems();
			};
		}

		private static void OnHistoryItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			if (d is SlackStyleHistoryView view)
			{
				// 以前のコレクションからの通知を解除
				if (e.OldValue is INotifyCollectionChanged oldCollection)
				{
					oldCollection.CollectionChanged -= view.Collection_CollectionChanged;
				}

				// 新しいコレクションの通知を購読
				if (e.NewValue is INotifyCollectionChanged newCollection)
				{
					view._notifyCollection = newCollection;
					newCollection.CollectionChanged += view.Collection_CollectionChanged;
				}

				// 最初は最新アイテムのみ表示
				view.InitialRebuildWithLatestItems();
			}
		}

		private void Collection_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
			// コレクションが変更されたら対応するUIの更新のみを行う
			switch (e.Action)
			{
				case NotifyCollectionChangedAction.Add:
					if (e.NewItems != null)
					{
						AddNewItems(e.NewItems.Cast<HistoryItem>());
					}
					break;
				case NotifyCollectionChangedAction.Remove:
					if (e.OldItems != null)
					{
						RemoveOldItems(e.OldItems.Cast<HistoryItem>());
					}
					break;
				case NotifyCollectionChangedAction.Reset:
					// 完全にリセットが必要な場合
					InitialRebuildWithLatestItems();
					break;
			}
		}

		// 新しいアイテムを追加（通常は1つだけ）
		// 新しいアイテムを追加（通常は1つだけ）
		private void AddNewItems(IEnumerable<HistoryItem> newItems)
		{
			if (_isUpdating) return;
			_isUpdating = true;

			try
			{
				foreach (var item in newItems)
				{
					// 日付セパレータの追加（必要な場合）
					var itemDate = item.CreatedAt.Date;
					if (!_dateSeparators.ContainsKey(itemDate))
					{
						var dateSeparator = new DateSeparatorControl { Date = itemDate };
						_dateSeparators[itemDate] = dateSeparator;

						// 日付順に追加（新しい日付は下に）
						int insertIndex = HistoryItemsControl.Items.Count; // 最後尾を初期値とする

						for (int i = 0; i < HistoryItemsControl.Items.Count; i++)
						{
							if (HistoryItemsControl.Items[i] is DateSeparatorControl existingSeparator &&
								((DateTime)existingSeparator.GetValue(DateSeparatorControl.DateProperty)).Date > itemDate)
							{
								insertIndex = i;
								break;
							}
						}

						HistoryItemsControl.Items.Insert(insertIndex, dateSeparator);
					}

					// カードを作成
					var card = new HistoryCardControl { HistoryItem = item };
					card.ItemDoubleClicked += Card_ItemDoubleClicked;
					card.HorizontalAlignment = HorizontalAlignment.Stretch;
					_historyCards[item] = card;

					// 同じ日付の最後に挿入（新しいアイテムほど下になるように）
					int cardIndex = HistoryItemsControl.Items.Count; // 最後尾を初期値とする

					for (int i = HistoryItemsControl.Items.Count - 1; i >= 0; i--)
					{
						var child = HistoryItemsControl.Items[i];

						if (child is DateSeparatorControl separator &&
							((DateTime)separator.GetValue(DateSeparatorControl.DateProperty)).Date == itemDate)
						{
							// 同日付のセパレータを見つけたら、その直後に挿入
							cardIndex = i + 1;

							// 同日付の既存カードの後ろに追加するため、その日付の最後のカードを探す
							for (int j = i + 1; j < HistoryItemsControl.Items.Count; j++)
							{
								if (HistoryItemsControl.Items[j] is HistoryCardControl existingCard &&
									existingCard.HistoryItem.CreatedAt.Date == itemDate)
								{
									cardIndex = j + 1; // 同じ日付の最後のカードの後ろに
								}
								else if (HistoryItemsControl.Items[j] is DateSeparatorControl)
								{
									// 次の日付セパレータが来たら終了
									break;
								}
							}
							break;
						}
					}

					HistoryItemsControl.Items.Insert(cardIndex, card);
				}

				// 自動スクロール（追加時のみ）
				ScrollToBottom();
			}
			finally
			{
				_isUpdating = false;
			}
		}

		// 削除されたアイテムをUIから削除
		private void RemoveOldItems(IEnumerable<HistoryItem> oldItems)
		{
			if (_isUpdating) return;
			_isUpdating = true;

			try
			{
				foreach (var item in oldItems)
				{
					if (_historyCards.TryGetValue(item, out var card))
					{
						HistoryItemsControl.Items.Remove(card);
						_historyCards.Remove(item);

						// 日付セパレータ削除の処理（その日付の最後のカードが削除された場合）
						var itemDate = item.CreatedAt.Date;
						if (!_historyCards.Values.Any(c => c.HistoryItem.CreatedAt.Date == itemDate) &&
							_dateSeparators.TryGetValue(itemDate, out var separator))
						{
							HistoryItemsControl.Items.Remove(separator);
							_dateSeparators.Remove(itemDate);
						}
					}
				}
			}
			finally
			{
				_isUpdating = false;
			}
		}

		// 初期表示用 - 最新の限定数のアイテムだけを表示
		public void InitialRebuildWithLatestItems()
		{
			if (_isUpdating) return;
			_isUpdating = true;

			try
			{
				HistoryItemsControl.Items.Clear();
				_dateSeparators.Clear();
				_historyCards.Clear();

				if (HistoryItems == null)
					return;

				// 日付でグループ化（最新のものから）
				var allItems = HistoryItems.ToList();

				// 最新のVISIBLE_ITEMS_LIMIT個だけ取得
				var latestItems = allItems
					.OrderByDescending(h => h.CreatedAt)
					.Take(VISIBLE_ITEMS_LIMIT)
					.OrderBy(h => h.CreatedAt.Date)
					.ThenBy(h => h.CreatedAt)
					.ToList();

				// 日付グループごとに表示
				foreach (var group in latestItems.GroupBy(h => h.CreatedAt.Date).OrderBy(g => g.Key))
				{
					AddDateGroup(group.Key, group.OrderBy(h => h.CreatedAt));
				}

				// 事前にスクロール位置を設定
				Dispatcher.InvokeAsync(() => ScrollToBottom(), DispatcherPriority.Loaded);
			}
			finally
			{
				_isUpdating = false;
			}
		}

		// 日付グループを追加
		private void AddDateGroup(DateTime date, IEnumerable<HistoryItem> items)
		{
			// 日付セパレータを作成
			var dateSeparator = new DateSeparatorControl { Date = date };
			_dateSeparators[date] = dateSeparator;
			dateSeparator.HorizontalAlignment = HorizontalAlignment.Stretch;

			// 日付セパレータを適切な位置に追加（古い日付ほど上に）
			int separatorIndex = HistoryItemsControl.Items.Count; // 最後尾を初期値とする

			for (int i = 0; i < HistoryItemsControl.Items.Count; i++)
			{
				if (HistoryItemsControl.Items[i] is DateSeparatorControl existingSeparator &&
					((DateTime)existingSeparator.GetValue(DateSeparatorControl.DateProperty)).Date > date)
				{
					separatorIndex = i;
					break;
				}
			}

			HistoryItemsControl.Items.Insert(separatorIndex, dateSeparator);

			// 履歴カードを追加（新しいアイテムほど下に表示）
			var sortedItems = items.OrderBy(h => h.CreatedAt).ToList();
			foreach (var item in sortedItems)
			{
				var card = new HistoryCardControl { HistoryItem = item };
				card.ItemDoubleClicked += Card_ItemDoubleClicked;
				card.HorizontalAlignment = HorizontalAlignment.Stretch;
				_historyCards[item] = card;
				HistoryItemsControl.Items.Insert(separatorIndex + 1, card);
				separatorIndex++; // 次のカードの挿入位置を更新
			}
		}

		// スクロール変更イベントハンドラ - スロットリング処理
		private void HistoryScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
		{
			if (_isUpdating) return;

			// スクロール中はタイマーをリセット
			if (_scrollTimer != null)
			{
				_scrollTimer.Stop();
				_scrollTimer.Start();
			}
		}

		// スクロール時に表示範囲に入ったアイテムを動的に追加
		private void UpdateVisibleItems()
		{
			if (_isUpdating || HistoryItems == null) return;
			_isUpdating = true;

			try
			{
				// 現在の表示領域
				var viewportTop = HistoryScrollViewer.VerticalOffset;
				var viewportBottom = viewportTop + HistoryScrollViewer.ViewportHeight;

				// バッファエリアを含む計算（スクロール前後に余裕を持たせる）
				double bufferSize = HistoryScrollViewer.ViewportHeight * 1.5;
				var extendedTop = Math.Max(0, viewportTop - bufferSize);
				var extendedBottom = viewportBottom + bufferSize;

				// 未ロードの日付グループがあれば、表示範囲に入っているものを追加
				var allItems = HistoryItems.ToList();
				var displayedDates = _dateSeparators.Keys.ToHashSet();

				var missingDateGroups = allItems
					.GroupBy(h => h.CreatedAt.Date)
					.Where(g => !displayedDates.Contains(g.Key))
					.OrderBy(g => g.Key);

				foreach (var group in missingDateGroups)
				{
					// 表示範囲に入る可能性があるかを判断（日付の推定位置）
					double estimatedPosition = EstimatePositionForDate(group.Key);

					if (estimatedPosition >= extendedTop && estimatedPosition <= extendedBottom)
					{
						// 表示範囲内なので追加
						AddDateGroup(group.Key, group.OrderBy(h => h.CreatedAt));
					}
				}
			}
			finally
			{
				_isUpdating = false;
			}
		}

		// 日付の表示位置を推定するヘルパーメソッド
		private double EstimatePositionForDate(DateTime date)
		{
			// 日付の相対的な位置を推定（最古の日付を0、最新の日付を1とする）
			var allDates = HistoryItems
				.Select(h => h.CreatedAt.Date)
				.Distinct()
				.OrderBy(d => d)
				.ToList();

			if (allDates.Count == 0) return 0;

			int index = allDates.FindIndex(d => d >= date);
			if (index < 0) index = allDates.Count;

			double relativePosition = (double)index / allDates.Count;

			// ScrollViewerの全体の高さに相対位置を適用
			return relativePosition * HistoryScrollViewer.ScrollableHeight;
		}

		private void Card_ItemDoubleClicked(object sender, RoutedEventArgs e)
		{
			if (sender is HistoryCardControl card)
			{
				SelectedHistoryItem = card.HistoryItem;
				RaiseEvent(new RoutedEventArgs(HistoryItemDoubleClickedEvent, this));
			}
		}

		// スクロールビューを最下部にスクロールするメソッド
		private void ScrollToBottom()
		{
			// UIスレッドでのレンダリングが完了した後にスクロール処理を実行するために
			// Dispatcherを使用して非同期にスクロール処理をキューに入れる
			Dispatcher.InvokeAsync(() =>
			{
				if (HistoryItemsControl.Items.Count > 0)
				{
					HistoryScrollViewer.ScrollToBottom();
				}
			}, DispatcherPriority.Render);
		}

		// 新しい履歴が追加されたとき、最新の履歴を表示するためのスクロール
		public void ScrollToLatestHistory()
		{
			ScrollToBottom();
		}

	}
}