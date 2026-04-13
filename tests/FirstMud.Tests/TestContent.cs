using FirstMud.Application.Content;

namespace FirstMud.Tests;

/// <summary>
/// Lazily-loaded shared ContentProvider backed by the repo-root content/
/// folder. Tests that need a full IContentProvider (e.g. to satisfy new
/// faction-aware service constructors) should call <see cref="Shared"/>.
/// </summary>
internal static class TestContent
{
    private static readonly Lazy<IContentProvider> _shared = new(() =>
        new ContentProvider(ContentRootResolver.Resolve()));

    public static IContentProvider Shared => _shared.Value;
}
