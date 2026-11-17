namespace BlazorStoc.Checks;

// A progress that calls its handler at once, on the thread that reports (Progress<T> posts to the thread pool, so a test cannot rely on the order or on the last value).
internal sealed class ImmediateProgress(Action<int> handler) : IProgress<int>
{
    public void Report(int value) => handler(value);
}