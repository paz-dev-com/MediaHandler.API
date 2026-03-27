# Auth0 Migration Checklist — MediaHandler.API

## 1. Configuration (User Secrets / appsettings)

- [x] Set `Okta:Domain` to `https://dev-y7ayyfr68ji4tkr7.eu.auth0.com/`
  - ⚠️ Auth0 requires a **trailing slash** on the Authority URL for OIDC discovery to work
- [x] Set `Okta:Audience` to the Auth0 API Identifier you created (e.g. `https://localhost:7001/api/v1`)
  - Must match **exactly** what was entered in the Auth0 Dashboard → Applications → APIs
- [x] Set `Okta:ClientId` to any non-empty string (it is `[Required]` at startup but not used by JWT validation)
- [x] Set `Okta:ClientSecret` to any non-empty string (same reason — validated but unused)
- [x] Add `http://localhost:4200` to `AllowedOrigins` in `appsettings.Development.json`
  - Currently only `http://localhost:3000` is listed → Angular app will be blocked by CORS

---

## 2. JWT Bearer — `ServiceExtensions.cs`

File: `MediaHandler.API/Extensions/ServiceExtensions.cs`

- [x] Verify `options.Authority` receives the domain **with trailing slash**
  - If `OktaOptions.Domain` is stored without it, append it here:
    ```csharp
    options.Authority = okta.Domain.TrimEnd('/') + '/';
    ```
- [x] Map the Auth0 role claim so `[Authorize(Policy = "AdminOnly")]` works
  - Auth0 puts roles in a custom namespaced claim (e.g. `https://mediahandler.com/roles`)
  - Add inside `.AddJwtBearer(options => { ... })`:
    ```csharp
    options.TokenValidationParameters.RoleClaimType = "https://mediahandler.com/roles";
    ```
  - Or add an Auth0 Action in the Dashboard to map `roles` → standard `roles` claim and set:
    ```csharp
    options.TokenValidationParameters.RoleClaimType = "roles";
    ```

---

## 3. CORS — `Program.cs`

File: `MediaHandler.API/Program.cs`

- [x] Add `http://localhost:4200` to `AllowedOrigins` in `appsettings.Development.json`
- [x] Ensure `AllowAnyHeader()` is present (it is — no change needed)
- [ ] No `AllowCredentials()` needed since the Angular app uses Bearer tokens, not cookies

---

## 4. `CurrentUserService.cs` — Claim compatibility

File: `MediaHandler.API/Identity/CurrentUserService.cs`

- [x] The `UserId` Guid property always returns `null` because Auth0 `sub` values are strings like `auth0|64abc...`, not GUIDs
  - **No functional impact** — all application handlers use `OktaId` (raw `sub` string) for DB lookups
  - ~~Cosmetic fix if needed: remove or document the property~~ **Done:** property removed from `ICurrentUserService` and `CurrentUserService`
- [x] `IsAdmin` updated to use `ClaimsPrincipal.IsInRole()` instead of `FindFirst(ClaimTypes.Role)`
  - Ensures compatibility with the custom `RoleClaimType` (`https://mediahandler.com/roles`) set in JWT Bearer options
  - `IsInRole()` respects the `ClaimsIdentity.RoleClaimType`, so it works in both dev mode (`ClaimTypes.Role`) and production (Auth0 namespaced claim)

---

## 5. Auth0 Dashboard (must be done before the API will accept tokens)

- [ ] **Application Settings**
  - Allowed Callback URLs: `http://localhost:4200/auth/callback`
  - Allowed Logout URLs: `http://localhost:4200`
  - Allowed Web Origins: `http://localhost:4200`
- [ ] **APIs → Create API**
  - Identifier: `https://localhost:7001/api/v1` (must match `Okta:Audience` exactly)
  - Signing: `RS256`
- [ ] *(Optional)* Add an Auth0 Action (Login flow) to attach roles to the JWT if `AdminOnly` policy is needed

---

## Notes

- The `OktaId` field in `User` entity stores the Auth0 `sub` value (`auth0|xxx`) — no DB migration needed, values will simply differ in format for new users
- `DevAuthenticationHandler` is still active in `Development` mode — it bypasses all token validation and injects fake claims via request headers (`X-Dev-OktaId`, `X-Dev-Email`, `X-Dev-IsAdmin`). This means the API works locally without a real token as long as `ASPNETCORE_ENVIRONMENT=Development`
