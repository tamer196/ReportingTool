import { ComponentFixture, TestBed } from '@angular/core/testing';

import { TimeZonePicker } from './time-zone-picker';
import { TimeZoneOption } from './time-zone.model';

const TEST_ZONES: TimeZoneOption[] = [
  { id: 'UTC', currentOffsetMinutes: 0 },
  { id: 'America/New_York', currentOffsetMinutes: -240 },
  { id: 'America/Sao_Paulo', currentOffsetMinutes: -180 },
  { id: 'Asia/Dubai', currentOffsetMinutes: 240 },
  { id: 'Asia/Kathmandu', currentOffsetMinutes: 345 },
  { id: 'Asia/Kolkata', currentOffsetMinutes: 330 },
  { id: 'Europe/London', currentOffsetMinutes: 60 },
];

describe('TimeZonePicker (#95 AC3, AC10)', () => {
  let fixture: ComponentFixture<TimeZonePicker>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [TimeZonePicker] }).compileComponents();

    fixture = TestBed.createComponent(TimeZonePicker);
    fixture.componentRef.setInput('zones', TEST_ZONES);
    fixture.componentRef.setInput('value', 'UTC');
    await fixture.whenStable();
  });

  const host = () => fixture.nativeElement as HTMLElement;
  const input = () => host().querySelector('input') as HTMLInputElement;
  const listbox = () => host().querySelector('[role="listbox"]') as HTMLElement;
  const options = () => Array.from(host().querySelectorAll<HTMLElement>('[role="option"]'));
  const optionTexts = () => options().map((option) => option.textContent?.trim());

  const type = async (text: string) => {
    input().value = text;
    input().dispatchEvent(new Event('input'));
    await fixture.whenStable();
  };
  const press = async (key: string) => {
    const event = new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true });
    input().dispatchEvent(event);
    await fixture.whenStable();
    return event;
  };

  it('is a labelled combobox that controls a listbox', () => {
    const label = host().querySelector('label') as HTMLLabelElement;

    expect(label.textContent?.trim()).toBe('Time zone');
    expect(label.htmlFor).toBe(input().id);
    expect(input().getAttribute('role')).toBe('combobox');
    expect(input().getAttribute('aria-controls')).toBe(listbox().id);
    expect(input().getAttribute('aria-expanded')).toBe('false');
    expect(listbox().hidden).toBe(true);
  });

  it('lists every zone with its current offset, UTC first', async () => {
    await press('ArrowDown');

    expect(input().getAttribute('aria-expanded')).toBe('true');
    expect(optionTexts()).toEqual([
      'UTC',
      'America/New_York (UTC-4)',
      'America/Sao_Paulo (UTC-3)',
      'Asia/Dubai (UTC+4)',
      'Asia/Kathmandu (UTC+5:45)',
      'Asia/Kolkata (UTC+5:30)',
      'Europe/London (UTC+1)',
    ]);
  });

  it('filters by any part of the ID, ignoring case, with a space matching an underscore', async () => {
    await type('new york');
    expect(optionTexts()).toEqual(['America/New_York (UTC-4)']);

    await type('  ASIA/K ');
    expect(optionTexts()).toEqual(['Asia/Kathmandu (UTC+5:45)', 'Asia/Kolkata (UTC+5:30)']);

    await type('paulo');
    expect(optionTexts()).toEqual(['America/Sao_Paulo (UTC-3)']);
  });

  it('says so when nothing matches', async () => {
    await type('Mars');

    expect(listbox().hidden).toBe(true);
    expect(host().querySelector('[role="status"]')?.textContent).toContain(
      'No time zone matches “Mars”',
    );
  });

  it('chooses the active option with the arrow keys and Enter, and announces it with its offset', async () => {
    await type('asia');
    expect(input().getAttribute('aria-activedescendant')).toBe(options()[0].id);

    await press('ArrowDown');
    expect(input().getAttribute('aria-activedescendant')).toBe(options()[1].id);
    expect(options()[1].classList).toContain('active');

    const enter = await press('Enter');

    expect(enter.defaultPrevented).toBe(true);
    expect(fixture.componentInstance.value()).toBe('Asia/Kathmandu');
    expect(input().value).toBe('Asia/Kathmandu (UTC+5:45)');
    expect(input().getAttribute('aria-expanded')).toBe('false');
    expect(input().hasAttribute('aria-activedescendant')).toBe(false);
  });

  it('opens on the chosen zone and marks it selected', async () => {
    fixture.componentRef.setInput('value', 'Asia/Dubai');
    await fixture.whenStable();

    await press('ArrowDown');

    const selected = options().find((option) => option.getAttribute('aria-selected') === 'true');
    expect(selected?.textContent?.trim()).toBe('Asia/Dubai (UTC+4)');
    expect(input().getAttribute('aria-activedescendant')).toBe(selected?.id);
  });

  it('chooses an option with the pointer without taking focus from the input', async () => {
    await press('ArrowDown');
    const mousedown = new MouseEvent('mousedown', { bubbles: true, cancelable: true });

    options()[3].dispatchEvent(mousedown);
    await fixture.whenStable();

    expect(mousedown.defaultPrevented).toBe(true);
    expect(fixture.componentInstance.value()).toBe('Asia/Dubai');
    expect(input().value).toBe('Asia/Dubai (UTC+4)');
  });

  it('closes on Esc, keeps the chosen zone and stops Esc there', async () => {
    await type('lon');

    const escape = await press('Escape');

    expect(escape.defaultPrevented).toBe(true);
    expect(input().getAttribute('aria-expanded')).toBe('false');
    expect(input().value).toBe('UTC');
    expect(fixture.componentInstance.value()).toBe('UTC');
  });

  it('lets Esc through when the list is closed, so the panel can close', async () => {
    const escape = await press('Escape');

    expect(escape.defaultPrevented).toBe(false);
  });

  it('drops unfinished typing when focus leaves', async () => {
    await type('dub');

    input().dispatchEvent(new FocusEvent('blur'));
    await fixture.whenStable();

    expect(input().value).toBe('UTC');
    expect(fixture.componentInstance.value()).toBe('UTC');
  });
});
