# Asset Management CIS Implementation

This note records how the external CIS guideline is mapped into Assetra's WPF design system.

## Brand Direction

Assetra should read as an institutional asset management product: precise, stable, calm, structured, and quietly premium. The product UI should prioritize dense financial clarity over decorative marketing language.

## Implemented Tokens

| CIS role | Resource key | HEX |
| --- | --- | --- |
| Primary Black / Deep Graphite | `Color.Brand.Graphite` | `#0A0F1C` |
| Secondary Navy / Institutional Navy | `Color.Brand.Navy` | `#14213D` |
| Accent Gold / Soft Metallic Gold | `Color.Brand.Gold` | `#C8A96B` |
| Surface Gray / Graphite Surface | `Color.Brand.SurfaceGraphite` | `#1E2430` |
| Light Background / Soft White | `Color.Brand.SoftWhite` | `#F7F8FA` |

Dark and light semantic theme brushes now use the CIS layer values from `DesignSystem/Tokens/Colors.xaml`.

## Typography

Assetra uses Windows-first typography because the runtime is WPF and the primary audience works in dense Traditional Chinese financial screens.

| Role | Resource key | Stack |
| --- | --- | --- |
| UI text and controls | `Font.Family.UI` | `Segoe UI Variable Text, Segoe UI, Microsoft JhengHei UI, Noto Sans TC, Taipei Sans TC Beta, Microsoft YaHei UI, sans-serif` |
| Headings and dialog titles | `Font.Family.Display` | `Segoe UI Variable Display, Segoe UI, Microsoft JhengHei UI, Noto Sans TC, Taipei Sans TC Beta, Microsoft YaHei UI, sans-serif` |
| Financial numbers | `Font.Family.Numeric` | `Segoe UI Variable Text, Segoe UI, Microsoft JhengHei UI, Noto Sans TC, Taipei Sans TC Beta, Microsoft YaHei UI, sans-serif` |
| Legacy compatibility alias | `Font.Family.Base` | Same stack as `Font.Family.UI`; new styles should choose `UI`, `Display`, or `Numeric` explicitly. |

Numeric text styles also enable tabular figures through `Typography.NumeralAlignment="Tabular"` so amounts align cleanly in tables, metric cards, and portfolio panels.

## Logo System

`Resources/Branding.xaml` now uses a geometric abstract mark based on layered capital allocation: two structured vertical asset forms and a centered gold allocation triangle. It keeps the existing `AssetraLogo` resource key so current consumers do not need code changes.

## UI Direction

When extending the app, prefer:

- 12-column thinking for page composition, with existing WPF grids and `8px` spacing tokens.
- Institutional dark surfaces using graphite layers, navy structure, and gold accents.
- Clear tabular hierarchy for portfolios, statements, allocation, risk, and performance.
- WCAG AA minimum contrast, with AAA preferred for dashboards and key financial values.

## Production Follow-Ups

The CIS recommends deliverables outside the current WPF runtime scope:

- Master logo exports as SVG/PDF/PNG.
- App icon export set.
- Figma component library.
- Print-ready business cards and PDF/X production files.
- Presentation and investor report templates.
