import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Router, provideRouter } from '@angular/router';
import { BehaviorSubject, Subject, of, throwError } from 'rxjs';
import { AppComponent } from './app.component';
import { AuthService } from './services/auth.service';

describe('AppComponent', () => {
  let auth: { user$: BehaviorSubject<unknown>; logout: ReturnType<typeof vi.fn>; clearSession: ReturnType<typeof vi.fn> };
  let navigate: ReturnType<typeof vi.spyOn>;
  beforeEach(() => {
    vi.clearAllMocks(); auth = { user$: new BehaviorSubject<unknown>(null), logout: vi.fn(), clearSession: vi.fn() };
    TestBed.configureTestingModule({ imports: [AppComponent], providers: [provideRouter([]), { provide: AuthService, useValue: auth }] });
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  });
  it('shows the current session and its logout action', () => {
    const fixture = TestBed.createComponent(AppComponent); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('Log out');
    auth.user$.next({ id: 7, userName: 'tester' }); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('tester'); expect(fixture.nativeElement.textContent).toContain('Log out');
  });
  it('prevents duplicate logout while pending and returns to login after success', () => {
    const pending = new Subject<void>(); auth.logout.mockReturnValue(pending);
    const fixture = TestBed.createComponent(AppComponent); const app = fixture.componentInstance;
    app.logout(); app.logout(); expect(auth.logout).toHaveBeenCalledOnce(); expect(app.loggingOut$.value).toBe(true);
    pending.next(); pending.complete(); expect(navigate).toHaveBeenCalledWith(['/']); expect(app.loggingOut$.value).toBe(false);
  });
  it('shows a retryable logout failure and resets the pending state', () => {
    auth.logout.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 0 })));
    const fixture = TestBed.createComponent(AppComponent); fixture.componentInstance.logout(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('Cannot reach');
    expect(fixture.componentInstance.loggingOut$.value).toBe(false); expect(navigate).not.toHaveBeenCalled();
  });
});

