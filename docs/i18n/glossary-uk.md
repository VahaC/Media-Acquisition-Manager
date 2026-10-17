# Ukrainian glossary

The words the Ukrainian web app uses (`frontend/src/locales/uk/`), settled before the translation (MAM-46) so
every namespace says the same thing the same way. A term follows what Jellyfin, Transmission, qBittorrent and Toloka
show in Ukrainian where they agree, because the owner reads those apps beside this one; where they disagree the
choice and its reason are in the Notes column. Vasyl chose four of the terms himself on 2026-10-17 (marked
*Vasyl*); the rest are the translator's, open to his corrections.

A new string uses these words. A change to a term here changes every string that uses it in the same commit.

## Style

- **Address the user as «ви», lowercase** («ваш», «вам»), as Jellyfin, Transmission and the Microsoft style guide do.
  Never «ти». Leave the pronoun out where the sentence reads without it.
- **Buttons and menu items are infinitives**: Зберегти, Скасувати, Видалити, Повторити, Відновити, Надіслати.
  **Instructions are imperatives**: Натисніть, Введіть, Виберіть (not «Оберіть»). Work under way is a verbal noun with
  an ellipsis: Збереження…, Видалення…, Надсилання….
- **Sentence case** everywhere, as in English: only the first word and proper names get a capital
  («Теки з медіафайлами», not «Теки З Медіафайлами»).
- **Quotes** are «…», nested „…“. Never "…" or “…” in Ukrainian text.
- **Apostrophe** is ’ (U+2019): ім’я, прив’язка, з’єднання, комп’ютер. Never the ASCII `'`.
- **Product and service names stay as they are**: Media Acquisition Manager, Jellyfin, TMDb, IMDb, TVDB, Toloka,
  Transmission, qBittorrent, Jackett, Prowlarr, Claude Code, Claude Desktop. Format and codec names too: HDR, SDR,
  Dolby Vision, HEVC, BDRip, WEB-DL, NZB, MCP.
- **Names in a sentence are never declined and never govern agreement.** Put a generic noun in front
  («у клієнті «{{client}}»», «інтеграцію «{{name}}» вимкнено») and use the impersonal «-но/-то» forms, so the
  sentence is right whatever the name's gender: «Інтеграцію вимкнено», not «{{name}} вимкнена». A name a person
  chose (an integration, a destination, a token, a library, a file, a title) goes in «…»; a user name follows
  «користувач» without quotes («користувача {{name}}», «задав користувач {{user}}»); an adapter's own name
  ({{service}}: Jellyfin, qBittorrent, TMDb…) follows «сервіс» or «клієнт» without quotes.
