import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Greeting } from './greeting.model';
import { GreetingsPage } from './greetings-page';

describe('GreetingsPage', () => {
  let fixture: ComponentFixture<GreetingsPage>;
  let http: HttpTestingController;

  const seeded: Greeting = { id: 1, message: 'Hello from SQL Server', createdAtUtc: '2026-10-12T00:00:00Z' };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [GreetingsPage],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(GreetingsPage);
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();

    http.expectOne('/api/system/info').flush({
      service: 'ReportingTool.Api',
      version: '1.0.0+abc123',
      environment: 'Dev',
      serverTimeUtc: '2026-10-12T09:30:00Z',
    });
  });

  afterEach(() => http.verify());

  const page = () => fixture.nativeElement as HTMLElement;

  it('lists greetings from the API with their UTC time', async () => {
    http.expectOne('/api/greetings').flush([seeded]);
    await fixture.whenStable();

    const item = page().querySelector('.greetings li');
    expect(item?.textContent).toContain('Hello from SQL Server');
    expect(item?.querySelector('time')?.textContent).toContain('2026-10-12 00:00:00 UTC');
    expect(page().querySelector('footer')?.textContent).toContain('1.0.0+abc123 · Dev');
  });

  it('saves a new greeting and shows it first', async () => {
    http.expectOne('/api/greetings').flush([seeded]);
    await fixture.whenStable();

    const input = page().querySelector('input') as HTMLInputElement;
    input.value = 'Hi from the browser';
    input.dispatchEvent(new Event('input'));
    (page().querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));

    const post = http.expectOne({ method: 'POST', url: '/api/greetings' });
    expect(post.request.body).toEqual({ message: 'Hi from the browser' });
    post.flush({ id: 2, message: 'Hi from the browser', createdAtUtc: '2026-10-12T09:31:00Z' }, { status: 201, statusText: 'Created' });
    await fixture.whenStable();

    const items = page().querySelectorAll('.greetings li');
    expect(items.length).toBe(2);
    expect(items[0].textContent).toContain('Hi from the browser');
    expect(input.value).toBe('');
  });

  it('shows the field error and does not call the API for a blank message', async () => {
    http.expectOne('/api/greetings').flush([]);
    await fixture.whenStable();

    const input = page().querySelector('input') as HTMLInputElement;
    input.value = '   ';
    input.dispatchEvent(new Event('input'));
    (page().querySelector('form') as HTMLFormElement).dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    http.expectNone({ method: 'POST', url: '/api/greetings' });
    expect(input.getAttribute('aria-invalid')).toBe('true');
    expect(page().querySelector('.hint .error')?.textContent).toContain('Enter a message');
  });

  it('says so when the API cannot be reached', async () => {
    http.expectOne('/api/greetings').flush('down', { status: 503, statusText: 'Service Unavailable' });
    await fixture.whenStable();

    expect(page().querySelector('[role="alert"]')?.textContent).toContain('could not be reached');
  });
});
