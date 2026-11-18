using System.Windows;
using System.Windows.Input;
using LANVC.Client.ViewModels;

namespace LANVC.Client;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (ServerPasswordBox.Visibility == Visibility.Visible)
        {
            ViewModel.Password = ServerPasswordBox.Password;
        }
    }

    private void TogglePassword_Click(object sender, RoutedEventArgs e)
    {
        if (ServerPasswordBox.Visibility == Visibility.Visible)
        {
            ServerPlainTextBox.Text = ServerPasswordBox.Password;
            ServerPasswordBox.Visibility = Visibility.Collapsed;
            ServerPlainTextBox.Visibility = Visibility.Visible;
            ServerPlainTextBox.Focus();
            ServerPlainTextBox.CaretIndex = ServerPlainTextBox.Text.Length;
        }
        else
        {
            ServerPasswordBox.Password = ServerPlainTextBox.Text;
            ServerPlainTextBox.Visibility = Visibility.Collapsed;
            ServerPasswordBox.Visibility = Visibility.Visible;
            ServerPasswordBox.Focus();
        }
    }
}
