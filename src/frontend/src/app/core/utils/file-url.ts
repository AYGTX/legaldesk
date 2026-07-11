import { environment } from '../../../environments/environment';

/**
 * Resolves a stored file path (e.g. "avatars/{id}/x.png") to an absolute URL served by the API host.
 * Passes through values that are already absolute (http/https), data URLs, or blob URLs.
 */
export function resolveFileUrl(path?: string | null): string | null {
  if (!path) return null;
  if (/^(https?:|data:|blob:)/.test(path)) return path;
  const origin = environment.apiUrl.replace(/\/api\/?$/, '');
  return `${origin}/${path.replace(/^\//, '')}`;
}
