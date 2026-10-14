import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';

import { Greeting } from './greeting.model';

export type LoadStatus = 'idle' | 'loading' | 'ready' | 'error';

/**
 * Transport and state for `/api/greetings`, the hello-world slice. Components read the signals
 * and call the methods; they never call HttpClient themselves.
 */
@Injectable({ providedIn: 'root' })
export class GreetingsApiService {
  private readonly http = inject(HttpClient);
  private readonly url = '/api/greetings';

  private readonly greetingsState = signal<Greeting[]>([]);
  private readonly statusState = signal<LoadStatus>('idle');

  /** Newest first, as the API returns them. */
  readonly greetings = this.greetingsState.asReadonly();
  readonly status = this.statusState.asReadonly();

  load(): void {
    this.statusState.set('loading');
    this.http.get<Greeting[]>(this.url).subscribe({
      next: (greetings) => {
        this.greetingsState.set(greetings);
        this.statusState.set('ready');
      },
      error: () => this.statusState.set('error'),
    });
  }

  /** Saves a greeting and puts it at the top of the list once the API confirms it. */
  add(message: string): Observable<Greeting> {
    return this.http
      .post<Greeting>(this.url, { message })
      .pipe(tap((created) => this.greetingsState.update((list) => [created, ...list])));
  }
}
