import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { UserTimeZoneService } from '../shared/time/user-time-zone.service';
import { TimeZoneSettings } from './time-zone-settings';
import { TimeZoneOption } from './time-zone.model';

const ZONES: TimeZoneOption[] = [
  { id: 'UTC', currentOffsetMinutes: 0 },
  { id: 'Asia/Dubai', currentOffsetMinutes: 240 },
  { id: 'Asia/Kathmandu', currentOffsetMinutes: 345 },
  { id: 'Europe/London', currentOffsetMinutes: 60 },
];

describe('TimeZoneSettings (#95 AC1, AC7)', () => {
  let fixture: ComponentFixture<TimeZoneSettings>;
  let http: HttpTestingController;
  let userTimeZone: UserTimeZoneService;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TimeZoneSettings],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    userTimeZone = TestBed.inject(UserTimeZoneService);
    userTimeZone.apply('UTC');
    fixture = TestBed.createComponent(TimeZoneSettings);
    document.body.appendChild(fixture.nativeElement);
    await fixture.whenStable();
  });

  afterEach(() => {
    http.verify();
    (fixture.nativeElement as HTMLElement).remove();
  });

  const host = () => fixture.nativeElement as HTMLElement;
  const toggle = () => host().querySelector('button.toggle') as HTMLButtonElement;
  const panel = () => host().querySelector('form.panel');
  const input = () => host().querySelector('[role="combobox"]') as HTMLInputElement;
  const preview = () => host().querySelector('#time-zone-preview') as HTMLElement;
  const status = () => host().querySelector('.saved[role="status"]') as HTMLElement;

  const openPanel = async () => {
    toggle().click();
    await fixture.whenStable();
    http.expectOne('/api/time-zones').flush(ZONES);
    await fixture.whenStable();
  };
  const choose = async (text: string) => {
    input().value = text;
    input().dispatchEvent(new Event('input'));
    await fixture.whenStable();
    input().dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }),
    );
    await fixture.whenStable();
  };

  it("shows the user's zone on a button that opens the setting", async () => {
    expect(toggle().textContent?.trim()).toBe('Time zone: UTC');
    expect(toggle().getAttribute('aria-expanded')).toBe('false');
    expect(panel()).toBeNull();

    await openPanel();

    expect(toggle().getAttribute('aria-expanded')).toBe('true');
    expect(panel()?.id).toBe(toggle().getAttribute('aria-controls'));
    expect(document.activeElement).toBe(input());
    expect(input().value).toBe('UTC');
  });

  it('previews the current time in the chosen zone before saving (AC7)', async () => {
    await openPanel();
    expect(preview().textContent).toContain('Time now in UTC');
    expect(input().getAttribute('aria-describedby')).toBe(preview().id);

    await choose('kathmandu');

    expect(preview().textContent).toContain('Time now in Asia/Kathmandu');
    expect(preview().querySelector('time')?.textContent).toMatch(
      /^\d{1,2} \w{3} \d{4} \d{2}:\d{2} UTC\+5:45$/,
    );
    expect(userTimeZone.zoneId()).toBe('UTC');
  });

  it('applies a saved zone at once and says so (AC1)', async () => {
    await openPanel();
    await choose('dubai');

    (panel() as HTMLFormElement).dispatchEvent(new Event('submit', { cancelable: true }));
    await fixture.whenStable();

    expect(userTimeZone.zoneId()).toBe('Asia/Dubai');
    expect(status().textContent?.trim()).toBe('Times are now shown in Asia/Dubai (UTC+4)');
    expect(toggle().textContent?.trim()).toBe('Time zone: Asia/Dubai');
    expect(panel()).toBeNull();
    expect(document.activeElement).toBe(toggle());
  });

  it('keeps the saved zone on Cancel and on Esc', async () => {
    await openPanel();
    await choose('london');
    (host().querySelector('button.secondary') as HTMLButtonElement).click();
    await fixture.whenStable();

    expect(panel()).toBeNull();
    expect(userTimeZone.zoneId()).toBe('UTC');
    expect(document.activeElement).toBe(toggle());

    toggle().click();
    await fixture.whenStable();
    expect(input().value).toBe('UTC');
    await choose('london');
    input().dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }),
    );
    await fixture.whenStable();

    expect(panel()).toBeNull();
    expect(userTimeZone.zoneId()).toBe('UTC');
    expect(status().textContent?.trim()).toBe('');
  });

  it('says so when the list of zones cannot be loaded', async () => {
    toggle().click();
    await fixture.whenStable();
    http
      .expectOne('/api/time-zones')
      .flush('down', { status: 503, statusText: 'Service Unavailable' });
    await fixture.whenStable();

    expect(host().querySelector('[role="alert"]')?.textContent).toContain('could not be loaded');
  });
});
