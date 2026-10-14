import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';

/** Application shell: the product header and the routed page. */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {}
