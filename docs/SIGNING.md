# Цифровий підпис Beryll (щоб SmartScreen і Defender не сварились)

## Два різні підписи — не плутати

| | Навіщо | Чим | Де ключ |
|---|---|---|---|
| **Підпис оновлень** (вже працює) | Beryll встановлює оновлення лише від вас | RSA-3072, `build.ps1` | `keys\update_private.xml` — лише у вас, офлайн-копія обов'язкова |
| **Authenticode** (цей документ) | Windows бачить «Видавець: …», SmartScreen перестає лякати | сертифікат підпису коду | у центру сертифікації / SignPath, не в нас |

Авторизаційний підпис не замінює підпис оновлень, а доповнює: SignPath підписує `Beryll.exe`,
потім `build.ps1 -SignedExe` пакує його в архів і підписує архів вашим ключем оновлень.

## Варіанти

| Варіант | Ціна | Для кого | Коментар |
|---|---|---|---|
| **SignPath Foundation** ✅ рекомендую | безкоштовно | open source (MIT підходить) | Сертифікат на ім'я SignPath Foundation, підпис лише зі збірки на GitHub Actions. Розглядають заявку вручну. |
| Certum Open Source Code Signing | ≈ 25–70 € / рік | фізична особа, з перевіркою документів | Сертифікат на ваше ім'я; ключ у хмарі SimplySign або на картці; підписуєте самі (`signtool`). |
| Azure Trusted Signing | ≈ 10 $ / міс | компанії й особи з окремих країн | Для фізосіб з України зараз недоступно. |
| EV-сертифікат | 300+ $ / рік | компанії | Надлишково для цього проєкту. |

Важливо: **жоден підпис не прибирає SmartScreen миттєво**. Він прив'язує репутацію до видавця:
після кількох сотень завантажень без скарг попередження зникає і вже не з'являється для нових версій.

## SignPath Foundation — покроково

Репозиторій уже підготовлений:
- `.github/workflows/build.yml` — збірка на сервері GitHub з відкритого коду (приватних ключів там немає;
  `build.ps1` без ключа бере вже закомічений публічний ключ і не чіпає `download\`);
- крок підпису SignPath вмикається сам, коли з'являться змінна `SIGNPATH_ORGANIZATION_ID` і секрет `SIGNPATH_API_TOKEN`;
- у README є розділи «Політика підпису коду» і «Приватність» — їх вимагає SignPath Foundation.

Що зробити вам:
1. Увімкніть **двофакторну автентифікацію** в GitHub (Settings → Password and authentication) — це вимога.
2. Перевірте, що збірка на GitHub зелена: вкладка **Actions** → `build`.
3. Подайте заявку: https://signpath.org/apply — проєкт `ihorstefaniv/Beryllium-Browser`, ліцензія MIT,
   опис «lightweight privacy browser for old Windows PCs». Чекати від кількох днів до кількох тижнів.
4. Після схвалення в кабінеті SignPath:
   - встановіть **GitHub App SignPath** на репозиторій (Trusted Build System → GitHub.com);
   - проєкт зі slug `Beryllium-Browser`, signing policy `release-signing`;
   - artifact configuration зі slug `initial`:
     ```xml
     <?xml version="1.0" encoding="utf-8"?>
     <artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
       <zip-file>
         <pe-file path="Beryll.exe">
           <authenticode-sign/>
         </pe-file>
       </zip-file>
     </artifact-configuration>
     ```
     (DLL WebView2 вже підписані Microsoft — їх не чіпаємо.)
5. У GitHub → Settings → Secrets and variables → Actions:
   - **Variables**: `SIGNPATH_ORGANIZATION_ID` = ID організації з SignPath;
   - **Secrets**: `SIGNPATH_API_TOKEN` = токен користувача CI з SignPath.
6. Розкоментуйте рядок про SignPath у README (розділ «Політика підпису коду»).

## Як випускати версію з підписом

1. Змінити версію в `src\Program.cs` (три атрибути + `Version`), описати зміни в `res\release-notes.txt`.
2. `git commit`, `git push`, потім тег: `git tag v1.2.0` → `git push origin v1.2.0`.
3. GitHub Actions збере й надішле `Beryll.exe` на підпис; у SignPath натисніть **Approve** (ручне підтвердження).
4. Завантажте артефакт **Beryll-signed** з вкладки Actions, розпакуйте.
5. Локально (тут лежить ключ оновлень):
   ```
   powershell -ExecutionPolicy Bypass -File build.ps1 -SignedExe C:\Downloads\Beryll-signed\Beryll.exe
   ```
   Скрипт перевірить, що підпис справжній, запакує саме цей exe і підпише архів для автооновлення.
6. `git add download && git commit && git push` — усі встановлені Beryll отримають оновлення протягом доби.

## Якщо Defender все ж позначить файл
Хибні спрацювання надсилати сюди: https://www.microsoft.com/wdsi/filesubmission
(«Software developer» → файл → «Incorrectly detected as malware»). Зазвичай знімають за 1–3 дні.
