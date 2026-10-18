# Amazon Luna symbol

`luna-symbol.path` contains the exact standalone symbol path from the
`TempoLogoSVG` component in [Luna's official website](https://luna.amazon.com/)
resource bundle. It has no circular background.

- Source: <https://m.media-amazon.com/images/S/sash/pvahmxrO0Vmzxiy.js?name=resources>
- Component: `TempoLogoSVG`, SVG view box `0 0 21 20`.
- Retrieved October 18, 2026.
- Resource SHA-256: `49d9bad571e99a91efd31d671b550d39ec14bc84388af064f097a0ab26f97c4c`

The SVG path coordinates are unchanged. The `F1 ` prefix selects SVG's nonzero
fill rule in WPF; the file ends with a newline. The embedded resource is parsed
locally into frozen vector geometry. Its fill inherits the host sidebar's
foreground, including theme and selection changes. No network icon request,
bitmap, SVG renderer, or additional runtime file is needed.

This replaces the circular purple favicon used in preview `0.2.3`. Amazon Luna
and its logo are Amazon brand assets, separate from this project's MIT-licensed
code.
