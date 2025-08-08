namespace Cocoar.Configuration;

public interface IConfigLogger
{
    void Debug(string message, params object[] args);
    void Information(string message, params object[] args);
    void Warning(Exception? ex, string message, params object[] args);
    void Error(Exception ex, string message, params object[] args);
}

public sealed class NullConfigLogger : IConfigLogger
{
    public static readonly NullConfigLogger Instance = new();
    private NullConfigLogger() { }
    public void Debug(string message, params object[] args) { }
    public void Information(string message, params object[] args) { }
    public void Warning(Exception? ex, string message, params object[] args) { }
    public void Error(Exception ex, string message, params object[] args) { }
}
