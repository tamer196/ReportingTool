import { Component, computed, inject, input, signal } from '@angular/core';

import { formatTimestamp, formatUtc, toInstant, toUtcIso } from './time-format';
import { UserTimeZoneService } from './user-time-zone.service';

let nextId = 0;

/**
 * The one way screens show an app timestamp (#95): `13 Oct 2026 09:00 UTC+4` in the user's time
 * zone, with the offset in force at that instant. Hovering or keyboard focus shows the UTC value
 * and the zone (`2026-10-13 05:00:00 UTC · shown in Asia/Dubai`); Esc hides it again (WCAG 1.4.13).
 *
 * Use it only for instants the app records (runs, versions, notifications, audit entries).
 * Date and DateTime values from datasets are data: show them as stored.
 */
@Component({
  selector: 'app-timestamp',
  templateUrl: './timestamp.html',
  styleUrl: './timestamp.css',
  host: {
    '(mouseenter)': 'hovered.set(true); dismissed.set(false)',
    '(mouseleave)': 'hovered.set(false)',
    '(focusin)': 'focused.set(true); dismissed.set(false)',
    '(focusout)': 'focused.set(false)',
    '(keydown.escape)': 'dismiss($event)',
  },
})
export class Timestamp {
  /** The instant: an ISO 8601 string in UTC from the API (ending in `Z`, BR-14) or a Date. */
  readonly value = input.required<string | Date>();
  /** Shows the time in this zone instead of the user's, e.g. the time zone preview (#95 AC7). */
  readonly zoneId = input<string>();

  private readonly userTimeZone = inject(UserTimeZoneService);

  protected readonly tooltipId = `app-timestamp-tooltip-${nextId++}`;
  protected readonly hovered = signal(false);
  protected readonly focused = signal(false);
  protected readonly dismissed = signal(false);
  protected readonly tooltipShown = computed(
    () => (this.hovered() || this.focused()) && !this.dismissed(),
  );

  private readonly instant = computed(() => toInstant(this.value()));
  private readonly zone = computed(() => this.zoneId() ?? this.userTimeZone.zoneId());

  protected readonly datetime = computed(() => toUtcIso(this.instant()));
  protected readonly text = computed(() => formatTimestamp(this.instant(), this.zone()));
  protected readonly utcText = computed(
    () => `${formatUtc(this.instant())} · shown in ${this.zone()}`,
  );

  /** Esc hides an open tooltip and goes no further, so it doesn't also close a panel around it. */
  protected dismiss(event: Event): void {
    if (this.tooltipShown()) {
      event.stopPropagation();
      this.dismissed.set(true);
    }
  }
}
