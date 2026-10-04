using Microsoft.Extensions.Logging;

namespace M2Server.Lib.Infrastructure;

public sealed class DailyFileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly Lock _gate = new();
    private readonly Func<DateTimeOffset> _utcNow;
    private DateOnly? _day;

    public DailyFileLoggerProvider(string directory, Func<DateTimeOffset>? utcNow = null)
    {
        this._directory = directory;

        this._utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        lock (this._gate)
        {
            this.Prepare(DateOnly.FromDateTime(this._utcNow().UtcDateTime));
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(this, categoryName);
    }

    public void Dispose()
    {
    }

    private void Prepare(DateOnly day)
    {
        try
        {
            Directory.CreateDirectory(this._directory);
            foreach (var file in Directory.EnumerateFiles(this._directory, "activation-*.log"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.Length == 21 && DateOnly.TryParseExact(name[11..], "yyyy-MM-dd", out var fileDay) &&
                    fileDay < day.AddDays(-7))
                {
                    File.Delete(file);
                }
            }

            this._day = day;
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void Write(string category, LogLevel level, string message, Exception? exception)
    {
        lock (this._gate)
        {
            var now = this._utcNow();
            var day = DateOnly.FromDateTime(now.UtcDateTime);
            if (this._day != day)
            {
                this.Prepare(day);
            }

            try
            {
                Directory.CreateDirectory(this._directory);
                File.AppendAllText(
                    Path.Combine(this._directory, $"activation-{day:yyyy-MM-dd}.log"),
                    $"{now:O} [{level}] {category}: {message}{(exception is null ? "" : Environment.NewLine + exception)}{Environment.NewLine}"
                );
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class FileLogger(DailyFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull
        {
            return EmptyScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= LogLevel.Information && logLevel != LogLevel.None;
        }

        public void Log<TState>(
            LogLevel level,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (this.IsEnabled(level))
            {
                provider.Write(category, level, formatter(state, exception), exception);
            }
        }
    }

    private sealed class EmptyScope : IDisposable
    {
        public static readonly EmptyScope Instance = new();

        public void Dispose()
        {
        }
    }
}