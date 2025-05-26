// SettingsService.cs
using System;
using System.IO;
using Tomlet;
using AIChatHelper.Models;
using Tomlet.Attributes;

namespace AIChatHelper.Core.Services;

public class SettingsService : ISettingsService
{
	private readonly string _filePath;
	public SettingsService()
	{
		var exeDir = AppDomain.CurrentDomain.BaseDirectory;
		_filePath = Path.Combine(exeDir, "settings.toml");
	}

	public AppConfig Load()
	{
		// TOML ファイルを読み込み
		var tomlText = File.ReadAllText(_filePath);
		AppConfig config = TomletMain.To<AppConfig>(tomlText);

		return config;
	}
}
