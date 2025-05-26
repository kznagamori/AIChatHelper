//HistoryManagementWindowViewModel.cs
using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AIChatHelper.Models;
using AIChatHelper.Core.Services;
using System.Windows;

namespace AIChatHelper.ViewModels
{
	public partial class HistoryManagementWindowViewModel : ObservableObject
	{
		private readonly IHistoryService _historyService;

		public ObservableCollection<HistoryItem> HistoryItems { get; } = new();

		[ObservableProperty]
		private HistoryItem? _selectedHistoryItem;

		public HistoryManagementWindowViewModel(IHistoryService historyService)
		{
			_historyService = historyService;
			LoadHistories();
		}

		private void LoadHistories()
		{
			HistoryItems.Clear();
			foreach (var item in _historyService.GetHistoryRecords())
				HistoryItems.Add(item);
		}
		partial void OnSelectedHistoryItemChanged(HistoryItem? value)
		{
			// DeleteHistoryCommand の CanExecute を再評価
			DeleteHistoryCommand.NotifyCanExecuteChanged();
		}
		// ───────── Delete ─────────
		[RelayCommand(CanExecute = nameof(CanDelete))]
		private void DeleteHistory()
		{
			if (SelectedHistoryItem is not null)
			{
				_historyService.DeleteHistory(SelectedHistoryItem.Id);
				LoadHistories();
			}
		}
		private bool CanDelete() => SelectedHistoryItem != null;

		// ───────── Clear All ─────────
		[RelayCommand]
		private void ClearAllHistories()
		{
			_historyService.ClearAllHistories();
			LoadHistories();
		}

		// ───────── Close ─────────
		// CommandParameter で Window 自身を渡します
		[RelayCommand]
		private void CloseWindow(Window window)
		{
			window.Close();
		}
	}
}
