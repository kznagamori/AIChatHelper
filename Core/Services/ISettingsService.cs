// ISettingsService.cs
using System;
using AIChatHelper.Models;

namespace AIChatHelper.Core.Services;
public interface ISettingsService
{
	string SettingsFilePath { get; }
	AppConfig Load();
	AppConfig Validate(AppConfig config);
	void Save(AppConfig config);
	void SaveUiState(UiStateSettings uiState);
}
