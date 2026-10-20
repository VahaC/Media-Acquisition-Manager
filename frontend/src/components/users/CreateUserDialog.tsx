import { useState, type FormEvent } from 'react'
import { isAxiosError } from 'axios'
import { useTranslation } from 'react-i18next'
import { Banner } from '@/components/shared/Banner'
import { Field } from '@/components/shared/Field'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { useCreateUser } from '@/lib/queries'
import { getApiErrorMessage } from '@/lib/utils'
import '@/styles/settings-dialog.css' // .settings-dialog* — the scrolling body and the buttons under it

/**
 * Adds a user who signs in with a user name and password. The parent mounts it only while it is
 * open, so every opening starts with empty fields.
 *
 * The passwords must match before anything is sent — the owner would otherwise hand out a password
 * nobody knows — and every other rule is the server's, shown as its sentence.
 *
 * MAM-16 — each refusal shows where the answer says it belongs: the mismatch under Confirm password,
 * a 409 (only ever a taken name) under User name, and every other sentence in a banner above the
 * buttons. A user-name rule and a password rule share the 400 and say which field they are about
 * only in their sentence (the `code` beside it, MAM-47, isn't read here), so they stay in the banner:
 * copying the rules here to place them would give each rule a second wording, free to drift from the
 * server's.
 */
export function CreateUserDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation(['users', 'auth', 'common'])
  const create = useCreateUser()
  const [userName, setUserName] = useState('')
  const [password, setPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  // The passwords differed at the last submit: said under Confirm password until either is edited.
  const [mismatch, setMismatch] = useState(false)
  // The server's sentence for a taken name, under User name until the name is edited.
  const [nameTaken, setNameTaken] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const save = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setNameTaken(null)
    setError(null)
    if (password !== confirmPassword) {
      setMismatch(true)
      return
    }
    try {
      await create.mutateAsync({ userName: userName.trim(), password })
      onClose()
    } catch (e: unknown) {
      const sentence = getApiErrorMessage(e, t('create.failed'))
      if (isAxiosError(e) && e.response?.status === 409) setNameTaken(sentence)
      else setError(sentence)
    }
  }

  return (
    <Dialog open onOpenChange={(open) => { if (!open) onClose() }}>
      <DialogContent className="settings-dialog">
        <DialogHeader>
          <DialogTitle>{t('create.title')}</DialogTitle>
          <DialogDescription>{t('create.description')}</DialogDescription>
        </DialogHeader>
        {/* noValidate: the rules live on the server, and its sentence is the one to show. */}
        <form className="settings-dialog-form" noValidate onSubmit={(event) => void save(event)}>
          <div className="settings-dialog-body">
            <Field label={t('auth:userName')} error={nameTaken}>
              {(control) => (
                <Input
                  {...control}
                  value={userName}
                  autoComplete="off"
                  autoCapitalize="none"
                  spellCheck={false}
                  autoFocus
                  onChange={(event) => {
                    setUserName(event.target.value)
                    setNameTaken(null)
                  }}
                />
              )}
            </Field>
            <Field label={t('auth:password')}>
              {(control) => (
                <Input
                  {...control}
                  type="password"
                  value={password}
                  autoComplete="new-password"
                  onChange={(event) => {
                    setPassword(event.target.value)
                    setMismatch(false)
                  }}
                />
              )}
            </Field>
            <Field label={t('auth:confirmPassword')} error={mismatch ? t('auth:passwordsDiffer') : null}>
              {(control) => (
                <Input
                  {...control}
                  type="password"
                  value={confirmPassword}
                  autoComplete="new-password"
                  onChange={(event) => {
                    setConfirmPassword(event.target.value)
                    setMismatch(false)
                  }}
                />
              )}
            </Field>
          </div>
          {/* Outside the scrolling body, above the buttons; the fields keep what was typed. */}
          {error && <Banner>{error}</Banner>}
          {/* Primary first, Cancel last — the order every dialog in the app uses. */}
          <DialogFooter>
            <Button type="submit" disabled={create.isPending}>
              {create.isPending ? t('create.submitting') : t('create.submit')}
            </Button>
            <Button type="button" variant="outline" onClick={onClose}>{t('common:cancel')}</Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
