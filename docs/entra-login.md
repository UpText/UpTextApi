# Entra login for UpText CRM

The API verifies Entra identity through tenant-specific authorization code + PKCE login, resolves explicit CRM membership, then issues the existing UpText JWT. Entra ID tokens are never accepted as CRM API bearer tokens. Existing password login remains available.

## Tenant-admin configuration

Provider settings belong to `crm.configuration.config`, not a new configuration table. In CRM **Settings → Microsoft Entra login**, a tenant administrator can edit `authentication.entra`. Mobile administrators can open **Tenant settings and Entra login** from Settings.

Merge this object into the tenant's existing JSON (preserve branding and other settings):

```json
{
  "authentication": {
    "entra": {
      "enabled": false,
      "directoryId": "f05141e7-a3c7-4c6d-815e-4e98c8a70fd4",
      "clientId": "65cde3da-eada-4331-a6f8-c4dfdadb96da",
      "clientSecretUri": "https://uptextsecrets.vault.azure.net/secrets/uptext-crm-login-client-secret",
      "provisioningMode": "invite_only"
    }
  }
}
```

The registration **UpText CRM Login** is single-tenant. Other organizations need their own directory/client configuration and approved Key Vault credential. `enabled` defaults to false; missing `provisioningMode` defaults to `invite_only`. Unknown policies fail closed. `automatic` creates standard users only. It never matches or links an existing CRM account by email.

The secret value is held only in Azure Key Vault. The credential created for this registration expires **2027-05-15 11:01:55 UTC**. Rotate it before expiry: create a new application password, write a new version of the existing Key Vault secret, verify login, then remove the old password. Versionless references pick up rotations after the five-minute API cache expires. No credentials belong in JSON, browser storage, source code, logs, or CLI arguments.

## Deployment prerequisites

1. Publish the updated UpTextCrm SQL project through the normal reviewed database deployment workflow. It adds `crm.external_identities`, a supporting unique key on sales, and internal `crmauth` procedures. Its post-deployment script grants the configured service user EXECUTE on `crmauth`. The API SQL identity must have this grant. Do not configure a generic REST service with `SqlSchema=crmauth`.
2. Deploy both API and CRM from their `entra` branches. This implementation does not publish the branches, migrate production, or enable an existing CRM tenant automatically.
3. Give the API runtime's Azure identity secret-read access to the approved vault through your normal infrastructure process. `DefaultAzureCredential` uses managed identity in Azure and Azure CLI/developer credentials locally. For a user-assigned managed identity, set `AZURE_CLIENT_ID`. No runtime identity permission was changed by this code implementation.
4. Configure the API deployment's `Entra` section below. These server-controlled allowlists cannot be changed by tenant administrators. They prevent redirect and Key Vault abuse.
5. Keep existing CORS allowed origins restricted to the actual CRM origins. Login state and completion are bound to the initiating browser; cross-origin AJAX does not require credentialed cookies.

Production `appsettings.json` defaults:

```json
{
  "Entra": {
    "Service": "crmapi",
    "CallbackUri": "https://api.uptext.com/auth/entra/callback",
    "AllowedReturnUris": ["https://crm.uptext.com/"],
    "AllowedKeyVaults": ["https://uptextsecrets.vault.azure.net"]
  }
}
```

For local development, merge the following into your ignored `appsettings.Development.json`, preserving existing service configuration:

```json
{
  "Entra": {
    "CallbackUri": "http://localhost:5092/auth/entra/callback",
    "AllowedReturnUris": ["http://localhost:5173/", "http://localhost:8080/"]
  }
}
```

The app registration already permits the production callback, HTTP localhost:5092, and HTTPS localhost:7219 at `/auth/entra/callback`. Use the matching server `CallbackUri` for your deployment; e.g. HTTPS localhost:7219 when running the HTTPS profile. The browser must reach the API under the same host used for the callback so its correlation cookie is sent. For SSH-tunnel port 8880, set the callback port accordingly; Entra ignores the port when matching HTTP localhost web redirects. No public VM application ports need to be exposed.

