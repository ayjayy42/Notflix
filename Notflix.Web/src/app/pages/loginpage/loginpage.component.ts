import { AsyncPipe } from '@angular/common';
import { Component, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { BehaviorSubject, finalize } from 'rxjs';
import { AuthService, requestError } from '../../services/auth.service';
@Component({ selector: 'app-loginpage', standalone: true, imports: [ReactiveFormsModule, AsyncPipe],
  templateUrl: './loginpage.component.html', styleUrls: ['./loginpage.component.scss'] })
export class LoginpageComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly destroyRef = inject(DestroyRef);
  readonly form = inject(FormBuilder).nonNullable.group({ userName: ['', [Validators.required, Validators.maxLength(80)]], password: ['', [Validators.required, Validators.maxLength(256)]] });
  readonly busy$ = new BehaviorSubject(false);
  readonly error$ = new BehaviorSubject(this.route.snapshot.queryParamMap.has('connectionError') ? 'Could not check your session. Retry when the connection is available.' : this.route.snapshot.queryParamMap.has('expired') ? 'Your session expired. Please log in again.' : '');
  readonly connectionError = this.route.snapshot.queryParamMap.has('connectionError');
  login(): void {
    if (this.form.invalid || this.busy$.value) { this.form.markAllAsTouched(); return; }
    this.busy$.next(true); this.error$.next('');
    const { userName, password } = this.form.getRawValue();
    this.auth.login(userName, password).pipe(finalize(() => this.busy$.next(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => void this.router.navigateByUrl('/home'),
      error: error => this.error$.next(error.status === 401 ? 'Invalid username or password.' : requestError(error, 'Login failed. Please try again.'))
    });
  }
  retrySession(): void {
    this.busy$.next(true);
    this.auth.refreshSession().pipe(finalize(() => this.busy$.next(false)), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: user => { this.error$.next(''); if (user) void this.router.navigateByUrl('/home'); },
      error: error => this.error$.next(requestError(error, 'Could not check your session. Please retry.'))
    });
  }
}
