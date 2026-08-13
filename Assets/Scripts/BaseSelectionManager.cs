using System.Collections.Generic;

// Local, per-peer notion of "which base is selected". Not networked - each
// client (and the host) tracks its own selection independently.
public static class BaseSelectionManager
{
    private static readonly List<Base> allBases = new List<Base>();

    public static Base SelectedBase { get; private set; }

    public static void Register(Base b)
    {
        allBases.Add(b);
        if (SelectedBase == null) Select(b);
    }

    public static void Unregister(Base b)
    {
        allBases.Remove(b);
        if (SelectedBase == b) Select(allBases.Count > 0 ? allBases[0] : null);
    }

    public static void Select(Base b)
    {
        if (SelectedBase == b) return;

        if (SelectedBase != null) SelectedBase.SetHighlighted(false);
        SelectedBase = b;
        if (SelectedBase != null) SelectedBase.SetHighlighted(true);
    }
}
