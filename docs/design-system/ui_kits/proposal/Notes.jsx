/* Reviewer notes (outside the product UI): what changed and why, per screen. Ukrainian, for the owner. */
const NOTES = {
  search: ['Пошук — головна сторінка (так у nav-items.ts), тож форма зведена в один липкий рядок; IMDb і TMDb — одне поле (tt… = IMDb, цифри = TMDb).',
    'Ваша копія закріплена ліворуч і стає точкою порівняння — це цінність №2 зі специфікації (verified vs claimed).',
    'Релізи — щільна таблиця замість карток по 5–6 рядків: на екрані ~10 релізів замість 3. Розмір і сідери вирівняні праворуч.',
    'Чипи, що відрізняються від вибраної копії, підсвічені тінтом primary — без оцінки «краще/гірше», рішення за користувачем.',
    'Статус джерел — компактні бейджі в заголовку секції; помилка одного джерела не ховає інші.',
    'Details відкривається боковою панеллю з таблицею «claimed | verified» рядок у рядок; список лишається видимим.'],
  library: ['Панель Sources (≈180px) стала однорядковою смугою з тими самими фактами й діями.',
    'Роботи — таблиця з колонкою «Best copy»; файли й треки розгортаються в рядку, а не займають кожну картку повністю.',
    'Фільтр «Missing an id» — сегментований перемикач з кількістю, бо це часта перевірка.'],
  integrations: ['Налаштування винесено з меню користувача в бічну панель: 4 сторінки власника більше не сховані.',
    'Інтеграції згруповані за роллю (бібліотека → джерела → клієнт), що повторює конвеєр застосунку.',
    'Результат Test з’являється в рядку, який тестували, а не банером над таблицею.'],
  shell: ['Бічна панель 232px замість хедера: місце під майбутні Jobs і Duplicates (фази A–B) без переповнення.',
    'Статус сервісу й версія — внизу панелі (зараз лише на Home).',
    'Перемикач теми Light / Dark / System внизу панелі. Пише той самий ключ mam:theme, який уже читає скрипт у index.html, тож вибір зберігається без білого спалаху.',
    'Контент ширший (до 80rem) — Search і Library отримують дві колонки/таблиці.'],
};
function NotesPanel({ page }) {
  const { Button } = window.MAM_DS;
  const [open, setOpen] = React.useState(true);
  const list = [...(NOTES[page] || NOTES.integrations), ...NOTES.shell];
  if (!open) return <div style={{ position: 'fixed', right: 16, bottom: 16, zIndex: 60 }}><Button icon="MessageSquareText" onClick={() => setOpen(true)}>Що змінено</Button></div>;
  return (
    <div style={{ position: 'fixed', right: 16, bottom: 16, zIndex: 60, width: 340, maxHeight: '70vh', overflowY: 'auto', background: 'var(--popover)', border: '1px solid var(--border)', borderRadius: 'var(--radius-lg)', boxShadow: 'var(--shadow-lg)', padding: '0.875rem 1rem', display: 'grid', gap: '0.625rem' }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}><strong style={{ flex: 1 }}>Що змінено в layout</strong>
        <Button variant="ghost" size="icon" icon="X" title="Hide" onClick={() => setOpen(false)} /></div>
      <ol style={{ display: 'grid', gap: 8, listStyle: 'decimal', paddingLeft: '1.1rem' }}>{list.map((n, i) => <li key={i} style={{ textWrap: 'pretty' }}>{n}</li>)}</ol>
      <p style={{ fontSize: 12, color: 'var(--muted-foreground)' }}>Кольори, шрифти, радіуси й компоненти — без змін. <a href="../current/index.html">Поточна версія →</a></p>
    </div>
  );
}
if (window.__MAM_KIT) Object.assign(window, { NotesPanel });
