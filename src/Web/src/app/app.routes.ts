import { Routes } from '@angular/router';
import { autoLoginPartialRoutesGuard } from 'angular-auth-oidc-client';

export const routes: Routes = [
  {
    path: '',
    title: 'Reporting Tool',
    loadComponent: () => import('./greetings/greetings-page').then((m) => m.GreetingsPage),
  },
  {
    // RT-108 PoC: the report designer, behind Keycloak sign-in (gate G2).
    path: 'designer',
    title: 'Report designer — Reporting Tool',
    canActivate: [autoLoginPartialRoutesGuard],
    loadComponent: () => import('./designer/designer-page').then((m) => m.DesignerPage),
  },
  { path: '**', redirectTo: '' },
];
