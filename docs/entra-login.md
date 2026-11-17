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

## Self-service organization connections

Tenant administrators can choose **Connect Microsoft organization** in Settings → Microsoft Entra login. Save pending settings first. Microsoft asks an organization administrator to consent to the shared UpText app's delegated `CRM.Connect` permission. This custom permission is admin-only and provides no Microsoft Graph, mail, contacts, or directory-listing access.

The API uses `/organizations/v2.0/adminconsent`, then a tenant-specific authorization-code + PKCE flow. The admin-consent callback's `tenant` and `admin_consent` query parameters are **not** trusted as proof. Before enabling login, the API verifies a signed ID token (issuer, audience, directory, nonce, lifetime) and a signed v2 access token for the separate connection resource carrying `scp=CRM.Connect` and the same directory/object identity, with the shared client in `azp`. The short-lived completion also requires the initiating browser verifier and a current JWT for the same CRM tenant and administrator. SQL rechecks active administrator membership and atomically rejects concurrent configuration changes.

Only that CRM tenant's Entra configuration changes. Existing settings, user roles, and identity bindings are preserved; connecting never links an account by email or grants CRM administrator rights. Existing users must be explicitly invited/linked. The completion screen shows the consenting user's Microsoft object ID for this purpose. Invite-only remains the default; admins can choose automatic standard-user provisioning separately. Reconnecting with another directory/client invalidates existing Entra bindings; retain a password administrator when changing providers.

API deployment configuration:

```json
{
  "Entra": {
    "SharedClientId": "3b001657-7fda-4407-a7dc-27250b62c4b6",
    "SharedResourceId": "cc5f8c55-4013-4c50-b9e8-a50e1f0aea06",
    "SharedClientSecretUri": "https://uptextsecrets.vault.azure.net/secrets/uptext-crm-organizations-client-secret"
  }
}
```

The shared registration **UpText CRM Organizations** supports `AzureADMultipleOrgs`, requests `api://<SharedResourceId>/CRM.Connect` from the separate multitenant **UpText CRM Connection Permission** resource. That resource exposes an enabled **Admin** delegated scope, issues v2 access tokens (`api.requestedAccessTokenVersion=2`), and lists the shared client in `knownClientApplications` for bundled consent. The client lists the resource scope in `requiredResourceAccess`. A separate resource avoids Microsoft token-to-self consent exemptions. Register both `/auth/entra/callback` and `/auth/entra/onboarding/callback` under Web for each API deployment. The existing single-tenant **UpText CRM Login** registration remains available for manually configured tenants. The API runtime identity needs secret-get access to the shared credential in the approved vault. Deploy `crmauth.connection_post` with the CRM database project before enabling the feature. Its access follows the existing internal `crmauth` schema EXECUTE grant; never expose that schema through generic REST.

The new endpoints are `/auth/entra/onboarding/options`, `POST .../start`, `GET .../launch`, `GET .../callback`, and `POST .../complete`. Browser transactions expire after 15 minutes and completions after two minutes. They use the same single-process/sticky-routing requirement as sign-in. External organizations' Conditional Access, consent policies, and publisher-verification requirements still apply. Do not claim another organization's consent is verified until a real administrator completes the live flow.

Production rollout on 2026-11-16 uses versioned `uptext/uptextapi:entra-selfservice-20261116` and `uptext/uptextcrm:entra-selfservice-20261116` images on `upubuntu01`. The previous images are tagged `before-entra-selfservice`; production `.env.before-entra-selfservice` and `.azure-production.env.before-entra-selfservice` retain rollback configuration. The shared app credential expires 2027-11-16 18:52:24 UTC. The app currently has no verified publisher; organizations that require verified publishers may block consent. Publisher/domain verification is separate from app registration and has not been completed.
