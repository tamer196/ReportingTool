import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';

/** `GET /api/system/info`: which build runs in which environment. */
export interface SystemInfo {
  service: string;
  /** Informational version; ends in `+<git sha>` for pipeline builds. */
  version: string;
  environment: string;
  serverTimeUtc: string;
}

/** Loads the API's build and environment once, for the page footer. QA uses it to confirm what is deployed. */
@Injectable({ providedIn: 'root' })
export class SystemInfoApiService {
  private readonly http = inject(HttpClient);
  private readonly infoState = signal<SystemInfo | null>(null);

  /** Null until loaded, or when the API cannot be reached. */
  readonly info = this.infoState.asReadonly();

  load(): void {
    this.http.get<SystemInfo>('/api/system/info').subscribe({
      next: (info) => this.infoState.set(info),
      error: () => this.infoState.set(null),
    });
  }
}
