// Core/Controls/DateSeparatorControl.xaml.cs
using System;
using System.Windows;
using System.Windows.Controls;

namespace AIChatHelper.Core.Controls
{
	public partial class DateSeparatorControl : UserControl
	{
		public static readonly DependencyProperty DateProperty =
			DependencyProperty.Register(
				nameof(Date),
				typeof(DateTime),
				typeof(DateSeparatorControl),
				new PropertyMetadata(DateTime.Now, OnDateChanged));

		public DateTime Date
		{
			get => (DateTime)GetValue(DateProperty);
			set => SetValue(DateProperty, value);
		}

		public DateSeparatorControl()
		{
			InitializeComponent();
			UpdateDateText();

			// 親コンテナの幅に合わせて自動調整
			HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
		}

		private static void OnDateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
		{
			if (d is DateSeparatorControl control)
			{
				control.UpdateDateText();
			}
		}

		private void UpdateDateText()
		{
			DateTime now = DateTime.Now;
			TimeSpan diff = now - Date;

			if (diff.TotalDays < 1)
			{
				// 今日
				DateText.Text = "今日";
			}
			else if (diff.TotalDays < 2)
			{
				// 昨日
				DateText.Text = "昨日";
			}
			else if (diff.TotalDays < 7)
			{
				// 1週間以内
				DateText.Text = Date.ToString("M月d日(ddd)");
			}
			else if (diff.TotalDays < 365)
			{
				// 1年以内
				DateText.Text = Date.ToString("M月d日");
			}
			else
			{
				// 1年以上前
				DateText.Text = Date.ToString("yyyy年M月d日");
			}
		}
	}
}