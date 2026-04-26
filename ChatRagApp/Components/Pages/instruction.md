# Pages Folder — Implementation Instruction

## Purpose

This folder contains the routed page-level UI for Chat RAG App.

Pages in this folder are responsible for user-facing application features that render inside the shared layout shell, including:

- the authenticated landing experience
- multi-agent chat interactions
- image upload and semantic image search
- operational reporting and dashboard views
- fallback error and not-found experiences
- remaining scaffold or demo pages that are not core product flows

These pages should stay focused on page composition, route handling, and user interactions. Shared shell behavior belongs in `Components/Layout`, while business logic and integrations belong in services, controllers, and agents.

---

## Files In Scope

### `Home.razor`

Implements the authenticated landing page for `/`.

Responsibilities:

- introduce the product and its primary capabilities
- provide entry points into chat, image management, and dashboard flows
- remain lightweight and navigation-focused rather than data-heavy

### `Chat.razor`

Implements the authenticated `/chat` page.

Responsibilities:

- render the interactive chat workspace
- allow the user to switch between available agents
- send messages through `AgentFactory` and the selected `IAgent`
- display assistant replies and token usage
- show follow-up question suggestions
- keep per-session state such as selected agent, message history, and accumulated tokens

### `Images.razor`

Implements the authenticated `/images` page.

Responsibilities:

- collect metadata for bulk image upload
- accept browser file input and adapt uploaded files into the image service contract
- trigger semantic image search
- render upload results and search results

### `Dashboard.razor`

Implements the authenticated `/dashboard` page.

Responsibilities:

- load daily reporting data from `IQdrantService`
- allow the user to refresh or change the reporting date
- render KPI cards, token charts, and activity tables
- auto-refresh the dashboard on a timer

### `Error.razor`

Implements the `/Error` page.

Responsibilities:

- render the generic application error screen
- show the request identifier when available
- communicate development-mode guidance for local debugging

### `NotFound.razor`

Implements the routed `/not-found` page.

Responsibilities:

- render the application-level not-found message inside the main layout
- serve as the target for status-code re-execution in the HTTP pipeline

### `Counter.razor`

Implements the `/counter` page.

Responsibilities:

- provide a simple interactive counter example
- remain isolated from core product behavior unless intentionally promoted

### `Weather.razor`

Implements the `/weather` page.

Responsibilities:

- demonstrate streamed rendering and asynchronous page loading
- remain a sample page unless it is explicitly converted into a product feature

### `Account/`

Contains account-specific routed pages with their own instruction file.

Note:

- account pages are related to this folder structurally but are documented separately in `Components/Pages/Account/instruction.md`

---

## Expected Page Flow

1. The router resolves a route under `Components/Pages`.
2. Protected routes require an authenticated user and redirect unauthenticated users through the login flow.
3. `MainLayout` renders the shared shell around the page.
4. The page coordinates UI state and delegates data work to agents, services, or controllers.
5. The resulting content is shown within the layout content region.

---

## Feature-Specific Flow

### Chat Flow

1. The user opens `/chat`.
2. The page resolves available agents from `AgentFactory`.
3. The user selects an agent and submits a prompt.
4. The selected agent processes the message and returns content, token usage, and suggested follow-up questions.
5. The page appends both user and assistant turns to the visible conversation.
6. The page updates token totals for the current session.

### Image Flow

1. The user opens `/images`.
2. The user enters metadata and selects one or more files.
3. The page adapts browser files into the upload contract used by `IImageService`.
4. Upload results are rendered per file.
5. The user can search using natural-language text queries.
6. Matching images are rendered as result cards with score and metadata.

### Dashboard Flow

1. The user opens `/dashboard`.
2. The page loads the report for the selected date.
3. KPI values, agent activity, and token usage are rendered.
4. The page periodically refreshes the report and re-renders the chart.

---

## Design Constraints

- Keep page components focused on page orchestration and presentation, not infrastructure setup.
- Route pages should call services and agents rather than embedding direct storage or transport logic.
- Preserve authentication requirements on protected business pages.
- Keep demo pages clearly separate from production-critical flows unless they are intentionally upgraded.
- When adding new pages, keep routes aligned with navigation links in `Components/Layout/NavMenu.razor`.
- Error and not-found pages should remain safe fallbacks and should not depend on fragile external services.

---

## Related Files

- `ChatRagApp/Components/Routes.razor` — route registration and layout application.
- `ChatRagApp/Components/Layout/MainLayout.razor` — shared shell around routed pages.
- `ChatRagApp/Components/Layout/NavMenu.razor` — navigation entry points into page routes.
- `ChatRagApp/Components/RedirectToLogin.razor` — redirects unauthorized access to login.
- `ChatRagApp/Agents/AgentFactory.cs` — provides chat agents used by `Chat.razor`.
- `ChatRagApp/Services/IImageService.cs` — upload and search contract used by `Images.razor`.
- `ChatRagApp/Services/IQdrantService.cs` — reporting source used by `Dashboard.razor`.
- `ChatRagApp/Components/Pages/Account/instruction.md` — separate instruction for account-specific pages.