import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { Movie, MovieRequest } from '../../models/movies';
@Injectable({ providedIn: 'root' })
export class MovieApiService {
  private readonly http = inject(HttpClient);
  getMovies(): Observable<Movie[]> { return this.http.get<Movie[]>('/api/movies'); }
  addMovie(movie: MovieRequest): Observable<Movie> { return this.http.post<Movie>('/api/movies', movie); }
  updateMovie(id: number, movie: MovieRequest): Observable<Movie> { return this.http.put<Movie>(`/api/movies/${id}`, movie); }
  rateMovie(id: number, rating: number): Observable<Movie> { return this.http.put<Movie>(`/api/movies/${id}/rating`, { rating }); }
  deleteMovie(id: number): Observable<void> { return this.http.delete<void>(`/api/movies/${id}`); }
}
