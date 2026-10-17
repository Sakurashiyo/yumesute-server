sealed class RealtimeFileLoggerProvider(LocalRequestLogger sink) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new FileLogger(sink, categoryName);

    public void Dispose() { }

    sealed class FileLogger(LocalRequestLogger sink, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel level) => level != LogLevel.None &&
            (category.StartsWith("Realtime", StringComparison.Ordinal) ||
             category.StartsWith("Grpc.", StringComparison.Ordinal) ||
             category is nameof(MultiLiveRealtimeService) or nameof(SocialRealtimeService));

        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            var message = $"level={level} category={category} eventId={eventId.Id} {formatter(state, exception)}";
            if (exception is not null) message += $"{Environment.NewLine}{exception}";
            sink.Log(message);
        }
    }
}
