import { HttpClient } from '@angular/common/http';
import {
  OpenIdConfiguration,
  StsConfigHttpLoader,
  StsConfigLoader,
} from 'angular-auth-oidc-client';
import { map } from 'rxjs';

/** Written by nginx from the container environment (D-15: one image for every environment). */
interface RuntimeConfig {
  authority: string;
  clientId: string;
}

/**
 * OIDC with code + PKCE against Keycloak (D-17). Standard OIDC only, so another provider is a
 * configuration change. Tokens are attached to the Reports API and nowhere else.
 */
export function oidcConfigLoader(http: HttpClient): StsConfigLoader {
  return new StsConfigHttpLoader(
    http.get<RuntimeConfig>('/config.json').pipe(
      map((runtime): OpenIdConfiguration => ({
        authority: runtime.authority,
        clientId: runtime.clientId,
        redirectUrl: window.location.origin,
        postLogoutRedirectUri: window.location.origin,
        scope: 'openid profile offline_access',
        responseType: 'code',
        silentRenew: true,
        useRefreshToken: true,
        secureRoutes: ['/api/reports/'],
      })),
    ),
  );
}
