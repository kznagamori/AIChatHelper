// ISettingsService.cs
using System;
using AIChatHelper.Models;

namespace AIChatHelper.Core.Services;
public interface ISettingsService
{
	AppConfig Load();
}
