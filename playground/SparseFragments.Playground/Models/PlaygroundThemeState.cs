namespace SparseFragments.Playground.Models;

public enum PlaygroundThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>Shared theme state for the playground and Monaco editors.</summary>
public sealed class PlaygroundThemeState
{
    /// <summary>Gets the selected theme mode.</summary>
    public PlaygroundThemeMode Mode { get; private set; } = PlaygroundThemeMode.System;

    /// <summary>Gets whether the effective theme is currently dark.</summary>
    public bool IsDark { get; private set; }

    /// <summary>Raised when the selected or resolved theme changes.</summary>
    public event Action? Changed;

    /// <summary>Sets the selected theme mode.</summary>
    public void SetMode(PlaygroundThemeMode mode)
    {
        if (Mode == mode)
        {
            return;
        }

        Mode = mode;
        if (mode != PlaygroundThemeMode.System)
        {
            IsDark = mode == PlaygroundThemeMode.Dark;
        }

        Changed?.Invoke();
    }

    /// <summary>Sets the resolved system theme when system mode is selected.</summary>
    public void SetSystemTheme(bool isDark)
    {
        if (Mode == PlaygroundThemeMode.System && IsDark != isDark)
        {
            IsDark = isDark;
            Changed?.Invoke();
        }
    }
}
