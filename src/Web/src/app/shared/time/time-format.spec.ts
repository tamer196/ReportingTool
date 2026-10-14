import {
  formatTimestamp,
  formatUtc,
  formatUtcOffset,
  toInstant,
  toUtcIso,
  utcOffsetMinutes,
} from './time-format';

describe('time format (#95 AC4–AC6)', () => {
  describe('formatTimestamp', () => {
    it.each([
      // AC6: the Europe/London offset in force at each instant, across both changes in 2026.
      ['2026-10-25T00:30:00Z', 'Europe/London', '25 Oct 2026 01:30 UTC+1'],
      ['2026-10-25T01:30:00Z', 'Europe/London', '25 Oct 2026 01:30 UTC'],
      ['2026-03-29T01:30:00Z', 'Europe/London', '29 Mar 2026 02:30 UTC+1'],
      ['2026-03-29T00:30:00Z', 'Europe/London', '29 Mar 2026 00:30 UTC'],
      // AC6: a zone that isn't a whole number of hours.
      ['2026-10-13T05:00:00Z', 'Asia/Kathmandu', '13 Oct 2026 10:45 UTC+5:45'],
      // AC4 examples.
      ['2026-10-13T05:00:00Z', 'Asia/Dubai', '13 Oct 2026 09:00 UTC+4'],
      ['2026-10-13T05:00:00Z', 'Asia/Kolkata', '13 Oct 2026 10:30 UTC+5:30'],
      ['2026-10-13T05:00:00Z', 'America/Sao_Paulo', '13 Oct 2026 02:00 UTC-3'],
      ['2026-10-13T05:00:00Z', 'UTC', '13 Oct 2026 05:00 UTC'],
      // Western zones a day behind, the 24-hour clock at midnight, and September's short name.
      ['2026-01-01T03:15:00Z', 'America/New_York', '31 Dec 2025 22:15 UTC-5'],
      ['2026-09-05T20:00:00Z', 'Asia/Dubai', '6 Sep 2026 00:00 UTC+4'],
    ])('shows %s in %s as %s', (instant, zone, expected) => {
      expect(formatTimestamp(toInstant(instant), zone)).toBe(expected);
    });

    it('drops the seconds rather than rounding them', () => {
      expect(formatTimestamp(toInstant('2026-10-13T05:00:59.999Z'), 'UTC')).toBe(
        '13 Oct 2026 05:00 UTC',
      );
    });
  });

  describe('formatUtcOffset', () => {
    it.each([
      [0, 'UTC'],
      [60, 'UTC+1'],
      [240, 'UTC+4'],
      [330, 'UTC+5:30'],
      [345, 'UTC+5:45'],
      [-180, 'UTC-3'],
      [-570, 'UTC-9:30'],
      [840, 'UTC+14'],
    ])('writes %i minutes as %s', (minutes, expected) => {
      expect(formatUtcOffset(minutes)).toBe(expected);
    });
  });

  it('gives the offset in force at the instant, not today', () => {
    expect(utcOffsetMinutes(toInstant('2026-07-01T12:00:00Z'), 'Europe/London')).toBe(60);
    expect(utcOffsetMinutes(toInstant('2026-12-01T12:00:00Z'), 'Europe/London')).toBe(0);
    expect(utcOffsetMinutes(toInstant('2026-07-01T12:00:00Z'), 'Australia/Lord_Howe')).toBe(630);
  });

  it('writes the UTC instant with seconds for the tooltip and the datetime attribute (AC5)', () => {
    const instant = toInstant('2026-10-13T05:00:07.250Z');

    expect(formatUtc(instant)).toBe('2026-10-13 05:00:07 UTC');
    expect(toUtcIso(instant)).toBe('2026-10-13T05:00:07Z');
  });

  it('takes a Date as it is', () => {
    const date = new Date(Date.UTC(2026, 9, 13, 5));

    expect(toInstant(date)).toBe(date);
  });
});
