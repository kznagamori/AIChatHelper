// Core/Services/LicenseService.cs
using System;
using System.IO;

namespace AIChatHelper.Core.Services
{
	public class LicenseService : ILicenseService
	{
		public string GetLicenseText()
		{
			try
			{
				// アプリケーションの実行ディレクトリを取得
				var exeDir = AppDomain.CurrentDomain.BaseDirectory;

				// ライセンス情報ファイルへのパス
				var licenseFilePath = Path.Combine(exeDir, "Licenses", "README.md");

				// ファイルが存在するか確認
				if (!File.Exists(licenseFilePath))
				{
					return "ライセンス情報ファイル (Licenses/README.md) が見つかりません。";
				}

				// ファイルを読み込む
				return File.ReadAllText(licenseFilePath);
			}
			catch (Exception ex)
			{
				return $"ライセンス情報の読み込み中にエラーが発生しました：{ex.Message}";
			}
		}
	}
}