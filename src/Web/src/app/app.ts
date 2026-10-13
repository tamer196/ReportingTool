import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { OidcSecurityService } from 'angular-auth-oidc-client';

/** Application shell: the product header, navigation, the signed-in user and the routed page. */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  private readonly oidc = inject(OidcSecurityService);
  private readonly auth = toSignal(this.oidc.userData$);

  protected readonly user = computed(() => {
    const data = this.auth()?.userData as { preferred_username?: string } | null | undefined;
    return data?.preferred_username ?? null;
  });

  protected signOut(): void {
    this.oidc.logoff().subscribe();
  }
}
