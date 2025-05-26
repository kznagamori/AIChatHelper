// Core/Services/IHistoryService.cs
using AIChatHelper.Models;
using System;
using System.Collections.Generic;

namespace AIChatHelper.Core.Services;
public interface IHistoryService
{
	void AddHistory(string text);
	IEnumerable<string> GetHistories();
	IEnumerable<HistoryItem> GetHistoryRecords();
	void DeleteHistory(int id);
	void ClearAllHistories();

	// 新しいメソッド
	HistoryItem? GetLatestHistoryItem();
	IEnumerable<HistoryItem> GetHistoryByDate(DateTime date);
	IEnumerable<DateTime> GetAvailableDates();
}