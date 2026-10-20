namespace BlazorStoc.Updater;

/// <summary>The log of the updater: the console and logs/updater-yyyyMMdd.log of the installation (what happened, in order, to be read after a failure).</summary>
public sealed class FileUpdateLog(string logsFolder) : IUpdateLog
{
    private readonly object gate = new();

    public void Info(string message) => Write("INFO ", message, Console.Out);
    public void Warn(string message) => Write("WARN ", message, Console.Out);
    public void Error(string message) => Write("ERROR", message, Console.Error);

    private void Write(string level, string message, TextWriter console)
    {
        foreach (var line in message.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var text = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {line}";
            console.WriteLine(text);
            lock (gate)
            {
                try
                {
                    Directory.CreateDirectory(logsFolder);
                    File.AppendAllText(Path.Combine(logsFolder, $"updater-{DateTime.Now:yyyyMMdd}.log"), text + Environment.NewLine);
                }
                catch (IOException) { /* a log that cannot be written must not stop the update */ }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
