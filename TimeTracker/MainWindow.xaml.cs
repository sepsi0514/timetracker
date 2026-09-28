using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace TimeTracker;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<WorkItem> _workItems = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private ICollectionView _workItemsView = null!;
    private TimeTrackerDatabase? _database;
    private ActiveSession? _activeSession;

    public MainWindow()
    {
        InitializeComponent();
        Icon = (System.Windows.Media.ImageSource)FindResource("ClockIcon");
        _workItemsView = CollectionViewSource.GetDefaultView(_workItems);
        _workItemsView.Filter = WorkItemMatchesSearch;
        TasksGrid.ItemsSource = _workItemsView;
        _timer.Tick += (_, _) => UpdateSessionDisplay();
        _timer.Start();
        UpdateControls();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var savedPath = GetSavedDatabasePath();
        if (!string.IsNullOrWhiteSpace(savedPath) && File.Exists(savedPath))
        {
            LoadDatabase(savedPath);
        }
    }

    private void SelectDatabase_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a time-tracker database",
            Filter = "SQLite database (*.db;*.sqlite;*.sqlite3)|*.db;*.sqlite;*.sqlite3|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) == true)
        {
            LoadDatabase(dialog.FileName);
        }
    }

    private void CreateDatabase_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Create a time-tracker database",
            Filter = "SQLite database (*.db)|*.db",
            DefaultExt = ".db",
            AddExtension = true,
            OverwritePrompt = false,
            FileName = "timetracker.db"
        };

        if (dialog.ShowDialog(this) == true)
        {
            if (File.Exists(dialog.FileName))
            {
                ShowError("That file already exists. Choose a new name, or use Select database to open it.");
                return;
            }

            LoadDatabase(dialog.FileName);
        }
    }

    private void LoadDatabase(string path)
    {
        try
        {
            var database = new TimeTrackerDatabase(path);
            var activeSession = database.GetActiveSession();
            _database = database;
            _activeSession = activeSession;
            DatabasePathText.Text = path;
            DatabasePathText.ToolTip = path;
            SaveDatabasePath(path);
            RefreshWorkItems();
            UpdateControls();
        }
        catch (Exception exception)
        {
            ShowError($"Could not open the database.\n{exception.Message}");
        }
    }

    private void AddTask_Click(object sender, RoutedEventArgs e)
    {
        if (_database is null)
        {
            return;
        }

        var name = NewTaskTextBox.Text.Trim();
        if (name.Length == 0)
        {
            return;
        }

        try
        {
            _database.AddWorkItem(name);
            NewTaskTextBox.Clear();
            RefreshWorkItems();
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            ShowError("An item with that name already exists.");
        }
        catch (Exception exception)
        {
            ShowError($"Could not add the item.\n{exception.Message}");
        }
    }

    private void DeleteTask_Click(object sender, RoutedEventArgs e)
    {
        if (_database is null || sender is not System.Windows.Controls.Button { Tag: WorkItem item })
        {
            return;
        }

        if (_activeSession?.WorkItemId == item.Id)
        {
            ShowError("Stop this item's active session before deleting it.");
            return;
        }

        var confirmation = new DeleteConfirmationWindow(item.Name)
        {
            Owner = this
        };
        if (confirmation.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _database.DeleteWorkItem(item.Id);
            RefreshWorkItems();
        }
        catch (Exception exception)
        {
            ShowError($"Could not delete the item.\n{exception.Message}");
        }
    }

    private void NewTaskTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && AddTaskButton.IsEnabled)
        {
            AddTask_Click(sender, e);
        }
    }

    private void NewTaskTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        AddTaskButton.IsEnabled = _database is not null && !string.IsNullOrWhiteSpace(NewTaskTextBox.Text);
    }

    private void SearchTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _workItemsView.Refresh();
    }

    private bool WorkItemMatchesSearch(object item)
    {
        return item is WorkItem workItem &&
            workItem.Name.Contains(SearchTextBox.Text.Trim(), StringComparison.CurrentCultureIgnoreCase);
    }

    private void TasksGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateControls();
    }

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_database is null || TasksGrid.SelectedItem is not WorkItem selectedItem || _activeSession is not null)
        {
            return;
        }

        try
        {
            _activeSession = _database.StartSession(selectedItem);
            UpdateControls();
        }
        catch (Exception exception)
        {
            ShowError($"Could not start the session.\n{exception.Message}");
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (_database is null || _activeSession is null)
        {
            return;
        }

        try
        {
            var minutes = _database.StopSession(_activeSession);
            _activeSession = null;
            RefreshWorkItems();
            UpdateControls();
            SessionStatusText.Text = $"Session saved: {minutes} minute{(minutes == 1 ? "" : "s")}.";
        }
        catch (Exception exception)
        {
            ShowError($"Could not stop the session.\n{exception.Message}");
        }
    }

    private void RefreshWorkItems()
    {
        if (_database is null)
        {
            _workItems.Clear();
            return;
        }

        var selectedId = (TasksGrid.SelectedItem as WorkItem)?.Id;
        var items = _database.GetWorkItems();
        _workItems.Clear();
        foreach (var item in items)
        {
            _workItems.Add(item);
        }

        TasksGrid.SelectedItem = _workItems.FirstOrDefault(item => item.Id == selectedId)
            ?? _workItems.FirstOrDefault(item => item.Id == _activeSession?.WorkItemId);
        UpdateControls();
    }

    private void UpdateControls()
    {
        var selectedItem = TasksGrid.SelectedItem as WorkItem;
        var hasDatabase = _database is not null;
        var isRunning = _activeSession is not null;

        SelectDatabaseButton.IsEnabled = !isRunning;
        CreateDatabaseButton.IsEnabled = !isRunning;
        NewTaskTextBox.IsEnabled = hasDatabase;
        SearchTextBox.IsEnabled = hasDatabase;
        AddTaskButton.IsEnabled = hasDatabase && !string.IsNullOrWhiteSpace(NewTaskTextBox.Text);
        TasksGrid.IsEnabled = hasDatabase;
        StartButton.IsEnabled = hasDatabase && selectedItem is not null && !isRunning;
        StopButton.IsEnabled = isRunning;

        if (_activeSession is not null)
        {
            CurrentItemText.Text = _activeSession.WorkItemName;
            SessionStatusText.Text = "Session running. Stop it to save the time.";
            UpdateSessionDisplay();
        }
        else
        {
            CurrentItemText.Text = selectedItem?.Name ?? "Choose an item to get started";
            SessionTimeText.Text = "00:00:00";
            if (!hasDatabase)
            {
                SessionStatusText.Text = "Select or create a database to begin.";
            }
            else
            {
                SessionStatusText.Text = selectedItem is null
                    ? "Select a work item from the list."
                    : "Ready to track this item.";
            }
        }
    }

    private void UpdateSessionDisplay()
    {
        if (_activeSession is null)
        {
            return;
        }

        var elapsed = DateTimeOffset.UtcNow - _activeSession.StartedAt;
        SessionTimeText.Text = $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        CurrentItemText.Text = _activeSession.WorkItemName;
    }

    private void ShowError(string message)
    {
        MessageBox.Show(this, message, "TimeTracker", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TimeTracker");

    private static string SettingsFilePath => Path.Combine(SettingsDirectory, "settings.txt");

    private static string? GetSavedDatabasePath()
    {
        try
        {
            return File.Exists(SettingsFilePath) ? File.ReadAllText(SettingsFilePath).Trim() : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void SaveDatabasePath(string path)
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsFilePath, path);
        }
        catch (IOException)
        {
            // Database selection still works when settings cannot be persisted.
        }
        catch (UnauthorizedAccessException)
        {
            // Database selection still works when settings cannot be persisted.
        }
    }
}