using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace MusicBoxManagement.Wpf.Views
{
    // Explicit command boundary: no global exception handler or automatic status popups.
    internal static class UserFeedback
    {
        private static readonly HashSet<Window> Running = new HashSet<Window>();

        internal static Task RunAsync(Window owner, Action action, Func<string> status)
        {
            return RunAsync(owner, () => { action(); return Task.CompletedTask; }, status);
        }

        internal static async Task RunAsync(Window owner, Func<Task> action, Func<string> status)
        {
            if (!owner.IsVisible || !Running.Add(owner)) return;
            try
            {
                await action();
                Show(owner, status());
            }
            catch (Exception error)
            {
                System.Diagnostics.Trace.TraceError("UI command failed: {0}", error.GetType().FullName);
                var expected = error is ArgumentException || error is InvalidOperationException || error is UnauthorizedAccessException;
                Show(owner, expected ? error.Message : "Không thực hiện được thao tác. Vui lòng kiểm tra trạng thái trước khi thử lại hoặc liên hệ nhân viên.");
            }
            finally { Running.Remove(owner); }
        }

        internal static void Show(Window owner, string message)
        {
            if (!owner.IsVisible || string.IsNullOrWhiteSpace(message)) return;
            var dialog = new Window
            {
                Name = "UserFeedbackDialog", Title = "Music Box — Thông báo", Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Width = 480, MaxWidth = SystemParameters.WorkArea.Width - 32,
                MaxHeight = SystemParameters.WorkArea.Height - 48,
                SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false, FontSize = 14, FontFamily = owner.FontFamily,
                Background = owner.Background
            };
            NameScope.SetNameScope(dialog, new NameScope());
            var panel = new StackPanel { Margin = new Thickness(24) };
            var text = new TextBlock { Name = "MessageText", Text = message, TextWrapping = TextWrapping.Wrap, LineHeight = 23 };
            panel.Children.Add(new ScrollViewer { Content = text, MaxHeight = Math.Max(100, SystemParameters.WorkArea.Height - 230), VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var accept = new Button
            {
                Name = "AcceptButton", Content = "Đã hiểu", IsDefault = true, IsCancel = true,
                MinWidth = 100, Margin = new Thickness(0, 20, 0, 0), HorizontalAlignment = HorizontalAlignment.Right
            };
            accept.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
            accept.Click += (sender, args) => dialog.Close();
            panel.Children.Add(accept); dialog.Content = panel;
            dialog.RegisterName(text.Name, text); dialog.RegisterName(accept.Name, accept);
            dialog.Loaded += (sender, args) => accept.Focus();
            dialog.ShowDialog();
        }
    }
}