- **Pages, tabs and buttons named in a sentence use their exact Ukrainian label in «…»**: a page is
  «на сторінці «Інтеграції»» (never «у розділі», which is a tracker's section), a tab is «на вкладці «Вилучені
  копії»» (of another page: «на вкладці «Вилучені копії» сторінки «Дублікати»»), a button is «Натисніть
  «Повторити»», Jellyfin's own button is «кнопка «Ідентифікувати» в Jellyfin». A label that is itself a link
  (`<page>…</page>`, `<jobs>…</jobs>`) carries no quotes. A page reached from the user menu: «на сторінці «Місця
  збереження» з меню користувача». A path through settings uses arrows: «на сторінці «Інтеграції» → «{{client}}» →
  «Зіставлення шляхів»».
- **«наприклад» that opens an example takes no comma after it** (2019 orthography), inside brackets too:
  «клієнт завантажень (наприклад qBittorrent чи Transmission)», «— наприклад «Мультсеріали» для місця
  «Мультфільми»». Every search example uses the same title as TMDb's Ukrainian catalog: «Інтерстеллар (2014)».
- **Language names in the language picker are endonyms**: English, Українська. Elsewhere a language name comes from
  `Intl.DisplayNames` in the UI language; at the start of a label or an option its first letter is capitalised
  («Українська»), in running text it stays lowercase («доріжка українською»).
- **Numbers, dates and percentages** come from `lib/format.ts` (Intl `uk`): `7,94`, `1 234 567` (no-break space),
  `17.10.2026`, `5 жовт. 2026 р., 08:01`, `45,6%`, «учора», «3 дні тому». A translation never writes a number format
  of its own.

## Counted words

Ukrainian has four plural forms. Every counted key has `_one`, `_few`, `_many` and `_other`:

| Form | Counts | Example |
| --- | --- | --- |
| `_one` | 1, 21, 31, 101… (not 11) | 1 копія, 21 копія |
| `_few` | 2–4, 22–24… (not 12–14) | 2 копії |
| `_many` | 0, 5–20, 25–30, 11–14… | 0 копій, 5 копій, 11 копій |
| `_other` | fractions only (1,5) | 1,5 копії — genitive singular |

`_one` is **not** "exactly one": it is also 21, 31 and 101. A `_one` string that leaves the number out
(«Відновити стару копію») is only right when the count can't be more than one; otherwise it shows the number
(«Відновити {{value}} стару копію» → «21 стару копію»). The key-parity test in `src/locales/locales.test.ts`
requires all four forms.

| one | few | many | other |
| --- | --- | --- | --- |
| копія | копії | копій | копії |
| файл | файли | файлів | файлу |
| реліз | релізи | релізів | релізу |
| торент | торенти | торентів | торента |
| твір | твори | творів | твору |
| версія | версії | версій | версії |
| серія | серії | серій | серії |
| сезон | сезони | сезонів | сезону |
| елемент | елементи | елементів | елемента |
| заміна | заміни | замін | заміни |
| тека | теки | тек | теки |
| медіатека | медіатеки | медіатек | медіатеки |
| завдання | завдання | завдань | завдання |
| посилання | посилання | посилань | посилання |
| прив’язка | прив’язки | прив’язок | прив’язки |
| токен | токени | токенів | токена |
| пошук | пошуки | пошуків | пошуку |
| голос | голоси | голосів | голосу |
| користувач | користувачі | користувачів | користувача |
| день | дні | днів | дня |
| рік | роки | років | року |
| сідер | сідери | сідерів | сідера |
| пір | піри | пірів | піра |
| ім’я | імені | імен | імені |
| серіал | серіали | серіалів | серіалу |
| розділ | розділи | розділів | розділу |
| вилучення | вилучення | вилучень | вилучення |
| додатковий матеріал | додаткові матеріали | додаткових матеріалів | додаткового матеріалу |

## Units

| English | Ukrainian | Notes |
| --- | --- | --- |
| B, KiB, MiB, GiB, TiB | Б, КіБ, МіБ, ГіБ, ТіБ | As Transmission and qBittorrent in Ukrainian; 1024 steps (ADR 0001) |
| b/s, kb/s, Mb/s, Gb/s | біт/с, кбіт/с, Мбіт/с, Гбіт/с | As Jellyfin; CLDR's «Мб/с» reads as megabytes |
| min | хв | |
| {{count}} ch (audio channels) | {{count}} кан. | |
| {{value}}p (resolution) | {{value}}p | 1080p stays Latin |
| S01E05, S{{season}} | stays Latin | Release-name codes; «Сезон 2, серія 5» in words |
| {{decade}}s | {{decade}}-ті | «1990-ті» |

## Terms

### The app and accounts

| English | Ukrainian | Notes |
| --- | --- | --- |
| app, this app | застосунок | Not «програма», not «додаток» |
| installation (this installation) | застосунок; «цей застосунок» | «The installation owner» → «власник» |
| owner | власник | |
| member | учасник | |
| user, users | користувач, користувачі | |
| profile | профіль | |
| sign in (button) | Увійти | Page title «Вхід» |
| sign out | Вийти | |
| signed in as {{name}} | Ви ввійшли як {{name}} | |
| last sign-in | останній вхід | |
| user name | ім’я користувача | |
| password; current, new password | пароль; поточний, новий пароль | Genitive «пароля» |
| reset password | скинути пароль | |
| account | обліковий запис | |
| role | роль | |
| session | сеанс | |
| device | пристрій | |
| personal access token, token | персональний токен доступу, токен | |
| revoke | відкликати | |
| expires, expired | діє до, термін дії минув | The token table's column «Діє до» |
| Expires (a new token's lifetime); In 30 days; Never | Термін дії; 30 днів; Безстроково | «Діє до» can't take «30 днів»; a token that never expires shows «Безстроково» under «Діє до» too, while «Ніколи» stays for «Востаннє використано» and «Останній вхід» |
| last used | востаннє використано | |
| API key | ключ API | |
| credentials | облікові дані | |
| AI assistant | ШІ-асистент | Hyphenated, as «ШІ-модель» |
| script | скрипт | |
| Read only; Read and download (token access) | Лише читання; Читання й завантаження | |
| appearance; theme: Light, Dark, System | вигляд; тема: Світла, Темна, Як у системі | |
| language; As in the browser | мова; Як у браузері | Language names as endonyms: English, Українська |
| catalog language | мова каталогу | |
| settings | налаштування | |
| integration | інтеграція | |
| service (what an integration connects to) | сервіс | «сервіс {{service}}», «клієнт {{service}}» with the adapter's name unquoted; the home page's own «Статус сервісу» is this app's server |
| connect (a service, a library, a catalog); not connected | підключити; не підключено | Not «під’єднати»; peers «під’єднані» to a torrent are the network sense and stay |
| web interface, Web UI | вебінтерфейс | One word (2019 orthography) |
| enable, disable; disabled | увімкнути, вимкнути; вимкнено | |
| test (a connection) | перевірити (з’єднання) | |

### Library and media

| English | Ukrainian | Notes |
| --- | --- | --- |
| library | медіатека | *Vasyl*: as Jellyfin («Медіатеки»); the page and the menu item «Медіатека» |
| library item (Jellyfin's) | елемент медіатеки | |
| work (a film, a series or an episode as one thing) | твір | *Vasyl* |
| film, movie | фільм | |
| series, show | серіал | |
| season | сезон | |
| episode | серія | *Vasyl*: as TMDb and Toloka, not Jellyfin's «епізод» |
| specials | спецвипуски | Goes with «серія» |
| extras | додаткові матеріали | Jellyfin's «Додатково» can't be counted |
| version (Jellyfin's media version) | версія | |
| edition | редакція | Edition names keep their usual Ukrainian: Режисерська версія, Кінотеатральна версія, Розширена версія, Без цензури, Без купюр, Ремастер, Спеціальне видання, Остаточна версія, Міжнародна версія, Альтернативна версія, Робоча версія, Широкоекранна, Повноекранна; IMAX, Open Matte and Redux (the cut's own name) stay |
| collection | колекція | |
| director; creator | режисер; творець | |
| title; original title | назва; оригінальна назва | As TMDb in Ukrainian |
| overview, description | опис | |
| genre; year | жанр; рік | |
| id, identifiers | ID, ідентифікатори | «IMDb ID», «TMDb ID» |
| metadata | метадані | |
| sync, synced | синхронізація, синхронізувати, синхронізовано | |
| scan (folders) | сканувати, сканування | |
| inspect, inspection (ffprobe) | проаналізувати, аналіз | Kept apart from «перевірка» |
| refresh, Refresh all | оновити, Оновити все | |
| Identify (Jellyfin's) | Ідентифікувати | Jellyfin's own button, which the UI tells the owner to press |
| folder | тека | *Vasyl*: as Jellyfin and Transmission, not «папка» |
| library folder | тека медіатеки | |
| Media folders (the allowed roots) | Теки з медіафайлами | |
| files in no library | файли поза медіатеками | |
| path; path mappings | шлях; зіставлення шляхів | |
| file system; hardlink | файлова система; жорстке посилання | |
| read-write; read-only (mount) | для читання й запису; лише для читання | |
| mount (noun); mounted read-write | точка монтування; змонтовано для читання й запису | |
| on disk | на диску | |
| missing | відсутній | |
| ignore; stop ignoring | ігнорувати; не ігнорувати | |
| disc | диск | |
| resolution; codec; bit rate | роздільна здатність; кодек; бітрейт | |
| running time, runtime, duration | тривалість | |
| dynamic range | динамічний діапазон | |
| audio; subtitles; track | аудіо; субтитри; доріжка | |
| audio language | мова аудіо | |
| default (track); forced; SDH | за замовчуванням; примусові; SDH | «примусові» agrees with «субтитри». The same flag also marks a forced audio track (rare), where «примусова» would agree; that takes a separate audio key in code, not a different word here |
| quality; medium (BDRip, WEB-DL) | якість; джерело | «джерело відео» where «джерело» could be read as the release source, as on the release's Details dialog: «Джерело відео невідоме» |

### Releases and torrents

| English | Ukrainian | Notes |
| --- | --- | --- |
| release | реліз | As Toloka |
| release source | джерело релізів | |
| tracker; tracker section | трекер; розділ трекера | |
| indexer | індексатор | |
| torrent; .torrent file | торент; торент-файл | As Transmission; «Toloka» stays as its name |
| magnet link | magnet-посилання | |
| seeders; peers | сідери; піри | |
| seeding | роздача («на роздачі», «роздається») | |
| category | категорія | |
| published | додано | As Toloka's column |
| claimed (by a release) | заявлено | |
| match; possible match | збіг; можливий збіг | |
| matched by title and year | збіг за назвою та роком | |

### Downloads, jobs, replacement and quarantine

| English | Ukrainian | Notes |
| --- | --- | --- |
| download (button); a download | Завантажити; завантаження | |
| download client | клієнт завантажень | NZB clients exist, so not «торент-клієнт» |
| destination; Destinations (page) | місце збереження; Місця збереження | Not «тека»: a destination is a client's category (ADR 0010) |
| send | надіслати | |
| job; Jobs | завдання; Завдання | |
| Needs you (tab) | Чекають на вас | |
| in progress; finished; all | виконуються; завершені; усі | |
| Sending; Submitted; Downloading; Paused; Downloaded; Failed; Cancelled | Надсилається; Передано; Завантажується; Призупинено; Завантажено; Помилка; Скасовано | |
| needs linking | потрібно зв’язати | |
| progress | прогрес | |
| follow; Keep following | відстежувати; Продовжити відстеження | |
| skipped | пропущено | |
| replacement; Replace after verification | заміна; Замінити після перевірки | |
| copy; old copy; exact copies | копія; стара копія; точні копії | |
| verify, check | перевірити, перевірка | Not for inspection («аналіз») |
| pair, paired | зіставити, зіставлено | |
| quarantine; move into the quarantine | карантин; перемістити в карантин | «в/у карантин», «в теку карантину», not «до карантину» |
| Being moved into the quarantine…; Being put back…; Back where it was.; Being deleted permanently… (a copy's state) | Переміщується в карантин…; Повертається на місце…; Повернуто на місце.; Видаляється остаточно… | The same in Duplicates and Jobs; «Moving…» and other «-ing» summaries stay verbal nouns |
| restore | відновити | |
| remove (a copy, into the quarantine); Removed copies (tab) | вилучити; Вилучені копії | «Вилучити» keeps the file in the quarantine |
| a removal (counted) | вилучення | «2 заміни і 1 вилучення» |
| delete for good | видалити остаточно | Only this one erases a file |
| remove (an integration, a destination, a mapping, a link) | видалити | A setting, not a file |
| delete (a user) | видалити | |
| drop (a copy from a replacement) | виключити (із заміни) | |
| Keep both; Keep all | Залишити обидві; Залишити всі | «Залишити обидві» also when more than two copies are involved, as the English button says |
| needs review | потребує вашого рішення | |
| scheduled; stopped; lost | заплановано; зупинено; втрачено | |
| held by a torrent | утримує торент | |
| write over | перезаписати | |

### Duplicates

| English | Ukrainian | Notes |
| --- | --- | --- |
| duplicate, Duplicates | дублікат, Дублікати | |
| One file, two names | Один файл, два імені | |
| Same film, different files | Той самий фільм, різні файли | |
| Maybe the same film | Можливо, той самий фільм | |
| Kept as they are | Залишені як є | |
| confirm; dismiss (a match) | підтвердити; відхилити | |
| dismiss (close a banner) | закрити | |
| evidence; group | підстави; група | |
| identity (device · inode · links) | ідентифікатор файлу | Not «ідентичність», which means sameness |
| hash, hashing | хеш, обчислення хешу | |

### Recommendations and TMDb links

| English | Ukrainian | Notes |
| --- | --- | --- |
| recommendation, Recommendations | рекомендація, Рекомендації | |
| Movies; Series; Cartoons; Documentaries (tabs) | Фільми; Серіали; Мультфільми; Документальні | The TMDb link evidence badges use the nouns «анімація», «документалістика» |
| Not interested | Не цікавить | |
| Undo (Not interested, a duplicate decision, None fits); Undoing… | Повернути; Повернення… | Not «скасувати», which is Cancel; a sentence keeps «скасувати рішення» |
| Similar to what you have | Схоже на те, що у вас є | |
| Missing seasons | Відсутні сезони | |
| Top rated | Найвищий рейтинг | |
| rating; votes | рейтинг; голоси | |
| metadata catalog; computation; Computing (status); Recompute now | каталог метаданих; обчислення; Обчислюється; Перерахувати зараз | The status is a verb, as the job statuses («Завантажується») |
| link to TMDb; a TMDb link | прив’язати до TMDb; прив’язка | «посилання» is kept for URLs |
| set (a TMDb link or id, by hand); set by {{user}} on {{time}}; set by hand on {{time}} | задати; задав користувач {{user}} {{time}}; задано вручну {{time}} | Search and the Library card say it the same way; capitalised only where the line starts with it. Not «задано користувачем» |
| Kept in the app (a link Jellyfin didn't get) | Лише в застосунку; прив’язка залишається лише в застосунку | |
| suggestions; Confident (tab); None fits; Skip | пропозиції; Надійні; Жоден не підходить; Пропустити | One suggestion's badge agrees: «Надійна» |
| Review (tab); Review works without a TMDb id | Перегляд; Переглянути твори без TMDb ID | MAM plays nothing, so it doesn't read as watching; «Розгляд» is the fallback if it ever does |
| conflicts; unattached; not identified | конфлікти; без елемента; не ідентифіковано | |

### Common words

| English | Ukrainian | Notes |
| --- | --- | --- |
| Cancel; Close; Save; Add; Edit | Скасувати; Закрити; Зберегти; Додати; Редагувати | |
| Retry; Retrying…; Try again | Повторити; Повторна спроба…; Спробувати ще раз | |
| Copy; Copied; Done | Копіювати; Скопійовано; Готово | |
| Show details; Hide details; Details | Показати подробиці; Сховати подробиці; Подробиці | |
| Sort by; Filter; Clear filters; Clear search | Сортувати за; Фільтр; Скинути фільтри; Скинути пошук | |
| Page {{x}} of {{y}}; Per page | Сторінка {{x}} з {{y}}; На сторінці | |
| dialog | вікно | «вікно завантаження» (the Download dialog), «вікно прив’язки до TMDb» |
| in the background | у фоновому режимі | |
| Loading… | Зачекайте… | «Завантаження» means a download here |
| Loading the jobs… | Отримання завдань… | The same for any «Loading X» |
| Refresh failed, showing X as last loaded | Не вдалося оновити — показано X, отримані востаннє. | One form on every page, with a dash; the participle agrees with X («профіль, отриманий», «користувачів, отриманих», «те, що отримано»); «the last known status» → «Не вдалося оновити — показано останній відомий статус.» |
| status; unknown; never | статус; невідомо; ніколи | |
| default | за замовчуванням | |
| (optional) | (необов’язково) | |
| Saved. | Збережено. | |
| Are you sure? | Ви впевнені? | |
| This can’t be undone. | Цю дію не можна скасувати. | |
| Something went wrong | Щось пішло не так | |
| Reload the page | Перезавантажте сторінку | |
| Ask the owner; Ask the owner to check X; The owner can see why. | Зверніться до власника; Попросіть власника перевірити X; Причину бачить власник. | |
| Only the owner can X. | X може лише власник. | «Синхронізувати медіатеку може лише власник.» |
| X failed {{time}}: {{error}}; The last X failed. | X не вдалася {{time}}: {{error}}; Остання X завершилася помилкою. | The run's noun is the subject and the verb agrees with it: «Синхронізація не вдалася», «Сканування не вдалося», «Аналіз файлів не вдався», «Обчислення не вдалося», «Підготовка пропозицій не вдалася»; the time follows the verb |
