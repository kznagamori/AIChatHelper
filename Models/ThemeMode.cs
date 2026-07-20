namespace AIChatHelper.Models;

/// <summary>
/// アプリケーションで選択できるテーマモードを表します。
/// </summary>
public enum ThemeMode
{
	/// <summary>
	/// Windows のアプリテーマへ継続的に追従します。
	/// </summary>
	System,

	/// <summary>
	/// ライトテーマに固定します。
	/// </summary>
	Light,

	/// <summary>
	/// ダークテーマに固定します。
	/// </summary>
	Dark
}

/// <summary>
/// <see cref="ThemeMode"/> と settings.toml の nullable bool 表現を相互変換します。
/// </summary>
public static class ThemeModeExtensions
{
	/// <summary>
	/// settings.toml の値をテーマモードへ変換します。
	/// </summary>
	/// <param name="isDarkTheme"><see langword="null"/> は Windows 追従、false はライト、true はダークです。</param>
	/// <returns>対応するテーマモード。</returns>
	public static ThemeMode ToThemeMode(this bool? isDarkTheme)
	{
		return isDarkTheme switch
		{
			true => ThemeMode.Dark,
			false => ThemeMode.Light,
			_ => ThemeMode.System
		};
	}

	/// <summary>
	/// テーマモードを settings.toml の nullable bool 表現へ変換します。
	/// </summary>
	/// <param name="themeMode">変換するテーマモード。</param>
	/// <returns>Windows 追従は <see langword="null"/>、ライトは false、ダークは true。</returns>
	public static bool? ToNullableBoolean(this ThemeMode themeMode)
	{
		return themeMode switch
		{
			ThemeMode.Light => false,
			ThemeMode.Dark => true,
			_ => null
		};
	}
}

/// <summary>
/// 設定画面へ表示するテーマモードと表示名の組み合わせを表します。
/// </summary>
/// <param name="Value">テーマモード。</param>
/// <param name="DisplayName">利用者向けの表示名。</param>
public sealed record ThemeModeOption(ThemeMode Value, string DisplayName);
