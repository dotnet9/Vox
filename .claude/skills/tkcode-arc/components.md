# Arc component map for Vox

All controls below are in `TKCode.Arc` unless noted. Use the API reference at the linked page before implementation.

| Vox use case | Arc control | API reference |
|---|---|---|
| Primary, secondary, ghost and destructive actions | `Button` / `ButtonVariant` | [Button](https://docs.tkcode.si/components/button.md) |
| Short fixed filters | `Select` / `SelectItem` / `SelectOption` | [Select](https://docs.tkcode.si/components/select.md) |
| One-line settings and search fields | `Input` | [Input](https://docs.tkcode.si/components/input.md) |
| Multi-line descriptions | `Textarea` | [Textarea](https://docs.tkcode.si/components/textarea.md) |
| Passwords and API secrets | `PasswordField` | [PasswordField](https://docs.tkcode.si/components/password-field.md) |
| Immediate boolean settings | `Switch` | [Switch](https://docs.tkcode.si/components/switch.md) |
| Editor modes and page-level view switches | `SegmentedControl` | [SegmentedControl](https://docs.tkcode.si/components/segmented-control.md) |
| Searchable long lists | `Combobox` | [Combobox](https://docs.tkcode.si/components/combobox.md) |
| Multiple simultaneous filters | `MultiSelect` | [MultiSelect](https://docs.tkcode.si/components/multi-select.md) |
| Confirmations and blocking decisions | `Dialog` | [Dialog](https://docs.tkcode.si/components/dialog.md) |
| Contextual detail | `Popover` / `Tooltip` | [Popover](https://docs.tkcode.si/components/popover.md) · [Tooltip](https://docs.tkcode.si/components/tooltip.md) |
| Transient feedback | `Toast` | [Toast](https://docs.tkcode.si/components/toast.md) |

## Current Vox adoption

- `Button`: publishing workspace, publish dialog, platform credential dialog, settings.
- `SegmentedControl`: writing modes, publishing tabs, MCP tabs, appearance options.
- `Select`: `MiniSelect` compatibility wrapper used by article, image, settings and publishing filters.
- `Input`: settings fields and search/commit-summary fields in article, image and publishing pages.
- Lucide icons: `TKCode.Arc.Icons.Lucide`.

## Choosing correctly

- Select is for a short fixed list. Use Combobox for a long list that people need to search.
- Use SegmentedControl when two to four choices should remain visible.
- Use Textarea for multiline text. Do not set AcceptsReturn on a single-line Input.
- Use PasswordField for credentials instead of masking a generic TextBox.
- Prefer Arc's own Dialog/Popover/Tooltip when the interaction matches; keep Vox's modal shell only where its workflow or host integration is materially product-specific.