Only HTTPS callbacks are accepted outside Development. CRM return URIs must exactly match the server allowlist, including path/trailing slash. For subpath hosting, include the full CRM base path. These return addresses are separate from Entra's callback URI. The API does not build security-sensitive redirect URLs from untrusted forwarded headers; configure externally visible addresses explicitly.

## Invite-only workflow

1. Save the tenant's directory ID, client ID, and approved Key Vault reference. Enable Entra when ready; preserve a local administrator login during setup.
2. In the same Settings section, enter the user's **object ID from Entra Users** and CRM email. Supply an existing CRM user ID to explicitly link that account, or leave it empty to create a standard CRM user without a password. Names can be edited in Sales afterward. The API checks actual tenant-admin membership; browser permissions are not sufficient.
3. Tell the user the CRM login URL and CRM tenant name. No email is sent and no shareable invitation bearer token is needed: the invitation is bound to a specific verified directory/object identity. The user must complete first login within seven days.
4. The user enters the CRM tenant and clicks **Sign in with Microsoft**. The API validates signature, exact issuer/audience, lifetime, nonce, directory/object IDs, PKCE, and cookie correlation before resolving CRM membership.
5. Administrators can list identities and revoke access in Settings. Revoked or expired invitations never become automatic accounts. Revoke and re-invite explicitly to replace a binding. Disabling the CRM user/tenant or Entra provider prevents further Entra API access. Changing Entra directory/client requires explicit re-linking where bindings no longer match.

Unknown users in invite-only mode see an access-denied message without account creation. Automatic mode never grants administrator rights. Roles remain CRM-managed. Entra-authenticated sessions are checked against current user, tenant, binding, and provider settings on each API request; role changes require signing in again.

## Protocol and operations

- `GET /auth/entra/options?tenant=...` exposes only availability.
- `GET /auth/entra/start` starts browser-bound login.
- `GET /auth/entra/callback` redeems the code server-side and returns a short-lived one-time completion code, not a bearer token.
- `POST /auth/entra/complete` requires the initiating browser's verifier and returns the existing UpText token plus authoritative CRM user identity.
- `GET /auth/entra/me` returns the current enabled CRM user.
- `GET/POST /auth/entra/invitations` and `DELETE /auth/entra/invitations/{objectId}?directoryId=...` require tenant-admin authorization.

Auth responses are non-cacheable; raw credentials, authorization codes and tokens are not logged. Existing password-login JWT logging is redacted as well. No Graph delegated permissions or group/role synchronization are required. CRM logout clears the local session; it does not sign the user out of Microsoft globally.

The login state/completion store is deliberately single-process, bounded and short-lived. An API restart requires restarting sign-in. Multiple API replicas need a shared atomic state store or correctly configured sticky routing before rollout. Local JWT expiry remains governed by `JWT_HOURS`; this integration additionally enforces live membership checks for Entra tokens.

## Validation

- API unit tests include signed-token rejection for wrong signature, issuer, audience, directory, nonce and lifetime, plus Key Vault allowlists and replay/expiry controls.
- CRM tests cover completion binding, tenant mismatch, expiry, safe storage, and password-login compatibility.
- `python3 sqlwebapi/tests/entra_auth_integration.py --container <local-sql-container>` in UpTextCrm creates and drops a unique disposable test database, exercising invitation lifecycle, authorization, tenant isolation, account linking, and automatic provisioning. It never changes an existing database.
- Interactive live Entra login still needs a deployed schema, runtime Key Vault access, a configured CRM tenant, and an invited test identity.

## User names from Entra

New users receive `first_name` from the verified `given_name` ID-token claim and `last_name` from `family_name`. Login also fills missing name fields for invited or previously created Entra users, preserving any nonblank names maintained in CRM. Display names are not split to guess first/last names.

The UpText CRM Login registration requests `given_name` and `family_name` as optional ID-token claims. Customer-owned app registrations should enable these under Token configuration, and the Entra user profile must contain the corresponding names. The login flow already requests the `profile` scope; no additional Graph permissions are required.
