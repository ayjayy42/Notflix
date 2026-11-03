import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { LoginpageComponent } from './loginpage.component';
import { AuthService } from '../../services/auth.service';

describe('LoginpageComponent', () => {
  const auth = { login: vi.fn(), refreshSession: vi.fn() };
  const router = { navigateByUrl: vi.fn() };
  beforeEach(() => {
    vi.clearAllMocks();
    TestBed.configureTestingModule({ imports: [LoginpageComponent], providers: [{ provide: AuthService, useValue: auth }, { provide: Router, useValue: router }, { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({}) } } }] });
  });
  it('requires both credentials before making a login request', () => {
    const fixture = TestBed.createComponent(LoginpageComponent); fixture.componentInstance.login(); fixture.detectChanges();
    expect(auth.login).not.toHaveBeenCalled(); expect(fixture.nativeElement.textContent).toContain('Enter your username and password.');
  });
  it('disables duplicate login until completion and navigates after success', () => {
    const pending = new Subject<{ id: number; userName: string }>(); auth.login.mockReturnValue(pending);
    const fixture = TestBed.createComponent(LoginpageComponent); const page = fixture.componentInstance;
    page.form.setValue({ userName: 'tester', password: 'password' }); page.login(); page.login(); fixture.detectChanges();
    expect(auth.login).toHaveBeenCalledOnce(); expect(fixture.nativeElement.querySelector('button[type="submit"]').disabled).toBe(true);
    pending.next({ id: 7, userName: 'tester' }); pending.complete();
    expect(router.navigateByUrl).toHaveBeenCalledWith('/home'); expect(page.busy$.value).toBe(false);
  });
  it('shows a generic wrong-credentials message and allows another attempt', () => {
    auth.login.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 401 })));
    const fixture = TestBed.createComponent(LoginpageComponent); const page = fixture.componentInstance;
    page.form.setValue({ userName: 'tester', password: 'wrong' }); page.login(); fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain('Invalid username or password.'); expect(page.busy$.value).toBe(false);
  });
});
