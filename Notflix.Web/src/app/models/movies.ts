export interface Movie {
  id: number; title: string; category: number | null; legacyRating: number | null;
  averageRating: number | null; ratingCount: number; myRating: number | null;
}
export interface MovieRequest { title: string; category: number; }
export const categories = [{ id: 1, name: 'Action' }, { id: 2, name: 'Comedy' },
  { id: 3, name: 'Drama' }, { id: 4, name: 'Sci-Fi' }, { id: 5, name: 'Romance' }];
export function categoryName(id: number | null): string { return categories.find(c => c.id === id)?.name ?? 'Unknown'; }
