## 🇷🇺 Версия 0.3.1 — исправления и удобство

Обновление для Windows x64 и ARM64. **455 Windows-тестов прошли на каждой
архитектуре**; реальный офлайн-перевод и 70 вариантов WPF-компоновки также
проверены на обеих архитектурах.

### Что нового

- Локальный перевод английский ↔ русский без API-ключей, платных аккаунтов и Python.
  Модель каждого направления устанавливается отдельно через менеджер пакетов
  (около 243 МБ). После установки перевод не использует сеть.
- История последних 50 переводов: восстановление, копирование, удаление и очистка
  с подтверждением. Сохранение выключено по умолчанию.
- Отдельные звуки Pomodoro и напоминаний о воде, переключатели и предпрослушивание.
- Контакты поддержки в «О программе»: denic8871@gmail.com и Telegram @incdenis.

### Исправления

- В выпадающих списках показываются понятные названия вместо внутренних типов.
- Переводчик получил поля с текстом 14 DIP и отдельную панель действий;
  обязательный облачный ключ убран из интерфейса.
- Тонкие скроллбары настроек, фокус клавиатуры и векторные значки поддержки.
- Repeat переключает выключено → все → один → выключено, если плеер поддерживает
  команду; неподдерживаемые Shuffle/Repeat по-прежнему объясняются подсказкой.
- Возобновление Pomodoro после сна использует обработку завершения с оповещением.
- История уведомлений повторно проверяет разрешение, отличает отсутствие доступа
  от пустого списка и обновляется по событиям Windows.
- Завершение приложения дожидается отмены перевода и операций с пакетами;
  прерванные загрузки безопасно очищаются при следующей установке.

### Установка и обновление

- **Обычный ПК Intel/AMD:** скачайте `DynamicIslandForWindows-win-Setup.exe`
  из файлов этого релиза и запустите.
- **Windows ARM64:** используйте `DynamicIslandForWindows-win-arm64-Setup.exe`.
- **Обновление установленной версии:** Настройки → О программе → Проверить
  обновления. Дождитесь загрузки, выйдите через трей и запустите приложение снова.
- ZIP не нужен. Установщик сам проверяет Visual C++ runtime и при необходимости
  предлагает штатную установку Microsoft; могут потребоваться права администратора.
- Для перевода откройте менеджер языковых пакетов и скачайте нужное направление.
  Пользовательские задачи, записи и настройки сохраняются.

### Известные ограничения

- Живая WPF-проверка RU/EN при масштабах 70–130% и DPI 100–200%, слышимость
  сигналов и реальные медиаплееры требуют интерактивного Windows-сеанса.
- Офлайн-модель предназначена для коротких фрагментов: до 2000 знаков и 255
  входных токенов. Слишком длинный ввод отклоняется, не обрезается молча.
- Во время перевода модель расходует сотни мегабайт памяти; в Linux smoke-тесте
  пик процесса достиг 778 MiB. В простое модели не удерживаются.
- Чтение чужих уведомлений требует identity и разрешения Windows. Собственные
  напоминания Pomodoro/воды не зависят от этого разрешения. ОС может скрывать
  визуальные уведомления или отключать звук своими настройками.

## 🇬🇧 Version 0.3.1 — fixes and usability improvements

Windows x64 and ARM64 update. All 455 Windows tests passed on each architecture,
including real native offline translation and 70 WPF layout/render scenarios.

Local English ↔ Russian translation uses separately downloaded models (~243 MB
per direction) without API keys, Python or accounts. Translation stays offline;
optional local history is disabled by default and capped at 50 entries.

This release fixes selected ComboBox labels, translator layout, the repeat-mode
cycle, Pomodoro completion after sleep and notification access diagnostics. It
adds independent reminder sounds with preview, thin settings scrollbars and
support links. Shutdown drains translation/download work; interrupted downloads
are safely recovered on the next installation attempt.

Download the matching `.exe` installer from this release, or check for updates
in Settings → About, wait for the download, exit through the tray and relaunch.
The installer checks the official Visual C++ prerequisite; Python is not required.
Interactive Windows layout/DPI, audible playback and real-player acceptance
remain unverified; these automated checks do not replace them.
Models support short text (2000 characters / 255 input tokens), require substantial
memory during inference and are released between requests. See the dated
implementation report for actual build, test and native-execution evidence.
