using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MicroClaw.Infrastructure.Data;

/// <summary>
/// Thread-safe in-memory store backed by a YAML file.
/// Loads from file on construction; every write operation persists to disk synchronously.
/// </summary>
public abstract class YamlFileStore<T> where T : class
{
    private readonly string _filePath;
    private readonly Func<T, string> _getId;
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private readonly Dictionary<string, T> _items = new(StringComparer.Ordinal);

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.Preserve)
        .Build();

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    protected YamlFileStore(string filePath, Func<T, string> getId)
    {
        _filePath = filePath;
        _getId = getId;
        LoadFromFile();
    }

    protected IReadOnlyList<T> GetAll()
    {
        _lock.EnterReadLock();
        try { return [.. _items.Values]; }
        finally { _lock.ExitReadLock(); }
    }

    protected T? GetYamlById(string id)
    {
        _lock.EnterReadLock();
        try { return _items.GetValueOrDefault(id); }
        finally { _lock.ExitReadLock(); }
    }

    protected bool ContainsYaml(string id)
    {
        _lock.EnterReadLock();
        try { return _items.ContainsKey(id); }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>Add or replace an item (keyed by its ID). Persists to disk.</summary>
    protected T SetYaml(T item)
    {
        _lock.EnterWriteLock();
        try
        {
            _items[_getId(item)] = item;
            Persist();
        }
        finally { _lock.ExitWriteLock(); }
        return item;
    }

    /// <summary>Find item by ID, mutate it in-place, then persist. Returns mutated item or null if not found.</summary>
    protected T? MutateYaml(string id, Action<T> mutate)
    {
        _lock.EnterWriteLock();
        try
        {
            if (!_items.TryGetValue(id, out T? item)) return null;
            mutate(item);
            Persist();
            return item;
        }
        finally { _lock.ExitWriteLock(); }
    }

    protected bool RemoveYaml(string id)
    {
        _lock.EnterWriteLock();
        try
        {
            if (!_items.Remove(id)) return false;
            Persist();
            return true;
        }
        finally { _lock.ExitWriteLock(); }
    }

    /// <summary>Remove all items matching predicate. Returns count of removed items.</summary>
    protected int RemoveAllYaml(Func<T, bool> predicate)
    {
        _lock.EnterWriteLock();
        try
        {
            List<string> toRemove = _items
                .Where(kv => predicate(kv.Value))
                .Select(kv => kv.Key)
                .ToList();
            foreach (string key in toRemove)
                _items.Remove(key);
            if (toRemove.Count > 0) Persist();
            return toRemove.Count;
        }
        finally { _lock.ExitWriteLock(); }
    }

    /// <summary>
    /// Execute a write operation with full access to the internal dictionary.
    /// Persists only if the operation returns true. Use for complex atomic operations
    /// that need cross-item validation (e.g. uniqueness checks before insert).
    /// </summary>
    protected bool ExecuteWrite(Func<Dictionary<string, T>, bool> operation)
    {
        _lock.EnterWriteLock();
        try
        {
            bool persist = operation(_items);
            if (persist) Persist();
            return persist;
        }
        finally { _lock.ExitWriteLock(); }
    }

    private void LoadFromFile()
    {
        if (!File.Exists(_filePath)) return;
        string yaml = File.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(yaml)) return;
        try
        {
            List<T>? loaded = Deserializer.Deserialize<List<T>>(yaml);
            if (loaded is null) return;
            foreach (T item in loaded)
                _items[_getId(item)] = item;
        }
        catch
        {
            // Corrupted or empty file: start with empty store
        }
    }

    private void Persist()
    {
        // Must be called inside write lock
        string? dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        string yaml = _items.Count == 0
            ? "[]\n"
            : Serializer.Serialize(_items.Values.ToList());
        File.WriteAllText(_filePath, yaml);
    }
}
