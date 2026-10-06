using Microsoft.FluentUI.AspNetCore.Components;

namespace SparseFragments.Playground.Models;

/// <summary>Shared theme state for the playground (Fluent mode + resolved darkness).</summary>
public sealed class PlaygroundThemeState
{
    /// <summary>Gets the selected Fluent design theme mode.</summary>
    public DesignThemeModes Mode { get; private set; } = DesignThemeModes.System;

    /// <summary>Gets whether the effective theme is currently dark.</summary>
    public bool IsDark { get; private set; }

    /// <summary>Raised when <see cref="Mode"/> or <see cref="IsDark"/> changes.</summary>
    public event Action? Changed;

    /// <summary>Sets the Fluent design theme mode.</summary>
    public void SetMode(DesignThemeModes mode)
    {
        if (Mode != mode)
        {
            Mode = mode;
            Changed?.Invoke();
        }
    }

    /// <summary>Sets the resolved dark/light luminance.</summary>
    public void SetLuminance(bool isDark)
    {
        if (IsDark != isDark)
        {
            IsDark = isDark;
            Changed?.Invoke();
        }
    }
}
