using System.Collections;
using System.Collections.Specialized;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace CdcExtractor.App.Controls;

/// <summary>
/// A scrollable log viewer that displays log entries color-coded by severity
/// with search, auto-scroll, and pause functionality.
/// </summary>
public partial class LogConsole : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(LogConsole),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty IsAutoScrollEnabledProperty =
        DependencyProperty.Register(
            nameof(IsAutoScrollEnabled),
            typeof(bool),
            typeof(LogConsole),
            new PropertyMetadata(true));

    public static readonly DependencyProperty IsPausedProperty =
        DependencyProperty.Register(
            nameof(IsPaused),
            typeof(bool),
            typeof(LogConsole),
            new PropertyMetadata(false));

    public static readonly DependencyProperty SearchTextProperty =
        DependencyProperty.Register(
            nameof(SearchText),
            typeof(string),
            typeof(LogConsole),
            new PropertyMetadata(string.Empty, OnFilterChanged));

    public static readonly DependencyProperty MinLevelProperty =
        DependencyProperty.Register(
            nameof(MinLevel),
            typeof(string),
            typeof(LogConsole),
            new PropertyMetadata("Information", OnFilterChanged));

    private bool _userScrolledUp;

    public LogConsole()
    {
        InitializeComponent();
    }

    public IEnumerable ItemsSource
    {
        get => (IEnumerable)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public bool IsAutoScrollEnabled
    {
        get => (bool)GetValue(IsAutoScrollEnabledProperty);
        set => SetValue(IsAutoScrollEnabledProperty, value);
    }

    public bool IsPaused
    {
        get => (bool)GetValue(IsPausedProperty);
        set => SetValue(IsPausedProperty, value);
    }

    public string SearchText
    {
        get => (string)GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    public string MinLevel
    {
        get => (string)GetValue(MinLevelProperty);
        set => SetValue(MinLevelProperty, value);
    }

    /// <summary>
    /// Scrolls the log viewer to the bottom.
    /// </summary>
    public void ScrollToBottom()
    {
        LogScrollViewer.ScrollToEnd();
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not LogConsole console)
        {
            return;
        }

        // Unsubscribe from old collection
        if (e.OldValue is INotifyCollectionChanged oldCollection)
        {
            oldCollection.CollectionChanged -= console.OnCollectionChanged;
        }

        // Subscribe to new collection
        if (e.NewValue is INotifyCollectionChanged newCollection)
        {
            newCollection.CollectionChanged += console.OnCollectionChanged;
        }

        console.ApplyFilter();
    }

    private static void OnFilterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is LogConsole console)
        {
            console.ApplyFilter();
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (IsPaused)
        {
            return;
        }

        ApplyFilter();

        if (IsAutoScrollEnabled && !_userScrolledUp)
        {
            Dispatcher.InvokeAsync(ScrollToBottom, System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void ApplyFilter()
    {
        if (ItemsSource is null)
        {
            LogItemsControl.ItemsSource = null;
            return;
        }

        var filtered = new List<object>();
        var searchText = (SearchText ?? string.Empty).Trim();
        var minLevel = (MinLevel ?? "Information").Trim().ToUpperInvariant();

        foreach (var item in ItemsSource)
        {
            if (item is null)
            {
                continue;
            }

            // Apply level filter
            if (minLevel != "ALL" && !PassesLevelFilter(item, minLevel))
            {
                continue;
            }

            // Apply search filter
            if (!string.IsNullOrEmpty(searchText) && !PassesSearchFilter(item, searchText))
            {
                continue;
            }

            filtered.Add(item);
        }

        LogItemsControl.ItemsSource = filtered;
    }

    private static bool PassesLevelFilter(object item, string minLevel)
    {
        var level = GetPropertyValue(item, "Level")?.ToUpperInvariant() ?? string.Empty;
        var minRank = GetLevelRank(minLevel);
        var itemRank = GetLevelRank(level);

        return itemRank >= minRank;
    }

    private static int GetLevelRank(string level)
    {
        return level switch
        {
            "DBG" or "DEBUG" => 0,
            "INF" or "INFO" or "INFORMATION" => 1,
            "WRN" or "WARN" or "WARNING" => 2,
            "ERR" or "ERROR" => 3,
            _ => 1,
        };
    }

    private static bool PassesSearchFilter(object item, string searchText)
    {
        var message = GetPropertyValue(item, "Message") ?? string.Empty;
        return message.Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetPropertyValue(object item, string propertyName)
    {
        var prop = item.GetType().GetProperty(propertyName);
        return prop?.GetValue(item)?.ToString();
    }

    private void SeverityFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SeverityFilter.SelectedItem is ComboBoxItem selected)
        {
            MinLevel = selected.Content?.ToString() ?? "Information";
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchText = SearchBox.Text;
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        IsPaused = !IsPaused;
        PauseButton.Content = IsPaused ? "Resume" : "Pause";

        if (!IsPaused)
        {
            ApplyFilter();

            if (IsAutoScrollEnabled)
            {
                Dispatcher.InvokeAsync(ScrollToBottom, System.Windows.Threading.DispatcherPriority.Background);
            }
        }
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        var text = BuildLogText();
        if (!string.IsNullOrEmpty(text))
        {
            Clipboard.SetText(text);
        }
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Text files (*.txt)|*.txt|Log files (*.log)|*.log|All files (*.*)|*.*",
            DefaultExt = ".txt",
            FileName = $"log-export-{DateTime.Now:yyyyMMdd-HHmmss}",
        };

        if (dialog.ShowDialog() == true)
        {
            var text = BuildLogText();
            System.IO.File.WriteAllText(dialog.FileName, text);
        }
    }

    private string BuildLogText()
    {
        var sb = new StringBuilder();
        var source = LogItemsControl.ItemsSource ?? ItemsSource;
        if (source is null)
        {
            return string.Empty;
        }

        foreach (var item in source)
        {
            if (item is null)
            {
                continue;
            }

            var timestamp = GetPropertyValue(item, "Timestamp") ?? string.Empty;
            var level = GetPropertyValue(item, "Level") ?? string.Empty;
            var message = GetPropertyValue(item, "Message") ?? string.Empty;
            sb.AppendLine($"{timestamp} [{level}] {message}");
        }

        return sb.ToString();
    }

    private void LogScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        // Detect if user scrolled up manually
        if (e.ExtentHeightChange == 0)
        {
            // User initiated scroll
            var atBottom = LogScrollViewer.VerticalOffset >=
                           LogScrollViewer.ScrollableHeight - 10;
            _userScrolledUp = !atBottom;
        }
        else if (IsAutoScrollEnabled && !_userScrolledUp)
        {
            // Content changed, auto-scroll if enabled
            LogScrollViewer.ScrollToEnd();
        }
    }
}
