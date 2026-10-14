import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Timestamp } from './timestamp';
import { UserTimeZoneService } from './user-time-zone.service';

describe('Timestamp (#95 AC4–AC6, AC10)', () => {
  let fixture: ComponentFixture<Timestamp>;
  let userTimeZone: UserTimeZoneService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [Timestamp] }).compileComponents();

    userTimeZone = TestBed.inject(UserTimeZoneService);
    userTimeZone.apply('Asia/Dubai');
    fixture = TestBed.createComponent(Timestamp);
    fixture.componentRef.setInput('value', '2026-10-13T05:00:00Z');
    await fixture.whenStable();
  });

  const host = () => fixture.nativeElement as HTMLElement;
  const time = () => host().querySelector('time') as HTMLTimeElement;
  const tooltip = () => host().querySelector('[role="tooltip"]') as HTMLElement;

  it("shows the instant in the user's zone with the offset at that instant", () => {
    expect(time().textContent?.trim()).toBe('13 Oct 2026 09:00 UTC+4');
  });

  it('is a time element whose datetime is the UTC instant', () => {
    expect(time().getAttribute('datetime')).toBe('2026-10-13T05:00:00Z');
  });

  it('describes itself with the UTC value and the zone, hidden until hover or focus', () => {
    expect(time().getAttribute('aria-describedby')).toBe(tooltip().id);
    expect(tooltip().textContent?.trim()).toBe('2026-10-13 05:00:00 UTC · shown in Asia/Dubai');
    expect(tooltip().hidden).toBe(true);
  });

  it('shows the UTC value while hovered', async () => {
    host().dispatchEvent(new MouseEvent('mouseenter'));
    await fixture.whenStable();
    expect(tooltip().hidden).toBe(false);

    host().dispatchEvent(new MouseEvent('mouseleave'));
    await fixture.whenStable();
    expect(tooltip().hidden).toBe(true);
  });

  it('can be reached by keyboard and shows the UTC value on focus until Esc', async () => {
    expect(time().tabIndex).toBe(0);

    time().dispatchEvent(new FocusEvent('focusin', { bubbles: true }));
    await fixture.whenStable();
    expect(tooltip().hidden).toBe(false);

    const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true });
    const reachedParent = vi.fn();
    host().parentElement?.addEventListener('keydown', reachedParent);
    time().dispatchEvent(escape);
    await fixture.whenStable();
    expect(tooltip().hidden).toBe(true);
    expect(reachedParent).not.toHaveBeenCalled();

    time().dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    expect(reachedParent).toHaveBeenCalledTimes(1);

    time().dispatchEvent(new FocusEvent('focusout', { bubbles: true }));
    time().dispatchEvent(new FocusEvent('focusin', { bubbles: true }));
    await fixture.whenStable();
    expect(tooltip().hidden).toBe(false);
  });

  it('follows a saved time zone change without a reload (AC1)', async () => {
    userTimeZone.apply('Asia/Kathmandu');
    await fixture.whenStable();

    expect(time().textContent?.trim()).toBe('13 Oct 2026 10:45 UTC+5:45');
    expect(tooltip().textContent).toContain('shown in Asia/Kathmandu');
    expect(time().getAttribute('datetime')).toBe('2026-10-13T05:00:00Z');
  });

  it('uses the daylight-saving offset of the instant (AC6)', async () => {
    userTimeZone.apply('Europe/London');
    fixture.componentRef.setInput('value', '2026-10-25T00:30:00Z');
    await fixture.whenStable();
    expect(time().textContent?.trim()).toBe('25 Oct 2026 01:30 UTC+1');

    fixture.componentRef.setInput('value', '2026-10-25T01:30:00Z');
    await fixture.whenStable();
    expect(time().textContent?.trim()).toBe('25 Oct 2026 01:30 UTC');
  });

  it('shows another zone when one is given, for the time zone preview', async () => {
    fixture.componentRef.setInput('zoneId', 'America/Sao_Paulo');
    await fixture.whenStable();

    expect(time().textContent?.trim()).toBe('13 Oct 2026 02:00 UTC-3');
    expect(tooltip().textContent).toContain('shown in America/Sao_Paulo');
  });
});
