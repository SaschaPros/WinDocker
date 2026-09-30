namespace WinDocker.Core.Tests.Support;

internal static class TestWait
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>Awaits the task but fails the test with a <see cref="TimeoutException"/> instead of hanging.</summary>
    public static Task Within(this Task task) => task.WaitAsync(Timeout);

    public static Task<T> Within<T>(this Task<T> task) => task.WaitAsync(Timeout);
}
