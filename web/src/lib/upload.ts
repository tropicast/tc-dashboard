/** Files the media library accepts (#104): the server checks the stored file again. */
export const acceptedExtensions = ['.mp3', '.flac', '.ogg', '.oga', '.opus'];

/** Largest track the API accepts (TrackRules.MaxBytes). */
export const maxTrackBytes = 200 * 1024 * 1024;

export function isAccepted(fileName: string) {
  const dot = fileName.lastIndexOf('.');
  return dot > 0 && acceptedExtensions.includes(fileName.slice(dot).toLowerCase());
}

/**
 * PUTs a file to a presigned bucket URL with the headers it was signed for. XMLHttpRequest, not fetch: only it
 * reports upload progress. Rejects on any non-2xx answer or network error.
 */
export function putFile(
  url: string,
  file: Blob,
  headers: Record<string, string>,
  onProgress: (fraction: number) => void,
): Promise<void> {
  return new Promise((resolve, reject) => {
    const request = new XMLHttpRequest();
    request.open('PUT', url);
    for (const [name, value] of Object.entries(headers)) request.setRequestHeader(name, value);
    request.upload.onprogress = (event) => {
      if (event.lengthComputable && event.total > 0) onProgress(event.loaded / event.total);
    };
    request.onload = () =>
      request.status >= 200 && request.status < 300
        ? resolve()
        : reject(new Error(`Storage answered ${request.status}`));
    request.onerror = () => reject(new Error('Network error'));
    request.send(file);
  });
}
