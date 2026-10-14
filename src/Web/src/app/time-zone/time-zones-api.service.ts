import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';

import { LoadStatus } from '../shared/load-status';
import { TimeZoneOption } from './time-zone.model';

/**
 * The zones the picker offers, from `GET /api/time-zones`. The list comes from the server rather
 * than the browser's own database, so the picker never offers an ID the server would reject.
 */
@Injectable({ providedIn: 'root' })
export class TimeZonesApiService {
  private readonly http = inject(HttpClient);

  private readonly zonesState = signal<TimeZoneOption[]>([]);
  private readonly statusState = signal<LoadStatus>('idle');

  /** `UTC` first, then the canonical IANA zones in order, as the API returns them. */
  readonly zones = this.zonesState.asReadonly();
  readonly status = this.statusState.asReadonly();

  /** Loads the list once per page load; it only changes when the server's NodaTime does. */
  load(): void {
    if (this.statusState() === 'loading' || this.statusState() === 'ready') {
      return;
    }

    this.statusState.set('loading');
    this.http.get<TimeZoneOption[]>('/api/time-zones').subscribe({
      next: (zones) => {
        this.zonesState.set(zones);
        this.statusState.set('ready');
      },
      error: () => this.statusState.set('error'),
    });
  }
}
