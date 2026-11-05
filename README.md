<div align="center">

<img src="docs/screenshots/logo.png" width="96" alt="Be">

# Beryll Browser

**Міцний і легкий, як берилій.**
Швидкий приватний браузер для старих і слабких ПК на Windows.

Захист від стеження у стилі DuckDuckGo · самоадаптація під залізо · сплячі вкладки · open source (MIT)

<table>
<tr>
<td align="center" width="50%">
<a href="https://github.com/ihorstefaniv/Beryllium-Browser/raw/main/download/Beryll-portable.zip">
  <img src="https://img.shields.io/badge/Windows%2010%20%2F%2011-%D0%97%D0%B0%D0%B2%D0%B0%D0%BD%D1%82%D0%B0%D0%B6%D0%B8%D1%82%D0%B8-20b296?style=for-the-badge&logo=windows&logoColor=white" alt="Завантажити для Windows 10 / 11" height="40">
</a><br>
<sub>Beryll 1.2 · ~0,4 МБ · автооновлення</sub>
</td>
<td align="center" width="50%">
<a href="https://github.com/ihorstefaniv/Beryllium-Browser/releases/download/legacy-v1.0/Beryll-Legacy-x86.zip">
  <img src="https://img.shields.io/badge/Windows%207%20%2F%208-%D0%97%D0%B0%D0%B2%D0%B0%D0%BD%D1%82%D0%B0%D0%B6%D0%B8%D1%82%D0%B8-106e68?style=for-the-badge&logo=windows&logoColor=white" alt="Завантажити для Windows 7 / 8" height="40">
</a><br>
<sub>Beryll Legacy 1.0 · 32-bit, ~147 МБ · <a href="https://github.com/ihorstefaniv/Beryllium-Browser/releases/download/legacy-v1.0/Beryll-Legacy-x64.zip">64-bit (4 ГБ+ RAM)</a></sub>
</td>
</tr>
</table>

</div>

---

![Нова вкладка](docs/screenshots/new-tab-dark.png)

## Чому «Берилій»
Берилій — один із найлегших металів і водночас жорсткіший за сталь. Так і Beryll: легкий для старого заліза
(Pentium / Celeron / Atom, 2–4 ГБ RAM, HDD) і міцний у захисті ваших даних.

1. **Не вантажить зайве**: вбудоване блокування трекерів і реклами.
2. **Не тримає в пам'яті непотрібне**: фонові вкладки «засинають» і повертають пам'ять Windows.
3. **Підлаштовується під ПК**: сам визначає RAM, тип диска, відеокарту й відеокодеки.

