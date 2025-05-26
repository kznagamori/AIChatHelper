//HistoryManagementWindow.xaml.cs
using System.Windows;
using AIChatHelper.Core.Services;
using AIChatHelper.ViewModels;

namespace AIChatHelper.Views
{
	public partial class HistoryManagementWindow : Window
	{
		public HistoryManagementWindow(ViewModels.HistoryManagementWindowViewModel viewModel)
		{
			InitializeComponent();
			DataContext = viewModel;
		}
	}
}
