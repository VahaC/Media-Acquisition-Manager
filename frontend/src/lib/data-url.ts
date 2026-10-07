/**
 * MAM-33 — a fetched picture as a `data:` URL, the only image source besides the app's own the CSP
 * allows (`img-src 'self' data:`). Anything but an image is refused, so only a picture reaches an `<img>`.
 *
 * MAM-35 — moved out of the recommendations' queries when the Library's posters took it too.
 */
export function dataUrlOf(blob: Blob): Promise<string> {
  if (!blob.type.startsWith('image/')) return Promise.reject(new Error(`Not an image: ${blob.type}`))
  return new Promise((resolve, reject) => {
    const reader = new FileReader()
    reader.onload = () => resolve(String(reader.result))
    reader.onerror = () => reject(reader.error ?? new Error('The poster could not be read.'))
    reader.readAsDataURL(blob)
  })
}
