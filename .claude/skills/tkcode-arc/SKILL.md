---
name: tkcode-arc
description: Use TKCode.Arc to build and review Vox Avalonia UI. Applies when modifying Vox screens, controls, dialogs, themes or navigation.
---

# TKCode.Arc in Vox

Vox pins **TKCode.Arc 0.3.2-preview.2** with Avalonia 12.1.4. Read the official [AI index](https://docs.tkcode.si/llms.txt), [Arc skill](https://docs.tkcode.si/skills/tkcode-arc/SKILL.md), and [review checklist](https://docs.tkcode.si/skills/tkcode-arc/checklist.md) before substantial UI work.

## Project-specific rules

1. Prefer Arc controls over custom look-alikes. Use Avalonia panels for product-specific composition; create a Vox control only when Arc has no suitable component.
2. Startup registration is in `src/Vox.App/Program.cs`:
   ```csharp
   .WithArcFonts()
   .UseArc(arc => arc
       .UseArcControls()
       .WithTheme(t => t.Appearance(ArcAppearance.System)));
   ```
3. Runtime theme changes go through `ArcTheme.Apply(...)` in `ThemeService`; do not add AtomUI ThemeManager APIs. Arc 0.3.2 is independent of AtomUI.
4. Arc controls share names with Avalonia controls. When both namespaces are used, add explicit aliases such as `using ArcButton = TKCode.Arc.Button;`.
5. Use Arc design tokens for Arc controls: `ArcTokens.Bind(...)` in C# or `ArcDesignTokenResource` in XAML. Vox semantic styles belong to `VoxStyles.axaml` and must not override Arc templates.
6. Use `Input` for single-line fields, `Textarea` for multiline fields, `PasswordField` for secrets, `Select` for short fixed choices, `Combobox` for searchable long lists, `Switch` for immediate settings, and `SegmentedControl` for a few always-visible modes.
7. Preserve the product's warm editorial/sage visual identity and the frozen seven-workspace information architecture. Do not copy static demo values from `design/Vox.html` into real app state.
8. Keep editor source/preview, markdown input, DataGrid, and product-specific workflows on their existing controls when Arc is not a better fit. Do not replace the Markdown editor with a generic Arc Input.

## Version migration notes

- 0.3.2 removes AtomUI dependencies. Replace `UseAtomUI(...UseDesktopControls().UseArc())` with `UseArc(arc => arc.UseArcControls())`.
- Replace AtomUI theme requests with `ArcTheme.Apply(t => ...)`; changes are partial and unspecified state is retained.
- Pin all Arc packages to the same exact version in `Directory.Packages.props`.
- Read the API reference page for every control. Never invent properties or enum members.

See [components.md](components.md) for Vox's component map and [checklist.md](checklist.md) before finishing a screen.
