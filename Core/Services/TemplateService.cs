// Core/Services/TemplateService.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using AIChatHelper.Models;

namespace AIChatHelper.Core.Services
{
	public class TemplateService : ITemplateService
	{
		private const int ResourceTypeDisk = 0x00000001;
		private const int ConnectInteractive = 0x00000008;
		private const int ConnectPrompt = 0x00000010;
		private const int NoError = 0;

		private readonly ISettingsService _settingsService;
		private readonly Dictionary<string, string> _templateMap = new(StringComparer.OrdinalIgnoreCase);

		public TemplateService(ISettingsService settingsService)
		{
			_settingsService = settingsService;
		}

		public IEnumerable<string> GetTemplateFileNames()
			=> _templateMap.Keys;

		public void RebuildTemplateMap()
		{
			_templateMap.Clear();

			var templateDir = GetResolvedTemplateDirectory();
			if (!Directory.Exists(templateDir))
			{
				return;
			}

			try
			{
				foreach (var path in Directory.EnumerateFiles(templateDir))
				{
					var fileInfo = new FileInfo(path);
					if (ShouldSkip(fileInfo))
					{
						continue;
					}

					var fileName = Path.GetFileNameWithoutExtension(path);
					if (!string.IsNullOrEmpty(fileName))
					{
						_templateMap[fileName] = path;
					}
				}
			}
			catch (Exception ex) when (IsFileSystemAccessException(ex))
			{
				Debug.WriteLine($"テンプレート一覧の再構築に失敗しました: {ex.Message}");
			}
		}

		public string LoadTemplate(string displayName)
		{
			if (string.IsNullOrEmpty(displayName))
			{
				return string.Empty;
			}

			if (!_templateMap.TryGetValue(displayName, out var fullPath))
			{
				return string.Empty;
			}

			return LoadTemplateByPath(fullPath);
		}

		public IReadOnlyList<TemplateTreeNode> GetTemplateTree(IntPtr ownerWindowHandle = default)
		{
			var templateDir = GetResolvedTemplateDirectory();

			if (!Directory.Exists(templateDir))
			{
				TryPromptForNetworkCredentials(templateDir, ownerWindowHandle);
			}

			if (!Directory.Exists(templateDir))
			{
				throw new DirectoryNotFoundException(templateDir);
			}

			try
			{
				EnsureDirectoryReadable(templateDir);
				return EnumerateDirectoryChildren(new DirectoryInfo(templateDir));
			}
			catch (Exception ex) when (IsFileSystemAccessException(ex))
			{
				if (TryPromptForNetworkCredentials(templateDir, ownerWindowHandle))
				{
					EnsureDirectoryReadable(templateDir);
					return EnumerateDirectoryChildren(new DirectoryInfo(templateDir));
				}

				throw;
			}
		}

		public string LoadTemplateByPath(string fullPath)
		{
			if (string.IsNullOrWhiteSpace(fullPath))
			{
				return string.Empty;
			}

			return File.ReadAllText(fullPath);
		}

