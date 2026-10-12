# Front-end style

## Design tokens

`src/styles.css` holds every colour, spacing step, radius and font size as a CSS custom property.
Component stylesheets use only these variables: no hex colours, no raw pixel spacing.

| Group   | Tokens                                                                                   |
| ------- | ---------------------------------------------------------------------------------------- |
| Colour  | `--color-background`, `-surface`, `-text`, `-text-muted`, `-border`, `-border-strong`, `-accent`, `-on-accent`, `-focus`, `-danger` |
| Spacing | `--space-1` (0.25rem) … `--space-6` (2.5rem)                                             |
| Shape   | `--radius`                                                                               |
| Type    | `--font-family`, `--font-size-sm` / `-md` / `-lg` / `-xl`                                 |

The light theme is on `:root`. The dark theme follows the operating system
(`prefers-color-scheme: dark`). A user-selectable theme switch would add a `[data-theme]`
override; there is none yet.

## Accessibility (WCAG 2.1 AA, #94)

- Every input has a visible `<label>`; field errors are linked with `aria-describedby` and
  announced with `aria-live`.
- Keyboard focus is always visible (`:focus-visible` ring in `styles.css`).
- Text colours meet 4.5:1 contrast on their surface in both themes.
- `ng lint` runs the angular-eslint template accessibility rules on every pull request.

## Angular idioms

Standalone components, signals for state, `inject()`, zoneless change detection, and the
built-in control flow (`@if`, `@for`, `@switch`). Services own HTTP calls and expose signals;
components stay presentational. Times from the API are UTC (BR-14) and are always shown with
their zone.
