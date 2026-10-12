/** A saved greeting, as returned by `GET/POST /api/greetings`. */
export interface Greeting {
  id: number;
  message: string;
  /** ISO 8601 in UTC, always ending in `Z` (BR-14). */
  createdAtUtc: string;
}

/** Must match `Greeting.MessageMaxLength` in the API. */
export const GREETING_MAX_LENGTH = 200;
