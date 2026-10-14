import { Component, OnInit, inject, signal } from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';

import { Timestamp } from '../shared/time/timestamp';
import { SystemInfoApiService } from '../system/system-info-api.service';
import { GREETING_MAX_LENGTH } from './greeting.model';
import { GreetingsApiService } from './greetings-api.service';

/**
 * The hello-world page (RT-106). It proves the whole platform path from the browser: Angular →
 * API → SQL Server and back. Times are stored and sent in UTC (BR-14) and shown in the user's
 * time zone through the shared timestamp component (#95).
 */
@Component({
  selector: 'app-greetings-page',
  imports: [ReactiveFormsModule, Timestamp],
  templateUrl: './greetings-page.html',
  styleUrl: './greetings-page.css',
})
export class GreetingsPage implements OnInit {
  protected readonly greetingsApi = inject(GreetingsApiService);
  protected readonly systemInfo = inject(SystemInfoApiService);

  protected readonly maxLength = GREETING_MAX_LENGTH;
  protected readonly form = new FormGroup({
    message: new FormControl('', {
      nonNullable: true,
      // Same rules as the API: required, not just spaces, at most GREETING_MAX_LENGTH characters.
      validators: [Validators.required, Validators.pattern(/\S/), Validators.maxLength(GREETING_MAX_LENGTH)],
    }),
  });
  protected readonly message = this.form.controls.message;
  protected readonly saving = signal(false);
  protected readonly saveError = signal(false);

  ngOnInit(): void {
    this.greetingsApi.load();
    this.systemInfo.load();
  }

  protected save(): void {
    if (this.message.invalid) {
      this.message.markAsTouched();
      return;
    }

    this.saving.set(true);
    this.saveError.set(false);
    this.greetingsApi.add(this.message.value).subscribe({
      next: () => {
        this.form.reset();
        this.saving.set(false);
      },
      error: () => {
        this.saveError.set(true);
        this.saving.set(false);
      },
    });
  }
}
