import { Monitor, Moon, Sun } from 'lucide-react'
import { useTranslation } from 'react-i18next'
import { SegmentedControl } from '@/components/shared/SegmentedControl'
import { useThemeStore } from '@/lib/theme-store'

/**
 * MAM-16 — Light, Dark or System, for this browser. The choice lives in the theme store, so every
 * switch on screen shows the same one and System goes on following the operating system once the
 * menu holding the switch has closed.
 */
export function ThemeSwitch({ showLabels = false }: { showLabels?: boolean }) {
  const { t } = useTranslation('common')
  const mode = useThemeStore((state) => state.mode)
  const setMode = useThemeStore((state) => state.setMode)

  return (
    <SegmentedControl
      label={t('theme.label')}
      options={[
        { value: 'light', label: t('theme.light'), icon: Sun },
        { value: 'dark', label: t('theme.dark'), icon: Moon },
        { value: 'system', label: t('theme.system'), icon: Monitor },
      ]}
      value={mode}
      onChange={setMode}
      showLabels={showLabels}
    />
  )
}
