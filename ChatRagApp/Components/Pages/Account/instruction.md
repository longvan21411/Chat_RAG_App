# Account Folder — Implementation Instruction

## Purpose

This folder contains the Blazor UI entry point for account access, currently focused on the login experience.

The page in this folder does not execute OAuth directly. It renders the login screen and delegates authentication flow to the MVC account controller.

---

## Files In Scope

### `Login.razor`

Implements the `/login` page for the application.

Responsibilities:

- Render the login card UI.
- Read `error` and `returnUrl` from query string parameters.
- Show a Google sign-in button only when Google OAuth settings are configured.
- Show a safe warning message when Google OAuth is not configured.
- Forward users into `/account/external-login` for the real authentication challenge.

---

## Expected Login Flow

1. An unauthenticated user requests a protected route.
2. The app redirects the user to `/login?returnUrl=...`.
3. `Login.razor` renders the login UI.
4. If user input a valid user name and password, the page allow to sign-in.
5. System support a technical account to login into the system. The technical account suggestion as username = 'admin' and password = 'admin'.
6. If user don't have a valid account, user considers to use the open connect id.
7. If `Google:ClientId` and `Google:ClientSecret` are present, the page shows the Google sign-in button.
8. Clicking the button navigates to `/account/external-login?provider=Google&returnUrl=...`.
9. The controller starts the OAuth challenge.
10. After callback, the controller signs the user in with cookies and redirects back to the original local `returnUrl`.
11. In case, you don't have account before, a given link help to register account.
12. After registering success, the system will redirect to login page.
---

## Configuration Requirements

The login page depends on the following configuration keys:

```json
"Google": {
  "ClientId": "",
  "ClientSecret": ""
}
```

Behavior:

- If both values exist, the page enables Google sign-in.
- If either value is missing, the page renders a warning instead of triggering a broken auth flow.

---

## Query Parameters

### `returnUrl`

- Used to send the user back to the page they originally requested.
- Should always resolve to a local application route.

### `error`

Supported values currently handled by the page:

- `auth_failed`
- `no_email`
- `provider_unavailable`

These are mapped into user-friendly messages on the page.

---

## Design Constraints

- Keep the page focused on UI and route entry, not authentication business logic.
- Do not place OAuth callback logic in this folder.
- Keep the login page resilient when external auth is unavailable.
- Preserve compatibility with .NET 8 Blazor routing and cookie authentication.

---

## Related Files

- `ChatRagApp/Controllers/AccountController.cs` — external login, callback, logout.
- `ChatRagApp/Components/RedirectToLogin.razor` — redirects unauthorized users to `/login`.
- `ChatRagApp/Program.cs` — authentication and cookie configuration.
