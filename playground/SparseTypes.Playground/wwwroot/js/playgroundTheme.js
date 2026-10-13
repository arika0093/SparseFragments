const systemTheme = window.matchMedia("(prefers-color-scheme: dark)");
let selectedMode = "System";
let dotNetReference = null;
let systemThemeChanged = null;

function applyColorScheme(mode) {
    const isDark = mode === "Dark" || (mode === "System" && systemTheme.matches);
    document.documentElement.style.colorScheme = isDark ? "dark" : "light";
    return isDark;
}

export function initialize(reference) {
    const savedMode = localStorage.getItem("pg-theme");
    selectedMode = ["System", "Light", "Dark"].includes(savedMode) ? savedMode : "System";
    dotNetReference = reference;
    systemThemeChanged = () => {
        if (selectedMode === "System") {
            dotNetReference.invokeMethodAsync(
                "OnSystemThemeChanged",
                applyColorScheme(selectedMode)
            );
        }
    };
    systemTheme.addEventListener("change", systemThemeChanged);
    return selectedMode;
}

export function apply(mode) {
    selectedMode = mode;
    localStorage.setItem("pg-theme", mode);
    return applyColorScheme(mode);
}

export function dispose() {
    if (systemThemeChanged !== null) {
        systemTheme.removeEventListener("change", systemThemeChanged);
        systemThemeChanged = null;
    }

    dotNetReference = null;
}
