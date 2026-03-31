namespace MicroClaw.RAG;

/// <summary>
/// 全量重索引任务的进度追踪器（单例，线程安全）。
/// </summary>
public sealed class RagReindexJobTracker
{
    private ReindexJobStatus _status = ReindexJobStatus.Idle;
    private int _total;
    private int _completed;
    private string? _currentItem;
    private string? _error;

    public ReindexJobStatus Status => _status;
    public int Total => _total;
    public int Completed => _completed;
    public string? CurrentItem => _currentItem;
    public string? Error => _error;

    public void Reset()
    {
        _status = ReindexJobStatus.Idle;
        _total = 0;
        _completed = 0;
        _currentItem = null;
        _error = null;
    }

    public void Start(int total)
    {
        _total = total;
        _completed = 0;
        _currentItem = null;
        _error = null;
        _status = ReindexJobStatus.Running;
    }

    public void Increment(string item)
    {
        _currentItem = item;
        Interlocked.Increment(ref _completed);
    }

    public void Complete()
    {
        _currentItem = null;
        _status = ReindexJobStatus.Done;
    }

    public void Fail(string error)
    {
        _error = error;
        _status = ReindexJobStatus.Error;
    }
}

public enum ReindexJobStatus
{
    Idle,
    Running,
    Done,
    Error,
}
