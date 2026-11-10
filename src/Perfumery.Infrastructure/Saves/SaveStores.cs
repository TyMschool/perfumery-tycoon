using Perfumery.Application.Abstractions.Game;

namespace Perfumery.Infrastructure.Saves;

public sealed class SaveOptions
{
    /// <summary>Where the save file lives. Defaults to the user's local application data.</summary>
    public string Path { get; set; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PerfumeryTycoon",
        "save.json");

    /// <summary>Keep the save in memory only. For tests and throwaway sessions.</summary>
    public bool InMemory { get; set; }

    /// <summary>The file is written at most once per this interval; later writes within it are coalesced.</summary>
    public TimeSpan MinWriteInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Overrides the time scale from <c>atelier.era1.json</c>, e.g. 60 for playtesting.</summary>
    public decimal? TimeScale { get; set; }
}

/// <summary>
/// Saves to one file. A write goes to a temporary file that is then renamed over the
/// save, so a crash mid-write cannot corrupt it. Writes are coalesced to at most one per
/// <see cref="SaveOptions.MinWriteInterval"/> and flushed when the store is disposed.
/// </summary>
internal sealed class FileSaveStore : ISaveStore, IAsyncDisposable
{
    private readonly string _path;
    private readonly TimeSpan _minInterval;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ITimer _timer;

    private string? _pending;
    private long? _lastWriteTimestamp;
    private bool _timerArmed;

    public FileSaveStore(SaveOptions options, TimeProvider timeProvider)
    {
        _path = System.IO.Path.GetFullPath(options.Path);
        _minInterval = options.MinWriteInterval;
        _timeProvider = timeProvider;
        _timer = timeProvider.CreateTimer(
            _ => FlushAsync(CancellationToken.None).GetAwaiter().GetResult(),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
    }

    /// <summary>How many times the file has actually been written.</summary>
    internal int FileWrites { get; private set; }

    public async Task<string?> ReadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_pending is not null)
            {
                return _pending;
            }

            return File.Exists(_path) ? await File.ReadAllTextAsync(_path, cancellationToken) : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WriteAsync(string content, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _pending = content;

            var sinceLast = _lastWriteTimestamp is { } last ? _timeProvider.GetElapsedTime(last) : TimeSpan.MaxValue;
            if (sinceLast >= _minInterval)
            {
                await WritePendingAsync(cancellationToken);
            }
            else if (!_timerArmed)
            {
                _timerArmed = true;
                _timer.Change(_minInterval - sinceLast, Timeout.InfiniteTimeSpan);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await WritePendingAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetAsideAsync(CancellationToken cancellationToken)
    {
        await FlushAsync(cancellationToken);
        if (File.Exists(_path))
        {
            File.Copy(_path, System.IO.Path.ChangeExtension(_path, ".corrupt.json"), overwrite: true);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await FlushAsync(CancellationToken.None);
        await _timer.DisposeAsync();
        _gate.Dispose();
    }

    private async Task WritePendingAsync(CancellationToken cancellationToken)
    {
        _timerArmed = false;
        if (_pending is null)
        {
            return;
        }

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        await File.WriteAllTextAsync(temporary, _pending, cancellationToken);
        File.Move(temporary, _path, overwrite: true);

        _pending = null;
        _lastWriteTimestamp = _timeProvider.GetTimestamp();
        FileWrites++;
    }
}

/// <summary>Keeps the save in memory. Nothing survives the process.</summary>
internal sealed class InMemorySaveStore : ISaveStore
{
    public string? Content { get; set; }

    public string? SetAside { get; private set; }

    public int Writes { get; private set; }

    public Task<string?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(Content);

    public Task WriteAsync(string content, CancellationToken cancellationToken)
    {
        Content = content;
        Writes++;
        return Task.CompletedTask;
    }

    public Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task SetAsideAsync(CancellationToken cancellationToken)
    {
        SetAside = Content;
        return Task.CompletedTask;
    }
}
