namespace FirstMud.Application.Content;

/// <summary>
/// Static bridge from a singleton <see cref="IContentProvider"/> to a handful
/// of legacy static call sites (notably <c>ZoneGridLayout</c> and
/// <c>BiomeService</c>) that predate the content-configification work and
/// cannot easily be migrated to constructor injection without cascading
/// signature changes through dozens of handlers.
///
/// Lazy fallback: if no provider has been published yet (e.g. in a minimal
/// test context that bypasses DI), a default provider rooted via
/// <see cref="ContentRootResolver"/> is created on first access. Production
/// code always goes through DI, which publishes the real singleton via
/// <see cref="Publish"/> as part of <c>ContentProvider.Reload</c>.
/// </summary>
public static class ContentAccessor
{
    private static IContentProvider? _current;
    private static readonly object _gate = new();

    /// <summary>
    /// Called by <see cref="ContentProvider"/> after a successful load so
    /// that static call sites see the same singleton DI resolves.
    /// </summary>
    public static void Publish(IContentProvider provider)
    {
        lock (_gate)
        {
            _current = provider;
        }
    }

    /// <summary>
    /// Current content provider. Constructs a default one lazily if no
    /// provider has been published yet.
    /// </summary>
    public static IContentProvider Current
    {
        get
        {
            var cur = _current;
            if (cur is not null) return cur;
            lock (_gate)
            {
                if (_current is null)
                {
                    _current = new ContentProvider(ContentRootResolver.Resolve());
                }
                return _current;
            }
        }
    }

    /// <summary>Test seam — resets the cached provider.</summary>
    public static void ResetForTests()
    {
        lock (_gate) { _current = null; }
    }
}