		public string GetResolvedTemplateDirectory()
		{
			var config = _settingsService.Load();
			var configuredPath = config.Config.TemplateSettings.TemplateDirectory;
			if (string.IsNullOrWhiteSpace(configuredPath))
			{
				configuredPath = "template";
			}

			configuredPath = configuredPath.Trim();
			if (Path.IsPathFullyQualified(configuredPath))
			{
				return Path.GetFullPath(configuredPath);
			}

			return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configuredPath));
		}

		private static List<TemplateTreeNode> EnumerateDirectoryChildren(DirectoryInfo directory)
		{
			var nodes = new List<TemplateTreeNode>();

			foreach (var childDirectory in EnumerateDirectoriesSafely(directory)
				.Where(dir => !ShouldSkip(dir))
				.OrderBy(dir => dir.Name, StringComparer.CurrentCultureIgnoreCase))
			{
				nodes.Add(CreateDirectoryNode(childDirectory));
			}

			foreach (var file in EnumerateFilesSafely(directory)
				.Where(file => !ShouldSkip(file))
				.OrderBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase))
			{
				nodes.Add(new TemplateTreeNode
				{
					Name = file.Name,
					FullPath = file.FullName,
					IsDirectory = false
				});
			}

			return nodes;
		}

		private static TemplateTreeNode CreateDirectoryNode(DirectoryInfo directory)
		{
			var node = new TemplateTreeNode
			{
				Name = directory.Name,
				FullPath = directory.FullName,
				IsDirectory = true
			};

			try
			{
				foreach (var child in EnumerateDirectoryChildren(directory))
				{
					node.Children.Add(child);
				}
			}
			catch (Exception ex) when (IsFileSystemAccessException(ex))
			{
				node.ErrorMessage = "このフォルダーを開けません。";
				Debug.WriteLine($"テンプレートサブフォルダーを開けません: {directory.FullName} {ex.Message}");
			}

			return node;
		}

		private static void EnsureDirectoryReadable(string directoryPath)
		{
			Directory.EnumerateFileSystemEntries(directoryPath).Take(1).ToList();
		}

		private static IEnumerable<DirectoryInfo> EnumerateDirectoriesSafely(DirectoryInfo directory)
		{
			try
			{
				return directory.EnumerateDirectories().ToList();
			}
			catch (Exception ex) when (IsFileSystemAccessException(ex))
			{
				Debug.WriteLine($"テンプレートフォルダーの列挙に失敗しました: {directory.FullName} {ex.Message}");
				return Enumerable.Empty<DirectoryInfo>();
			}
		}

		private static IEnumerable<FileInfo> EnumerateFilesSafely(DirectoryInfo directory)
		{
			try
			{
				return directory.EnumerateFiles().ToList();
			}
			catch (Exception ex) when (IsFileSystemAccessException(ex))
			{
				Debug.WriteLine($"テンプレートファイルの列挙に失敗しました: {directory.FullName} {ex.Message}");
				return Enumerable.Empty<FileInfo>();
			}
		}

		private static bool ShouldSkip(FileSystemInfo info)
		{
			return info.Attributes.HasFlag(FileAttributes.Hidden)
				|| info.Attributes.HasFlag(FileAttributes.System);
		}

		private static bool IsFileSystemAccessException(Exception ex)
		{
			return ex is IOException
				|| ex is UnauthorizedAccessException
				|| ex is DirectoryNotFoundException
				|| ex is PathTooLongException
				|| ex is NotSupportedException;
		}

		private static bool TryPromptForNetworkCredentials(string path, IntPtr ownerWindowHandle)
		{
			var remoteName = GetNetworkResourceName(path);
			if (string.IsNullOrWhiteSpace(remoteName))
			{
				return false;
			}

			var resource = new NetResource
			{
				ResourceType = ResourceTypeDisk,
				RemoteName = remoteName
			};

			var bufferSize = 0;
			var result = WNetUseConnection(
				ownerWindowHandle,
				ref resource,
				null,
				null,
				ConnectInteractive | ConnectPrompt,
				null,
				ref bufferSize,
				out _);

			if (result != NoError)
			{
				Debug.WriteLine($"ネットワーク資格情報 UI の呼び出しに失敗しました: {remoteName} result={result}");
			}

			return result == NoError;
		}

		private static string? GetNetworkResourceName(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				return null;
			}

			if (path.StartsWith(@"\\", StringComparison.Ordinal))
			{
				return GetUncShareRoot(path);
			}

			var root = Path.GetPathRoot(path);
			if (string.IsNullOrWhiteSpace(root) || root.Length < 2)
			{
				return null;
			}

			var driveName = root.Substring(0, 2);
			try
			{
				var driveInfo = new DriveInfo(driveName);
				if (driveInfo.DriveType != DriveType.Network && driveInfo.DriveType != DriveType.NoRootDirectory)
				{
					return null;
				}
			}
			catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
			{
				Debug.WriteLine($"ドライブ種別の判定に失敗しました: {driveName} {ex.Message}");
			}

			return GetMappedRemoteName(driveName);
		}

		private static string? GetUncShareRoot(string path)
		{
			var parts = path.TrimStart('\\')
				.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);

			return parts.Length >= 2
				? $@"\\{parts[0]}\{parts[1]}"
				: null;
		}

		private static string? GetMappedRemoteName(string driveName)
		{
			var remoteName = new StringBuilder(512);
			var length = remoteName.Capacity;
			var result = WNetGetConnection(driveName, remoteName, ref length);

			if (result != NoError)
			{
				Debug.WriteLine($"ネットワークドライブのリモートパス解決に失敗しました: {driveName} result={result}");
				return null;
			}

			return remoteName.ToString();
		}

		[DllImport("mpr.dll", CharSet = CharSet.Unicode)]
		private static extern int WNetUseConnection(
			IntPtr hwndOwner,
			ref NetResource netResource,
			string? password,
			string? userId,
			int flags,
			StringBuilder? accessName,
			ref int bufferSize,
			out int result);

		[DllImport("mpr.dll", CharSet = CharSet.Unicode)]
		private static extern int WNetGetConnection(
			string localName,
			StringBuilder remoteName,
			ref int length);

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct NetResource
		{
			public int Scope;
			public int ResourceType;
			public int DisplayType;
			public int Usage;
			public string? LocalName;
			public string? RemoteName;
			public string? Comment;
			public string? Provider;
		}
	}
}
