/**
 * How the app writes an app timestamp (#95 AC4–AC6): in a time zone, followed by the UTC offset
 * in force at that instant, e.g. `13 Oct 2026 09:00 UTC+4`. The browser's time zone database
 * does the conversion, so the offset follows daylight saving at the instant, not today's.
 *
 * Only instants the app records go through here. Date and DateTime values from datasets are
 * data and are shown as stored (#95 business rules).
 */

/** The ID for "no offset". The picker lists it first; a zero offset is written the same way. */
export const UTC = 'UTC';

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

interface WallTime {
  year: number;
  month: number;
  day: number;
  hour: number;
  minute: number;
  second: number;
}

/** One formatter per zone: building one is slow, and a page can show hundreds of timestamps. */
const formatters = new Map<string, Intl.DateTimeFormat>();

function formatterFor(zoneId: string): Intl.DateTimeFormat {
  let formatter = formatters.get(zoneId);
  if (!formatter) {
    // Numeric parts only: the month name comes from MONTHS, because locales disagree on it
    // (en-GB writes "Sept").
    formatter = new Intl.DateTimeFormat('en-US', {
      timeZone: zoneId,
      year: 'numeric',
      month: 'numeric',
      day: 'numeric',
      hour: 'numeric',
      minute: 'numeric',
      second: 'numeric',
      hourCycle: 'h23',
    });
    formatters.set(zoneId, formatter);
  }
  return formatter;
}

function wallTimeIn(instant: Date, zoneId: string): WallTime {
  const parts = Object.fromEntries(
    formatterFor(zoneId)
      .formatToParts(instant)
      .map((part) => [part.type, Number(part.value)]),
  );
  return {
    year: parts['year'],
    month: parts['month'],
    day: parts['day'],
    hour: parts['hour'],
    minute: parts['minute'],
    second: parts['second'],
  };
}

/** The offset is how far the wall clock in the zone is from UTC at that instant, to the minute. */
function offsetMinutes(instant: Date, wall: WallTime): number {
  const wallAsUtc = Date.UTC(
    wall.year,
    wall.month - 1,
    wall.day,
    wall.hour,
    wall.minute,
    wall.second,
  );
  const wholeSecond = Math.floor(instant.getTime() / 1000) * 1000;
  return Math.round((wallAsUtc - wholeSecond) / 60_000);
}

const pad = (value: number) => String(value).padStart(2, '0');

/** An ISO 8601 string from the API (UTC, ending in `Z`, BR-14) or a Date, as a Date. */
export function toInstant(value: string | Date): Date {
  return value instanceof Date ? value : new Date(value);
}

/** `UTC+4`, `UTC+5:30`, `UTC-3`, and `UTC` for a zero offset (#95 AC4). */
export function formatUtcOffset(minutes: number): string {
  if (minutes === 0) {
    return UTC;
  }
  const sign = minutes > 0 ? '+' : '-';
  const hours = Math.floor(Math.abs(minutes) / 60);
  const rest = Math.abs(minutes) % 60;
  return `${UTC}${sign}${hours}${rest === 0 ? '' : `:${pad(rest)}`}`;
}

/** `Asia/Dubai (UTC+4)` for the picker and messages; plain `UTC` for UTC itself. */
export function formatZoneLabel(zoneId: string, offsetMinutes: number): string {
  return zoneId === UTC ? UTC : `${zoneId} (${formatUtcOffset(offsetMinutes)})`;
}

/** The UTC offset in force in `zoneId` at `instant`, in minutes. */
export function utcOffsetMinutes(instant: Date, zoneId: string): number {
  return offsetMinutes(instant, wallTimeIn(instant, zoneId));
}

/** `13 Oct 2026 09:00 UTC+4`: the wall time in `zoneId` (`d MMM yyyy HH:mm`) and the offset at that instant. */
export function formatTimestamp(instant: Date, zoneId: string): string {
  const wall = wallTimeIn(instant, zoneId);
  const offset = formatUtcOffset(offsetMinutes(instant, wall));
  return `${wall.day} ${MONTHS[wall.month - 1]} ${wall.year} ${pad(wall.hour)}:${pad(wall.minute)} ${offset}`;
}

/** `2026-10-13 05:00:00 UTC`: the instant itself, with seconds (#95 AC5). */
export function formatUtc(instant: Date): string {
  return `${toUtcIso(instant).slice(0, 19).replace('T', ' ')} ${UTC}`;
}

/** `2026-10-13T05:00:00Z`: the value of a `<time>` element's `datetime` attribute (#95 AC5). */
export function toUtcIso(instant: Date): string {
  return instant.toISOString().replace(/\.\d{3}Z$/, 'Z');
}

/** The browser's own IANA zone, or null when it doesn't report one. */
export function browserTimeZone(): string | null {
  return Intl.DateTimeFormat().resolvedOptions().timeZone || null;
}
