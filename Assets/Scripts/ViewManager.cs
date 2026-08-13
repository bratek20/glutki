using System;

public enum ViewMode
{
    World,
    Base
}

// Local, per-peer notion of which "space" the camera is currently showing - the shared world
// map, or the interior of one specific base. Not networked, same reasoning as BaseSelectionManager:
// this is a viewing concern, every peer can look at whatever they want independently.
public static class ViewManager
{
    public static ViewMode CurrentView { get; private set; } = ViewMode.World;
    public static PlayerBase ViewedBase { get; private set; }

    public static event Action ViewChanged;

    public static void EnterBaseView(PlayerBase b)
    {
        if (b == null) return;

        BaseSelectionManager.Select(b);
        ViewedBase = b;
        CurrentView = ViewMode.Base;
        ViewChanged?.Invoke();
    }

    public static void EnterWorldView()
    {
        if (CurrentView == ViewMode.World) return;

        CurrentView = ViewMode.World;
        ViewChanged?.Invoke();
    }
}
