// SettingsWindow.xaml.cs
using System.Windows;

namespace AIChatHelper.Views;

public partial class SettingsWindow : Window
{
	public SettingsWindow(ViewModels.SettingsWindowViewModel viewModel)
	{
		InitializeComponent();
		DataContext = viewModel;
	}
}
