import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  inject,
  signal,
  viewChild,
} from '@angular/core';

import { Timestamp } from '../shared/time/timestamp';
import { formatZoneLabel, utcOffsetMinutes } from '../shared/time/time-format';
import { UserTimeZoneService } from '../shared/time/user-time-zone.service';
import { TimeZonePicker } from './time-zone-picker';
import { TimeZonesApiService } from './time-zones-api.service';

/** How often the preview's "now" moves on. It shows minutes, so a few seconds late is fine. */
const PREVIEW_TICK_MS = 10_000;
/** How long the "Times are now shown in …" message stays. */
const SAVED_MESSAGE_MS = 6_000;

/**
 * The *Time zone* setting (#95 AC1, AC7): a button that opens a small panel with the picker, a
 * live preview of the current time in the chosen zone, and Save and Cancel. Nothing changes
 * until Save; then every timestamp on the page follows at once.
 */
@Component({
  selector: 'app-time-zone-settings',
  imports: [TimeZonePicker, Timestamp],
  templateUrl: './time-zone-settings.html',
  styleUrl: './time-zone-settings.css',
})
export class TimeZoneSettings {
  protected readonly timeZonesApi = inject(TimeZonesApiService);
  private readonly userTimeZone = inject(UserTimeZoneService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);

  private readonly toggleButton = viewChild.required<ElementRef<HTMLButtonElement>>('toggleButton');

  protected readonly zoneId = this.userTimeZone.zoneId;
  protected readonly panelOpen = signal(false);
  /** The zone chosen in the picker and shown in the preview, not saved yet. */
  protected readonly pending = signal(this.zoneId());
  protected readonly now = signal(new Date());
  protected readonly savedMessage = signal('');

  private savedMessageTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    const tick = setInterval(() => this.now.set(new Date()), PREVIEW_TICK_MS);
    inject(DestroyRef).onDestroy(() => {
      clearInterval(tick);
      clearTimeout(this.savedMessageTimer);
    });
  }

  protected toggle(): void {
    if (this.panelOpen()) {
      this.closePanel();
      return;
    }

    this.timeZonesApi.load();
    this.pending.set(this.zoneId());
    this.now.set(new Date());
    this.panelOpen.set(true);
    afterNextRender(
      () => this.host.nativeElement.querySelector<HTMLInputElement>('[role="combobox"]')?.focus(),
      {
        injector: this.injector,
      },
    );
  }

  protected save(event: Event): void {
    event.preventDefault();
    const zoneId = this.pending();
    this.userTimeZone.apply(zoneId);
    this.showSavedMessage(
      `Times are now shown in ${formatZoneLabel(zoneId, utcOffsetMinutes(new Date(), zoneId))}`,
    );
    this.closePanel();
  }

  /** Cancel, Esc or the button again: the pending choice is dropped and focus returns to the button. */
  protected closePanel(): void {
    this.panelOpen.set(false);
    this.toggleButton().nativeElement.focus();
  }

  private showSavedMessage(message: string): void {
    clearTimeout(this.savedMessageTimer);
    this.savedMessage.set(message);
    this.savedMessageTimer = setTimeout(() => this.savedMessage.set(''), SAVED_MESSAGE_MS);
  }
}
