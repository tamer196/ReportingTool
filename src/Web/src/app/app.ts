import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

import { TimeZoneSettings } from './time-zone/time-zone-settings';

/**
 * Application shell: the product header and the routed page. The time zone setting sits in the
 * header until the app shell's user menu (#152) takes it in.
 */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, TimeZoneSettings],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {}
