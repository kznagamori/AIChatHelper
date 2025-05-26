// Core/Models/HistoryItem.cs
using System;

namespace AIChatHelper.Models;
public class HistoryItem
{
	public int Id { get; set; }
	public string Text { get; set; } = string.Empty;
	public DateTime CreatedAt { get; set; }
}
