import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { OidcSecurityService } from 'angular-auth-oidc-client';
import { of } from 'rxjs';

import { App } from './app';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter([]),
        {
          provide: OidcSecurityService,
          useValue: {
            userData$: of({ userData: { preferred_username: 'author.ar' } }),
            logoff: () => of(null),
          },
        },
      ],
    }).compileComponents();
  });

  it('shows the product name and a skip link to the main content', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const page = fixture.nativeElement as HTMLElement;

    expect(page.querySelector('.product')?.textContent).toContain('Reporting Tool');
    expect(page.querySelector('a.skip-link')?.getAttribute('href')).toBe('#main');
  });

  it('links to the report designer and shows the signed-in user', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();
    const page = fixture.nativeElement as HTMLElement;

    expect(page.querySelector('a[href="/designer"]')?.textContent).toContain('Report designer');
    expect(page.querySelector('.user')?.textContent).toContain('author.ar');
  });
});
