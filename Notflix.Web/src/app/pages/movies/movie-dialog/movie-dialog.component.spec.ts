import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Router } from '@angular/router';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { Subject, of, throwError } from 'rxjs';
import { MovieDialogComponent } from './movie-dialog.component';
import { MovieApiService } from '../../../services/movies/movie-api.service';
import { AuthService } from '../../../services/auth.service';

const movie = { id: 7, title: 'Arrival', category: 4, legacyRating: null, averageRating: null, ratingCount: 0, myRating: null };
describe('MovieDialogComponent', () => {
  let ref: { close: ReturnType<typeof vi.fn>; disableClose: boolean };
  const api = { addMovie: vi.fn(), updateMovie: vi.fn() }; const auth = { clearSession: vi.fn() }; const router = { navigate: vi.fn() };
  beforeEach(() => {
    vi.clearAllMocks(); ref = { close: vi.fn(), disableClose: false };
    TestBed.configureTestingModule({ imports: [MovieDialogComponent], providers: [{ provide: MAT_DIALOG_DATA, useValue: {} }, { provide: MatDialogRef, useValue: ref }, { provide: MovieApiService, useValue: api }, { provide: AuthService, useValue: auth }, { provide: Router, useValue: router }] });
  });
  it('rejects whitespace titles and missing categories without sending', () => {
    const dialog = TestBed.createComponent(MovieDialogComponent).componentInstance;
    dialog.movieForm.setValue({ title: '   ', category: 0 }); dialog.save(); expect(api.addMovie).not.toHaveBeenCalled(); expect(dialog.movieForm.touched).toBe(true);
  });
  it('trims the create request, prevents duplicate writes and closes only after success', () => {
    const pending = new Subject<typeof movie>(); api.addMovie.mockReturnValue(pending);
    const dialog = TestBed.createComponent(MovieDialogComponent).componentInstance;
    dialog.movieForm.setValue({ title: '  Arrival  ', category: 4 }); dialog.save(); dialog.save(); dialog.onCancel();
    expect(api.addMovie).toHaveBeenCalledOnce(); expect(api.addMovie).toHaveBeenCalledWith({ title: 'Arrival', category: 4 }); expect(ref.disableClose).toBe(true); expect(ref.close).not.toHaveBeenCalled();
    pending.next(movie); pending.complete(); expect(ref.close).toHaveBeenCalledWith(movie); expect(ref.disableClose).toBe(false); expect(dialog.saving$.value).toBe(false);
  });
  it('edits using the existing movie ID and keeps the dialog open on server failure', () => {
    TestBed.overrideProvider(MAT_DIALOG_DATA, { useValue: { movie } }); api.updateMovie.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 503 })));
    const dialog = TestBed.createComponent(MovieDialogComponent).componentInstance; dialog.save();
    expect(api.updateMovie).toHaveBeenCalledWith(7, { title: 'Arrival', category: 4 }); expect(dialog.error$.value).toContain('Movie was not saved'); expect(ref.close).not.toHaveBeenCalled(); expect(ref.disableClose).toBe(false);
  });
  it('closes the dialog and clears the session on unauthorized save', () => {
    api.addMovie.mockReturnValue(throwError(() => new HttpErrorResponse({ status: 401 })));
    const dialog = TestBed.createComponent(MovieDialogComponent).componentInstance; dialog.movieForm.setValue({ title: 'Arrival', category: 4 }); dialog.save();
    expect(auth.clearSession).toHaveBeenCalledOnce(); expect(ref.close).toHaveBeenCalled(); expect(router.navigate).toHaveBeenCalledWith(['/'], { queryParams: { expired: '1' } });
  });
});
