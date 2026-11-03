import { AsyncPipe } from '@angular/common';
import { Component, DestroyRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { Router } from '@angular/router';
import { BehaviorSubject, finalize } from 'rxjs';
import { Movie, categories } from '../../../models/movies';
import { MovieApiService } from '../../../services/movies/movie-api.service';
import { AuthService, requestError } from '../../../services/auth.service';
export interface MovieDialogData { movie?: Movie; }
@Component({ selector: 'app-movie-dialog', standalone: true,
  imports: [AsyncPipe, ReactiveFormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule],
  templateUrl: './movie-dialog.component.html', styleUrls: ['./movie-dialog.component.scss'] })
export class MovieDialogComponent {
  readonly data = inject<MovieDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject(MatDialogRef<MovieDialogComponent>);
  private readonly api = inject(MovieApiService);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  readonly categories = categories;
  readonly isNew = !this.data.movie;
  readonly saving$ = new BehaviorSubject(false);
  readonly error$ = new BehaviorSubject('');
  readonly movieForm = inject(FormBuilder).nonNullable.group({
    title: [this.data.movie?.title ?? '', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(100)]],
    category: [this.data.movie?.category ?? 0, [Validators.min(1), Validators.max(5)]]
  });
  save(): void {
    if (this.saving$.value || this.movieForm.invalid) { this.movieForm.markAllAsTouched(); return; }
    this.saving$.next(true); this.error$.next(''); this.ref.disableClose = true;
    const request = { ...this.movieForm.getRawValue(), title: this.movieForm.controls.title.value.trim() };
    const result$ = this.data.movie ? this.api.updateMovie(this.data.movie.id, request) : this.api.addMovie(request);
    result$.pipe(finalize(() => { this.saving$.next(false); this.ref.disableClose = false; }), takeUntilDestroyed(this.destroyRef)).subscribe({
      next: movie => this.ref.close(movie),
      error: error => { if (error.status === 401) { this.auth.clearSession(); this.ref.close(); void this.router.navigate(['/'], { queryParams: { expired: '1' } }); }
        else this.error$.next(requestError(error, 'Movie was not saved. Please try again.')); }
    });
  }
  onCancel(): void { if (!this.saving$.value) this.ref.close(); }
}
