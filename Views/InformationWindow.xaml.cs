//InformationWindow.xaml.cs
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using AIChatHelper.ViewModels;

namespace AIChatHelper.Views
{
	public partial class InformationWindow : Window
	{
		public InformationWindow(InformationWindowViewModel viewModel)
		{
			InitializeComponent();
			DataContext = viewModel;
		}

		private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
		{
			// URLを既定のブラウザで開く
			Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
			{
				UseShellExecute = true
			});
			e.Handled = true;
		}
	}
}