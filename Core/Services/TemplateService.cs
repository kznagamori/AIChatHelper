// Core/Services/TemplateService.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AIChatHelper.Core.Services
{
	public class TemplateService : ITemplateService
	{
		private readonly Dictionary<string, string> _templateMap;

		public TemplateService()
		{
			// 実行フォルダの "template" サブフォルダ
			var exeDir = AppDomain.CurrentDomain.BaseDirectory;
			var templateDir = Path.Combine(exeDir, "template");

			// フォルダがなければ空のマップ
			if (!Directory.Exists(templateDir))
			{
				_templateMap = new Dictionary<string, string>();
				return;
			}

			// 全ファイルを取得し、拡張子なしファイル名→フルパス のマップを作成
			_templateMap = Directory
				.EnumerateFiles(templateDir)
				.ToDictionary(
					path => Path.GetFileNameWithoutExtension(path),  // DisplayName
					path => path                                   // FullPath
				);
		}

		// ComboBox に表示する「拡張子なしファイル名」の一覧
		public IEnumerable<string> GetTemplateFileNames()
			=> _templateMap.Keys;

		// TemplateMapを再構築
		public void RebuildTemplateMap()
		{
			// 実行フォルダの "template" サブフォルダ
			var exeDir = AppDomain.CurrentDomain.BaseDirectory;
			var templateDir = Path.Combine(exeDir, "template");

			// フォルダがなければ空のマップ
			if (!Directory.Exists(templateDir))
			{
				_templateMap.Clear();
				return;
			}

			// 全ファイルを取得し、拡張子なしファイル名→フルパス のマップを作成
			_templateMap.Clear();
			foreach (var path in Directory.EnumerateFiles(templateDir))
			{
				var fileName = Path.GetFileNameWithoutExtension(path);
				if (!string.IsNullOrEmpty(fileName))
					_templateMap[fileName] = path;
			}
		}

		// 選択された displayName(拡張子なし) から元ファイルを読み込んで返す
		public string LoadTemplate(string displayName)
		{
			if (string.IsNullOrEmpty(displayName))
				return string.Empty;

			if (!_templateMap.TryGetValue(displayName, out var fullPath))
				return string.Empty;

			return File.Exists(fullPath)
				? File.ReadAllText(fullPath)
				: string.Empty;
		}
	}
}
