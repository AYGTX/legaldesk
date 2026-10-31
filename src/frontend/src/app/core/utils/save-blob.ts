/** Hands a downloaded file to the browser under the given name. */
export function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = fileName;
  a.click();
  URL.revokeObjectURL(url);
}

/** A file name without the characters Windows and macOS refuse. */
export function safeFileName(name: string): string {
  return (name || 'contract').replace(/[\\/:*?"<>|]+/g, ' ').trim() || 'contract';
}
