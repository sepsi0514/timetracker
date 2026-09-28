using System.Windows;

namespace TimeTracker;

public partial class DeleteConfirmationWindow : Window
{
    public DeleteConfirmationWindow(string itemName)
    {
        InitializeComponent();
        ItemNameText.Text = itemName;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}