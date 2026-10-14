import { Injectable, signal } from '@angular/core';

import { UTC, browserTimeZone } from './time-format';

/**
 * The IANA zone every app timestamp on the page is shown in (#95 AC4). It's one signal, so a
 * saved change reaches every `<app-timestamp>` at once, with no reload (AC1).
 */
@Injectable({ providedIn: 'root' })
export class UserTimeZoneService {
  private readonly zoneState = signal(browserTimeZone() ?? UTC);

  readonly zoneId = this.zoneState.asReadonly();

  /** Shows every timestamp in `zoneId` from now on. Call it with a zone the server accepted. */
  apply(zoneId: string): void {
    this.zoneState.set(zoneId);
  }
}
