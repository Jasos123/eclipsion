using Robust.Client.UserInterface;

namespace Content.Client._Crescent.UI;

/// <summary>
///     Keeps a container's children in step with a list of data, reusing the control already built for a key
///     rather than throwing the whole list away and building it again.
/// </summary>
/// <remarks>
///     <para>
///     Consoles push their state on a timer - three seconds for the taxation and payroll consoles - and the
///     usual <c>RemoveAllChildren</c> + rebuild loop quietly eats whatever the player was in the middle of:
///     the LineEdit they were typing a figure into is destroyed mid-keystroke, the scroll snaps back to the
///     top, and keyboard focus goes with it.
///     </para>
///     <para>
///     Rows here are only created, disposed or moved when the data actually changes shape. A refresh that
///     returns the same rows in the same order - which is the overwhelmingly common case - touches nothing
///     but the values inside them. Reordering uses <see cref="Control.SetPositionInParent"/>, which shuffles
///     a control within its parent without ever detaching it, so even a row that shifts position keeps its
///     focus and its half-typed contents.
///     </para>
/// </remarks>
public sealed class ConsoleRowList<TKey, TRow>
    where TKey : notnull
    where TRow : Control
{
    private readonly Control _container;
    private readonly Func<TRow> _factory;

    private readonly Dictionary<TKey, TRow> _rows = new();
    private readonly HashSet<TKey> _seen = new();
    private readonly List<TKey> _stale = new();

    /// <param name="container">
    ///     Control the rows are parented to. It may hold other children (an empty-state label, say) - those
    ///     are never touched, but they end up after the rows once any row exists.
    /// </param>
    /// <param name="factory">Builds an empty row. Called once per key, not once per refresh.</param>
    public ConsoleRowList(Control container, Func<TRow> factory)
    {
        _container = container;
        _factory = factory;
    }

    public int Count => _rows.Count;

    /// <summary>
    ///     Brings the container in line with <paramref name="data"/>: creates rows for new keys, updates the
    ///     rest in place, disposes the ones whose data is gone, and orders what is left to match.
    /// </summary>
    public void Sync<TData>(IReadOnlyList<TData> data, Func<TData, TKey> keySelector, Action<TRow, TData> update)
    {
        _seen.Clear();

        foreach (var item in data)
        {
            var key = keySelector(item);

            // Two rows fighting over one key would corrupt the list, so the first entry wins and any
            // duplicate is skipped instead.
            if (!_seen.Add(key))
                continue;

            if (!_rows.TryGetValue(key, out var row))
            {
                row = _factory();
                _rows[key] = row;
                _container.AddChild(row);
            }

            update(row, item);
        }

        // Drop the rows whose data went away.
        _stale.Clear();
        foreach (var (key, row) in _rows)
        {
            if (_seen.Contains(key))
                continue;

            _stale.Add(key);
            row.Orphan();
            row.Dispose();
        }

        foreach (var key in _stale)
            _rows.Remove(key);

        // Put the survivors in the data's order. This is a no-op per row that is already in place.
        _seen.Clear();
        var index = 0;
        foreach (var item in data)
        {
            var key = keySelector(item);
            if (!_seen.Add(key) || !_rows.TryGetValue(key, out var row))
                continue;

            row.SetPositionInParent(index++);
        }
    }

    /// <summary>Disposes every row and forgets them.</summary>
    public void Clear()
    {
        foreach (var row in _rows.Values)
        {
            row.Orphan();
            row.Dispose();
        }

        _rows.Clear();
    }
}
