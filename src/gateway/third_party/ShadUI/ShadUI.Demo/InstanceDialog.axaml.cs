using Avalonia.Interactivity;

namespace ShadUI.Demo;

public partial class InstanceDialog : Window
{
    public InstanceDialog()
    {
        InitializeComponent();
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        Close();
    }
}