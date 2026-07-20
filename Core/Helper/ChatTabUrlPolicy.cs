using AIChatHelper.Models;

namespace AIChatHelper.Core.Helper;

/// <summary>
/// 保存済みチャットタブと登録サイトを照合し、復元可能な URL を検証します。
/// </summary>
internal static class ChatTabUrlPolicy
{
	/// <summary>
	/// settings.toml に保存できる URL の最大文字数です。
	/// </summary>
	internal const int MaxPersistedUrlLength = 8192;

	/// <summary>
	/// 保存済みタブの登録元に一致する、現在のチャットサイト設定を検索します。
	/// </summary>
	/// <param name="savedTab">保存済みのタブ状態。</param>
	/// <param name="chatSites">現在登録されているチャットサイト。</param>
	/// <returns>一致するチャットサイト。見つからない場合は <see langword="null"/>。</returns>
	internal static ChatSite? FindMatchingSite(
		LeftPaneTabState savedTab,
		IReadOnlyList<ChatSite> chatSites)
	{
		ArgumentNullException.ThrowIfNull(savedTab);
		ArgumentNullException.ThrowIfNull(chatSites);

		return chatSites.FirstOrDefault(site =>
				string.Equals(site.Name, savedTab.SiteName, StringComparison.OrdinalIgnoreCase) &&
				AreSameRegisteredUrl(site.Url, savedTab.Url))
			?? chatSites.FirstOrDefault(site => AreSameRegisteredUrl(site.Url, savedTab.Url));
	}

	/// <summary>
	/// 候補 URL が登録サイトと同じスキーム、ホスト、またはその DNS サブドメインに属するか検証します。
	/// </summary>
	/// <param name="site">URL の登録元となるチャットサイト。</param>
	/// <param name="candidateUrl">保存または復元する候補 URL。</param>
	/// <param name="uri">検証に成功した絶対 URL。</param>
	/// <returns>安全に保存・復元できる場合は <see langword="true"/>。</returns>
	internal static bool TryGetRestorableUri(ChatSite site, string? candidateUrl, out Uri uri)
	{
		ArgumentNullException.ThrowIfNull(site);
		uri = null!;

		var normalizedCandidate = (candidateUrl ?? string.Empty).Trim();
		if (normalizedCandidate.Length == 0 || normalizedCandidate.Length > MaxPersistedUrlLength)
		{
			return false;
		}

		if (!Uri.TryCreate(normalizedCandidate, UriKind.Absolute, out var candidateUri) ||
			!Uri.TryCreate((site.Url ?? string.Empty).Trim(), UriKind.Absolute, out var registeredUri) ||
			!IsHttpScheme(candidateUri.Scheme) ||
			!IsHttpScheme(registeredUri.Scheme) ||
			!string.Equals(candidateUri.Scheme, registeredUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
			!string.IsNullOrEmpty(candidateUri.UserInfo))
		{
			return false;
		}

		if (candidateUri.AbsoluteUri.Length > MaxPersistedUrlLength)
		{
			return false;
		}

		var candidateHost = NormalizeHost(candidateUri.IdnHost);
		var registeredHost = NormalizeHost(registeredUri.IdnHost);
		if (candidateHost.Length == 0 || registeredHost.Length == 0)
		{
			return false;
		}

		if (string.Equals(candidateHost, registeredHost, StringComparison.OrdinalIgnoreCase))
		{
			uri = candidateUri;
			return true;
		}

		// IP アドレスや localhost は DNS サブドメインとして扱わず、完全一致だけを許可する。
		if (candidateUri.HostNameType != UriHostNameType.Dns ||
			registeredUri.HostNameType != UriHostNameType.Dns ||
			IsLocalhost(candidateHost) ||
			IsLocalhost(registeredHost) ||
			!candidateHost.EndsWith('.' + registeredHost, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		uri = candidateUri;
		return true;
	}

	private static bool AreSameRegisteredUrl(string? left, string? right)
	{
		return string.Equals(
			NormalizeRegisteredUrl(left),
			NormalizeRegisteredUrl(right),
			StringComparison.OrdinalIgnoreCase);
	}

	private static string NormalizeRegisteredUrl(string? value)
	{
		return (value ?? string.Empty).Trim().TrimEnd('/');
	}

	private static string NormalizeHost(string value)
	{
		return value.TrimEnd('.');
	}

	private static bool IsLocalhost(string host)
	{
		return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
			host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsHttpScheme(string scheme)
	{
		return string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
	}
}
