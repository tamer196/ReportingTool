import {
  Component,
  ElementRef,
  afterRenderEffect,
  computed,
  inject,
  input,
  linkedSignal,
  model,
  signal,
} from '@angular/core';

import { formatZoneLabel } from '../shared/time/time-format';
import { TimeZoneOption } from './time-zone.model';

let nextId = 0;

/**
 * The time zone combobox (#95 AC3, AC10): an editable combobox with a filtered list, after the
 * WAI-ARIA "list autocomplete" pattern. Focus stays in the input; the arrow keys move the active
 * option, Enter chooses it and Esc closes the list. Options read `Asia/Dubai (UTC+4)`, and so does
 * the input once a zone is chosen, so screen readers announce the zone with its offset.
 */
@Component({
  selector: 'app-time-zone-picker',
  templateUrl: './time-zone-picker.html',
  styleUrl: './time-zone-picker.css',
})
export class TimeZonePicker {
  readonly zones = input.required<readonly TimeZoneOption[]>();
  /** The chosen zone ID. It changes when an option is chosen, not while typing. */
  readonly value = model.required<string>();
  /** The ID of an element that describes the choice, e.g. the preview. */
  readonly describedBy = input<string>();

  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly id = nextId++;

  protected readonly inputId = `app-time-zone-input-${this.id}`;
  protected readonly listboxId = `app-time-zone-listbox-${this.id}`;
  protected readonly open = signal(false);
  /** What was typed, or null while the list isn't filtered. */
  protected readonly query = signal<string | null>(null);
  protected readonly activeIndex = signal(-1);
  protected readonly inputText = linkedSignal(() => this.labelOf(this.value()));

  /**
   * Any part of the ID, ignoring case, with a space matching `_`: "new york" finds America/New_York.
   */
  protected readonly matches = computed(() => {
    const query = this.query();
    if (query === null) {
      return this.zones();
    }
    const needle = query.trim().toLowerCase().replace(/\s+/g, '_');
    return this.zones().filter((zone) => zone.id.toLowerCase().includes(needle));
  });

  protected readonly activeOptionId = computed(() =>
    this.open() && this.activeIndex() >= 0 ? this.optionId(this.activeIndex()) : null,
  );

  constructor() {
    afterRenderEffect(() => {
      const optionId = this.activeOptionId();
      if (optionId) {
        // jsdom has no scrollIntoView.
        this.host.nativeElement
          .querySelector(`[id="${optionId}"]`)
          ?.scrollIntoView?.({ block: 'nearest' });
      }
    });
  }

  protected optionId(index: number): string {
    return `${this.listboxId}-${index}`;
  }

  protected label(zone: TimeZoneOption): string {
    return formatZoneLabel(zone.id, zone.currentOffsetMinutes);
  }

  protected onInput(event: Event): void {
    const text = (event.target as HTMLInputElement).value;
    this.inputText.set(text);
    this.query.set(text);
    this.open.set(true);
    this.activeIndex.set(this.matches().length > 0 ? 0 : -1);
  }

  protected onKeydown(event: KeyboardEvent): void {
    const count = this.matches().length;
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        if (this.open()) {
          this.activeIndex.update((index) => Math.min(index + 1, count - 1));
        } else {
          this.openList();
        }
        break;
      case 'ArrowUp':
        event.preventDefault();
        if (this.open()) {
          this.activeIndex.update((index) => Math.max(index - 1, 0));
        } else {
          this.openList();
        }
        break;
      case 'Enter':
        if (this.open() && this.activeIndex() >= 0) {
          event.preventDefault();
          this.choose(this.matches()[this.activeIndex()]);
        }
        break;
      case 'Escape':
        // Only the open list handles Esc; otherwise it reaches the panel, which closes.
        if (this.open()) {
          event.preventDefault();
          event.stopPropagation();
          this.close();
        }
        break;
    }
  }

  protected toggle(): void {
    if (this.open()) {
      this.close();
    } else {
      this.openList();
    }
  }

  /** Chosen with the pointer. Prevents the default so focus stays in the input. */
  protected pick(event: MouseEvent, zone: TimeZoneOption): void {
    event.preventDefault();
    this.choose(zone);
  }

  /** Closes the list and puts the chosen zone back in the input, dropping any unfinished typing. */
  protected close(): void {
    this.open.set(false);
    this.query.set(null);
    this.activeIndex.set(-1);
    this.inputText.set(this.labelOf(this.value()));
  }

  private openList(): void {
    this.open.set(true);
    this.activeIndex.set(
      Math.max(
        this.matches().findIndex((zone) => zone.id === this.value()),
        0,
      ),
    );
  }

  private choose(zone: TimeZoneOption): void {
    this.value.set(zone.id);
    this.close();
  }

  private labelOf(id: string): string {
    const zone = this.zones().find((option) => option.id === id);
    return zone ? this.label(zone) : id;
  }
}
