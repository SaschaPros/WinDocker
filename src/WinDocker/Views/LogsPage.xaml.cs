using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WinDocker.Core.ViewModels;

namespace WinDocker.Views;

public sealed partial class LogsPage : Page
{
    public LogsPage()
    {
        ViewModel = App.Services.GetRequiredService<LogsViewModel>();
        InitializeComponent();

        // The selection is set here rather than bound: an int cannot be bound two-way to a ComboBox's
        // SelectedItem, which is null while nothing is selected.
        TailComboBox.ItemsSource = ViewModel.TailOptions;
        TailComboBox.SelectedIndex = IndexOf(ViewModel.TailOptions, ViewModel.Tail);
    }

    public LogsViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not LogsPageParameter parameter)
        {
            return;
        }

        ViewModel.Initialize(parameter.ContainerId, parameter.ContainerName);
        ViewModel.LinesAppended += OnLinesAppended;
        ViewModel.RefreshCommand.Execute(null);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.LinesAppended -= OnLinesAppended;
        ViewModel.Stop();
    }

    private static int IndexOf(IReadOnlyList<int> values, int value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] == value)
            {
                return index;
            }
        }

        return -1;
    }

    private void TailComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TailComboBox.SelectedItem is int tail)
        {
            ViewModel.Tail = tail;
        }
    }

    private void OnLinesAppended(object? sender, EventArgs e)
    {
        // Stay at the end while following, and show the end of a freshly loaded snapshot.
        if ((ViewModel.IsFollowing || ViewModel.IsBusy) && ViewModel.VisibleLines.Count > 0)
        {
            LogList.ScrollIntoView(ViewModel.VisibleLines[^1]);
        }
    }
}
