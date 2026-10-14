import { Routes } from '@angular/router';

export const routes: Routes = [
  {
    path: '',
    title: 'Reporting Tool',
    loadComponent: () => import('./greetings/greetings-page').then((m) => m.GreetingsPage),
  },
  { path: '**', redirectTo: '' },
];
