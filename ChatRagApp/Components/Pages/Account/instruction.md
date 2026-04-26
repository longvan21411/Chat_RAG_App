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

### `Register.razor`

Implements the `/register` page for local account creation.

Responsibilities:

- Render a local account registration form.
- Send new account data to the account controller.
- Return the user to the login page after successful registration.

### `Profile.razor`

Implements the authenticated account details page.

Responsibilities:

- Show the signed-in user's current profile information.
- Allow the user to complete or correct missing profile fields.
- Refresh the signed-in cookie after profile updates so UI displays current user data.

---

## Expected Login Flow

1. An unauthenticated user requests a protected route.
2. The app redirects the user to `/login?returnUrl=...`.
3. `Login.razor` renders the login UI.
4. The user can sign in with a valid local username and password.
5. The system supports a technical development account with username `admin` and password `admin`.
6. If the user does not have a local account, the page provides a registration link to `/register`.
7. After successful registration, the system redirects back to `/login?registered=true`.
8. If Google OpenID Connect is configured, the page also shows the Google sign-in button.
9. Clicking the Google button navigates to `/account/external-login?provider=Google&returnUrl=...`.
10. The controller starts the external authentication challenge.
11. After successful local or Google sign-in, the controller signs the user in with cookies and redirects back to the original local `returnUrl`.
12. After login succeeds, the app displays user information at the bottom of the left navigation.
13. Clicking the avatar or user card navigates to the authenticated profile page.
14. The profile page allows the user to review and update missing profile information.
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
- `invalid_credentials`
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
- `ChatRagApp/Components/Pages/Account/Register.razor` — local account registration page.
- `ChatRagApp/Components/Pages/Account/Profile.razor` — authenticated user profile page.
- `ChatRagApp/Program.cs` — authentication and cookie configuration.
