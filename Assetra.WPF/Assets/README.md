# Asset Management App Icon Assets

## Canonical structure

- `svg/asset-logo-light.svg` — light theme app icon source
- `svg/asset-logo-dark.svg` — dark theme app icon source
- `svg/asset-logo-mark-transparent.svg` — transparent mark source for in-app use
- `png/` — generated PNG exports from 16px to 1024px
- `web/favicon.ico` — canonical Windows / web `.ico` bundle used by the WPF app
- `package/` — generated Windows package tile/store logo PNGs
- `android/` — Android adaptive icon XML example

Do not keep duplicate icon copies in `Assets/` root. If the app needs a different path, update the project/XAML references to point at the canonical files above.

## Palette

Light theme:
- Background: `#FFFFFF`
- Main: `#142F55`
- Accent: `#C8A25A`

Dark theme:
- Background: `#0F172A`
- Main: `#3B82F6`
- Accent: `#E6C27A`

## Usage guidance

- Splash screen:
  - light theme -> `png/asset-logo-light-128.png`
  - dark theme -> `png/asset-logo-dark-128.png`
- App / window icon:
  - `web/favicon.ico`
- Package / tile logos:
  - `package/StoreLogo.png`
  - `package/Square44x44Logo.png`
  - `package/Square150x150Logo.png`
  - `package/Wide310x150Logo.png`
- In-app title bar mark:
  - use the transparent mark proportions from `svg/asset-logo-mark-transparent.svg`
