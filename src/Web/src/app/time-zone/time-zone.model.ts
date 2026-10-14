/** One zone a user can choose, as returned by `GET /api/time-zones` (#95 AC3). */
export interface TimeZoneOption {
  /** A canonical IANA ID, or `UTC`. */
  id: string;
  /** The offset in force when the list was loaded: a label for the picker, never used to convert. */
  currentOffsetMinutes: number;
}
