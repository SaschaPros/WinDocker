using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Docker.DotNet;
using WinDocker.Core.Docker;
using WinDocker.Core.Localization;
using WinDocker.Core.Services;

namespace WinDocker.Core.ViewModels;

public abstract partial class PageViewModelBase : ObservableObject
{
    private int busyCount;

    protected PageViewModelBase(ILocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        Localizer = localizer;
    }

    protected ILocalizer Localizer { get; }

    /// <summary>True while at least one operation is running.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; protected set; }

    /// <summary>
    /// True while <see cref="ErrorMessage"/> is set. Setting it to <see langword="false"/> dismisses the error,
    /// so it can be bound two-way to <c>InfoBar.IsOpen</c>.
    /// </summary>
    public bool HasError
    {
        get => ErrorMessage is not null;
        set
        {
            if (!value)
            {
                ErrorMessage = null;
            }
        }
    }

    /// <summary>
    /// Runs <paramref name="action"/> with <see cref="IsBusy"/> set, after clearing the previous error.
    /// A failure becomes <see cref="ErrorMessage"/> instead of an exception.
    /// </summary>
    protected async Task RunSafeAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        BeginBusy();
        ErrorMessage = null;
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ErrorMessage = DescribeError(exception);
        }
        finally
        {
            EndBusy();
        }
    }

    /// <summary>
    /// Runs a change (start, stop, remove) and then reloads the list. When the engine rejected the change,
    /// the list may be out of date, so it is reloaded as well and the engine's message stays visible.
    /// </summary>
    protected Task RunMutationAsync(Func<Task> mutation, Func<Task> reload) =>
        RunSafeAsync(async () =>
        {
            try
            {
                await mutation();
            }
            catch (DockerApiException)
            {
                try
                {
                    await reload();
                }
                catch (Exception)
                {
                    // Keep the original error.
                }

                throw;
            }

            await reload();
        });

    protected string DescribeError(Exception exception) => exception switch
    {
        DockerUnavailableException => Localizer.GetString(ResourceKeys.ErrorDockerUnavailable),
        DockerApiException apiException => DockerFormat.DaemonMessage(apiException.ResponseBody) ?? apiException.Message,
        _ => exception.Message,
    };

    protected void BeginBusy()
    {
        if (Interlocked.Increment(ref busyCount) == 1)
        {
            IsBusy = true;
        }
    }

    protected void EndBusy()
    {
        if (Interlocked.Decrement(ref busyCount) == 0)
        {
            IsBusy = false;
        }
    }

    protected static void ReplaceAll<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}
