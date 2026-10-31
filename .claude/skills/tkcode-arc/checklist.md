# TKCode.Arc UI review checklist for Vox

Run this against every screen or control changed. Fix failures and review again.

## Version and registration
- [ ] All `TKCode.Arc*` packages are pinned to the same exact version in `Directory.Packages.props`.
- [ ] The app uses `UseArc(arc => arc.UseArcControls())`; no obsolete `UseAtomUI` or `UseDesktopControls` registration remains.
- [ ] Runtime appearance uses `ArcTheme.Apply`; no AtomUI ThemeManager calls remain.
- [ ] No `InvariantGlobalization=true`.

## Control selection
- [ ] Arc control exists and is used instead of a custom look-alike.
- [ ] The API reference for each control has been read; only documented properties/events/enums are used.
- [ ] C# files mixing Avalonia and Arc names use aliases for collisions.
- [ ] Text inputs use Input / Textarea / PasswordField according to purpose.
- [ ] Short choices use Select; long searchable lists use Combobox; always-visible modes use SegmentedControl.

## Theme and visual language
- [ ] Arc controls use Arc tokens, not hard-coded colors, radii, shadows or font sizes.
- [ ] VoxStyles.axaml styles only Vox semantic classes and does not override Arc templates.
- [ ] Light, dark and system appearance remain coherent; test at least two accents.
- [ ] Motion respects Full, Reduced and Off.
- [ ] The sage editorial identity and existing page information architecture remain intact.

## Interaction and accessibility
- [ ] Each field has a Label; hidden labels are still accessible.
- [ ] Icon-only actions have accessible names.
- [ ] Keyboard navigation, focus order, Escape and light-dismiss behavior are correct.
- [ ] State is not conveyed by color alone.
- [ ] One Primary action per surface; other actions are Secondary/Ghost, destructive actions Danger.
- [ ] Empty and loading states exist for collections.

## Product integration
- [ ] Real repository/account/publish state is used; prototype demo values are not hardcoded.
- [ ] Existing markdown editor, image workflow, Git operations and publishing semantics are preserved.
- [ ] New interactions preserve existing i18n bindings.
- [ ] No credentials or sensitive values leak into logs or repository files.

## Build and verification
- [ ] Run `dotnet restore`.
- [ ] Run `dotnet build Vox.slnx` (or the repository's current solution filename).
- [ ] Run relevant tests.
- [ ] Report exactly what was run; never claim an unrun build or UI check passed.
