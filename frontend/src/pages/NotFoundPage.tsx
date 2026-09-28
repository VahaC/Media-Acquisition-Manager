import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { Button } from '@/components/ui/button'

/** The answer for any address the route table does not know. Global card classes only. */
export function NotFoundPage() {
  const { t } = useTranslation('notFound')

  return (
    <section className="ui-card">
      <div className="ui-card-header">
        <h1 className="ui-card-title">{t('title')}</h1>
        <p className="ui-card-description">{t('description')}</p>
      </div>
      <div className="ui-card-footer">
        <Button asChild variant="outline" size="sm">
          <Link to="/">{t('goHome')}</Link>
        </Button>
      </div>
    </section>
  )
}
