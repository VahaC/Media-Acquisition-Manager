/**
 * MAM-5 — copies the text of a field to the clipboard; resolves whether it was copied.
 *
 * `navigator.clipboard` exists only in a secure context (HTTPS, or localhost), and MAM is often opened
 * at a plain-HTTP LAN address such as http://192.168.1.195:8080, where it is undefined. There the
 * field's text is selected and copied with `document.execCommand('copy')`, which browsers still honour
 * inside a click. The field itself is selected rather than a hidden one appended to the page: inside a
 * dialog the focus trap would pull the focus, and the selection with it, back out of a field outside
 * it. When both ways fail the text stays selected, so the caller's "press Ctrl+C" finds it ready.
 */
export async function copyFieldText(field: HTMLInputElement | HTMLTextAreaElement): Promise<boolean> {
  if (window.isSecureContext && navigator.clipboard) {
    try {
      await navigator.clipboard.writeText(field.value)
      return true
    } catch {
      /* refused, e.g. the page lost the focus or a permission said no; the selection below may still work */
    }
  }
  field.focus()
  field.select()
  try {
    return document.execCommand('copy')
  } catch {
    return false /* a browser that dropped execCommand throws instead of answering false */
  }
}
