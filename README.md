"# UniConnect" 

---

## UI/UX Refresh — 20-7 (July 20, 2026)

A design pass over the Home page, navbar, and the Login/Register experience. New tools and techniques introduced:

- **Hugeicons** (`@hugeicons/core-free-icons`, free stroke-rounded set) — inlined as a single SVG `<symbol>` sprite (`Views/Shared/_Icons.cshtml`) and referenced via `<svg class="hgi"><use href="#i-name"></use></svg>`. No npm/build step required.
- **CSS View Transitions API** (`@view-transition { navigation: auto }`) — powers the morph animation between the Login and Register pages, with a JS fallback fade for browsers that don't yet support it.
- **Dedicated split-screen auth layout** (`Views/Shared/_AuthLayout.cshtml`, `wwwroot/css/pages/auth.css`) — animated brand panel (floating icon chips, auto-rotating testimonial carousel), floating-label form fields with icon-led inputs and a password show/hide toggle, plus a social-proof strip.
- **Redesigned navbar** — Hugeicons throughout, an avatar-initial account dropdown (Bootstrap dropdown) replacing the old plain text menu, a notification bell with an animated unread badge, and a hamburger-to-close icon morph for the mobile menu. Switched to the `navbar-expand-xl` breakpoint so the growing list of service links no longer wraps awkwardly on laptop-sized screens.
- **Home page**: a rotating headline tagline and an auto-scrolling "highlights" marquee, both driven by small vanilla-JS class togglers (deliberately not pure-CSS animation-delay staggering, which is easy to get subtly wrong and can show two items overlapping mid-cycle); a six-service showcase grid; livelier hero background auras and a pulsing "live" indicator dot.
- All added motion respects `prefers-reduced-motion: reduce`.

No new runtime dependencies were added — everything above is plain Razor, CSS, and vanilla JavaScript layered onto the existing Bootstrap 5 + custom `.uc-*` design system.
