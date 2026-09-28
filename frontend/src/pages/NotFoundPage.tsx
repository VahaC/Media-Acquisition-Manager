import { Link } from 'react-router-dom'
import { Button } from '@/components/ui/button'

/** The answer for any address the route table does not know. Global card classes only. */
export function NotFoundPage() {
  return (
    <section className="ui-card">
      <div className="ui-card-header">
        <h1 className="ui-card-title">Page not found</h1>
        <p className="ui-card-description">There is no page at this address.</p>
      </div>
      <div className="ui-card-footer">
        <Button asChild variant="outline" size="sm">
          <Link to="/">Go to the home page</Link>
        </Button>
      </div>
    </section>
  )
}
