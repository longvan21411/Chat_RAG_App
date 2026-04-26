# Layout Folder — Implementation Instruction

## Purpose

This folder contains the shared application shell and layout-level UI for Chat RAG App.

Layout components in this folder are responsible for the persistent frame around routed pages, including:

- the left sidebar navigation
- the top application bar
- the signed-in user summary entry
- reconnect and circuit recovery UI for interactive Blazor sessions
- layout-specific styling and client-side behavior.
- the main layout should implement two different modes as dark and light style.
- user is able to manually change under button "Dark/Light" under user information in the left sidebar navigation.

These components should provide a consistent application shell across all authenticated pages.

---

## Files In Scope

### `MainLayout.razor`

Implements the top-level page shell used by routed content.

Current responsibilities:

- render the page structure with sidebar and main content regions
- host the shared `NavMenu`
- render a top row with the app label and logout control
- render `@Body` inside the main content area
- expose the default error UI container used by Blazor

### `MainLayout.razor.css`

Defines layout-specific styling for the overall shell.

Current responsibilities:

- control sidebar and main content sizing
- style the top row and article content container
- support responsive layout behavior

### `NavMenu.razor`

Implements the left sidebar navigation.

Current responsibilities:

- render navigation links for the main application pages
- show the product branding at the top of the sidebar
- render the authenticated user card at the bottom of the left navigation
- link the user card to `/account/profile`
- derive display name, subtitle, and initials from the current claims principal

### `NavMenu.razor.css`

Defines sidebar-specific styling.

Current responsibilities:

- style navigation links and hover/active states
- style the bottom user card
- keep the sidebar usable on small and large screens

### `ReconnectModal.razor`

Implements the reconnect UI for lost Blazor Server circuits.

Current responsibilities:

- render reconnect, retry, failed, and paused session states
- host the reconnect modal markup used during circuit interruptions
- load the reconnect modal script from `wwwroot/js/reconnect-modal.js`

### `ReconnectModal.razor.css`

Defines styling for the reconnect dialog and its state views.

### `ReconnectModal.razor.js`

Legacy component-local reconnect script file.

Note:

- the active runtime script path is currently `wwwroot/js/reconnect-modal.js`
- keep component-local script references aligned with the actual static asset strategy used by the app

---

## Expected Layout Flow

1. The router resolves a page and applies `MainLayout` as the active layout.
2. `MainLayout` renders the sidebar through `NavMenu`.
3. The current page content is rendered inside the main article region.
4. If the user is authenticated, layout-level UI shows identity information.
5. The left navigation shows the signed-in user card at the bottom.
6. Clicking the user card navigates to `/account/profile`.
7. If the interactive Blazor circuit is interrupted, the reconnect modal appears and guides the user through retry or reload behavior.

---

## Design Constraints

- Keep layout components focused on shared shell behavior, not page-specific business logic.
- Do not duplicate page content responsibilities inside layout components.
- Keep navigation routes aligned with the actual routed pages in `Components/Pages`.
- Preserve authenticated user access to the profile entry point from the sidebar.
- Keep reconnect behavior compatible with Blazor Server interactive rendering.

---

## Related Files

- `ChatRagApp/Components/Routes.razor` — applies layout to routed pages
- `ChatRagApp/Components/Pages` — routed page content displayed inside `MainLayout`
- `ChatRagApp/Components/Pages/Account/Profile.razor` — destination for the sidebar user card
- `ChatRagApp/wwwroot/js/reconnect-modal.js` — active static reconnect script
- `ChatRagApp/Program.cs` — interactive server setup and authentication pipeline
