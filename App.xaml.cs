using System.Configuration;
using System.Data;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using AIChatHelper.Core.Services;

namespace AIChatHelper;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
	private static Mutex? mutex = null;
	private readonly IServiceProvider _serviceProvider;

	public App()
	{
		var services = new ServiceCollection();
		ConfigureServices(services);
		_serviceProvider = services.BuildServiceProvider();
	}

	protected override void OnStartup(StartupEventArgs e)
	{
		// 未処理例外のキャッチ
		AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
		{
			Exception? exception = args.ExceptionObject as Exception;
			if (exception != null)
			{
				MessageBox.Show(exception.ToString(), "未処理例外発生", MessageBoxButton.OK, MessageBoxImage.Error);
				Application.Current.Shutdown();
			}
		};

		DispatcherUnhandledException += (sender, args) =>
		{
			MessageBox.Show(args.Exception.ToString(), "Dispatcher未処理例外発生", MessageBoxButton.OK, MessageBoxImage.Error);
			args.Handled = true; // 例外処理済みとする
			Application.Current.Shutdown();

		};

		base.OnStartup(e);
		var mainWindow = _serviceProvider.GetRequiredService<Views.MainWindow>();
		mainWindow.Show();
	}

	protected override void OnExit(ExitEventArgs e)
	{
		if (mutex != null)
		{
			mutex.ReleaseMutex();
			mutex.Dispose();
		}
		base.OnExit(e);
	}
	private void ConfigureServices(ServiceCollection services)
	{
		// ファクトリの登録
		services.AddSingleton<Core.Factory.IWindowFactory, Core.Factory.WindowFactory>();

		// 各データサービスの登録
		// 設定サービスの登録
		services.AddSingleton<ISettingsService, SettingsService>();
		// 履歴サービスの登録
		services.AddSingleton<IHistoryService, HistoryService>();
		// テンプレートサービスを登録
		services.AddSingleton<ITemplateService, TemplateService>();
		// ライセンスサービスの登録
		services.AddSingleton<ILicenseService, LicenseService>();

		// 各ウィンドウの登録
		services.AddTransient<Views.MainWindow>();
		services.AddTransient<ViewModels.MainWindowViewModel>();
		services.AddTransient<Views.HistoryManagementWindow>();
		services.AddTransient<ViewModels.HistoryManagementWindowViewModel>();
		services.AddTransient<Views.InformationWindow>();
		services.AddTransient<ViewModels.InformationWindowViewModel>();
		// 他のサービスやウィンドウもここに登録
	}
}

