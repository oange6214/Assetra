namespace Assetra.WPF.Shell;

/// <summary>
/// Single entry in the Ctrl+Shift+K command palette. Title is a localized resource key
/// (resolved at render time via ResourceKeyToStringConverter); Group is also a key
/// so palette items can group e.g. by 「導覽」/「動作」. Execute closes the palette
/// and runs the bound action (navigate, open dialog, toggle theme, etc.).
///
/// MVP intentionally simple — no icons, no shortcuts inline, no fuzzy ranking; flat
/// substring filter on the resolved Title is enough for v1.
/// </summary>
public sealed record CommandPaletteEntry(
    string TitleKey,
    string GroupKey,
    System.Action Execute);
