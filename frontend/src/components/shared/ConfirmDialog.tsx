/* eslint-disable react-refresh/only-export-components -- useConfirm and ConfirmProvider share one private context, so they live in one module */
import { createContext, useCallback, useContext, useRef, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'

export interface ConfirmOptions {
  /** Heading. Defaults to "Are you sure?". */
  title?: string
  /** Body text / node explaining what will happen. */
  message: React.ReactNode
  /** Confirm button label. Defaults to "Confirm". */
  confirmLabel?: string
  /** Cancel button label. Defaults to "Cancel". */
  cancelLabel?: string
  /** Render the confirm button in the destructive (red) style. */
  destructive?: boolean
  /**
   * When set, the confirm button stays disabled until this exact text is typed in.
   *
   * For the confirmations where a mis-click costs something a click cannot undo — an outage, or
   * data that is gone.
   *
   * Matched case-insensitively and trimmed: the point is deliberate re-entry of the name, not a
   * typing exam.
   */
  requireTyped?: string
}

type ConfirmFn = (options: ConfirmOptions) => Promise<boolean>

const ConfirmContext = createContext<ConfirmFn | null>(null)

/**
 * Returns an imperative `confirm(options)` that resolves to `true`/`false` — a drop-in,
 * promise-based replacement for the native `window.confirm`, rendered as the app's own modal.
 * The project never uses native confirm/alert dialogs; always go through this.
 */
export function useConfirm(): ConfirmFn {
  const ctx = useContext(ConfirmContext)
  if (!ctx) throw new Error('useConfirm must be used within a <ConfirmProvider>.')
  return ctx
}

/** App-wide provider that hosts the single confirmation modal. Mount once near the root. */
export function ConfirmProvider({ children }: { children: React.ReactNode }) {
  const [options, setOptions] = useState<ConfirmOptions | null>(null)
  const [typed, setTyped] = useState('')
  const resolver = useRef<((value: boolean) => void) | null>(null)

  const confirm = useCallback<ConfirmFn>((opts) => {
    setOptions(opts)
    // Cleared here rather than on close: a dialog that opened still holding the last answer
    // would let a second, different confirmation be accepted without reading it.
    setTyped('')
    return new Promise<boolean>((resolve) => { resolver.current = resolve })
  }, [])

  const settle = (result: boolean) => {
    resolver.current?.(result)
    resolver.current = null
    setOptions(null)
  }

  const confirmable = options?.requireTyped === undefined
    || typed.trim().toLowerCase() === options.requireTyped.trim().toLowerCase()

  return (
    <ConfirmContext.Provider value={confirm}>
      {children}
      <Dialog open={options !== null} onOpenChange={(open) => { if (!open) settle(false) }}>
        {options && (
          <DialogContent className="confirm-dialog">
            <DialogHeader>
              <DialogTitle>{options.title ?? 'Are you sure?'}</DialogTitle>
              <DialogDescription asChild>
                <div>{options.message}</div>
              </DialogDescription>
            </DialogHeader>

            {options.requireTyped !== undefined && (
              <label className="confirm-dialog-typed">
                <span>
                  Type <code>{options.requireTyped}</code> to confirm
                </span>
                <Input
                  value={typed}
                  aria-label={`Type ${options.requireTyped} to confirm`}
                  autoComplete="off"
                  autoFocus
                  onChange={(event) => setTyped(event.target.value)}
                />
              </label>
            )}

            {/* Primary first, Cancel last — the order every dialog in the app uses. shadcn's own
                DialogFooter example puts Cancel first; it is the odd one out, not the rule. */}
            <DialogFooter>
              <Button
                variant={options.destructive ? 'destructive' : 'default'}
                disabled={!confirmable}
                onClick={() => settle(true)}
              >
                {options.confirmLabel ?? 'Confirm'}
              </Button>
              <Button variant="outline" onClick={() => settle(false)}>{options.cancelLabel ?? 'Cancel'}</Button>
            </DialogFooter>
          </DialogContent>
        )}
      </Dialog>
    </ConfirmContext.Provider>
  )
}