## Яку версію обрати
| | Windows 10 / 11 | Windows 7 / 8 / 8.1 |
|---|---|---|
| Версія | **Beryll** | **Beryll Legacy** |
| Рушій | Microsoft WebView2 (вже є у Windows) | [Supermium](https://github.com/win32ss/supermium): сучасний Chromium для старих Windows |
| Розмір | ~0,4 МБ | ~147 МБ (32-bit) / ~170 МБ (64-bit) |
| Інтерфейс | власний легкий інтерфейс Beryll | інтерфейс Chromium + нова вкладка Beryll |
| Оновлення | автоматично | поки вручну |

Чому для Windows 7 окрема версія: Microsoft зупинила WebView2 і Edge на Windows 7/8 на версії 109 (2023),
тому там ламаються сучасні сайти, скрипти й відеоплеєри. Beryll Legacy приносить сучасний рушій з собою.
Подробиці й плани (зокрема XP) у [docs/LEGACY-OS.md](docs/LEGACY-OS.md).

## Як запустити

**Windows 10 / 11.** Розпакуйте архів і запустіть `Beryll.exe`. У вікні привітання залиште галочку
«Встановити на ПК»: Beryll з'явиться в меню «Пуск» і на робочому столі (права адміністратора не потрібні).
Далі він оновлюється сам. Якщо Windows не має компонента WebView2, Beryll запропонує його завантажити.
Portable-режим: покладіть поруч із `Beryll.exe` порожній файл `portable`.

**Windows 7 / 8.** Розпакуйте архів повністю (наприклад, у `C:\Beryll` або на флешку) і запустіть `Beryll.exe`.
Нічого встановлювати не треба: .NET 3.5 уже є у Windows 7. Усе лежить у папці програми.
- **32-bit** для будь-якої Windows 7/8 і ПК з 2–3 ГБ пам'яті (нетбуки, Atom): їсть найменше.
- **64-bit** для 64-бітної Windows з 4 ГБ пам'яті й більше.

## Можливості

### Захист у стилі DuckDuckGo
| | |
|---|---|
| 🛡 **Блокування трекерів і реклами** | вбудоване (Win10/11) або uBlock Origin (Legacy) |
| 🔒 **HTTPS спершу** | `http://` відкривається як `https://` |
| 🔗 **Чисті посилання** | прибирає `utm_*`, `fbclid`, `gclid` та інші мітки стеження |
| ✋ **Global Privacy Control** | сайтам іде сигнал «не продавати мої дані» (`Sec-GPC: 1`) |
| 🍪 **Cookie-банери** | автоматично «Відхилити» / «Лише необхідні» |
| 🔥 **Кнопка «Вогонь»** | закрити вкладки й стерти дані одним кліком, вибрані сайти лишаються залогіненими |
| 🔎 **DuckDuckGo** | пошук за замовчуванням, без акаунтів і телеметрії |

<sub>Повний набір є у версії для Windows 10 / 11. У Legacy 1.0 поки працюють uBlock Origin, GPC, Do Not Track, DuckDuckGo
і блокування сторонніх cookies. Решта з'явиться разом із власним інтерфейсом Beryll.</sub>

<img src="docs/screenshots/privacy-panel.png" width="360" alt="Панель захисту">

### Швидкість на слабкому залізі
- Профіль ПК (≤ 2 / ≤ 4 / ≤ 8 ГБ / потужний) визначає, скільки вкладок тримати живими.
- Фонові вкладки засинають. Не засинають закріплені, вкладки зі звуком і з незбереженим текстом у формах.
  Замір: 4 вкладки займали 720 МБ, після сну фонових — 490 МБ.
- **Легке відео**: якщо відеокарта не декодує VP9/AV1, YouTube отримує H.264, і процесор не захлинається.
- У Legacy на ПК з 2 ГБ вмикається економний режим Chromium.

### Як у Chrome, тільки легше
Вкладки в заголовку, адресний рядок з підказками, панель закладок, імпорт з Chrome / Edge / Opera, історія,
відновлення сесії, друк і PDF, завантаження, пошук на сторінці, паролі, запит дозволів для камери й мікрофона,
світла й темна теми.

**Гарячі клавіші:** `Ctrl+T` / `Ctrl+W` вкладка · `Ctrl+Shift+T` відновити · `Ctrl+L` адреса · `Ctrl+D` закладка ·
`Ctrl+H` історія · `Ctrl+J` завантаження · `Ctrl+F` пошук · `Ctrl+Shift+Del` вогонь · `F11` повний екран

## Безпека
- **Оновлення** (Win10/11) підписані RSA-3072 / SHA-256. Підробленого чи зміненого архіву Beryll не встановить.
- **Legacy** збирається на GitHub Actions з цього коду. Рушій і uBlock завантажуються з офіційних релізів
  з перевіркою SHA-256.
- **Паролі** зашифровані через захист Windows (DPAPI) і прив'язані до вашого облікового запису Windows.
- Без драйверів, служб, автозапуску й прав адміністратора.
- SmartScreen може попередити «Windows захистила ваш ПК», бо exe поки без цифрового підпису: «Докладніше» →
  «Виконати все одно». Підпис коду через SignPath Foundation в процесі, див. [docs/SIGNING.md](docs/SIGNING.md).

**Політика підпису коду.** Підписуються лише файли, зібрані з цього репозиторію на GitHub Actions.
Автор, рецензент і затвердження кожного підпису: [Ihor Stefaniv](https://github.com/ihorstefaniv).

## Приватність
Без акаунтів, телеметрії й аналітики. Історія, закладки й налаштування лежать лише на вашому ПК.
Сам Beryll звертається в мережу лише для перевірки оновлень (раз на добу, вимикається) і за значками сайтів,
які є у ваших закладках.

## Обмеження
- Апаратні токени КЕП, яким потрібне розширення браузера, можуть не працювати у версії для Win10/11
  (файлові ключі й Дія.Підпис працюють).
- Немає синхронізації й перекладу сторінок.

## Збірка з коду
```
powershell -ExecutionPolicy Bypass -File build.ps1                  # Beryll для Windows 10 / 11
powershell -ExecutionPolicy Bypass -File legacy\build-legacy.ps1    # Beryll Legacy x86 + x64
```
Нічого встановлювати не треба: використовується компілятор C#, що вже є в Windows.
Історія розробки: [docs/DEVLOG.md](docs/DEVLOG.md).

## Ліцензія
MIT, див. [LICENSE](LICENSE). Microsoft WebView2 SDK — за ліцензією Microsoft, Supermium — BSD, uBlock Origin — GPLv3.

<div align="center"><sub>Ihor Stefaniv with AI Claude</sub></div>
