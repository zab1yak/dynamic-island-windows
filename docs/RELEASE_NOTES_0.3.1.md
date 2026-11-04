## 🇷🇺 Версия 0.3.1 — исправления и удобство

**Проект описания для проверочной сборки. Стабильный релиз ещё не опубликован.**

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

Проверочные x64/ARM64 пакеты формирует workflow **Candidate packages**.
Они не публикуются в стабильный канал автообновления. Установленная 0.3.0 не
обновляется этим описанием; установщики и feeds 0.3.1 появятся только после
выполнения условий выпуска. Пользовательские задачи, записи и настройки сохраняются.

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

**Draft candidate notes; a stable release has not been published.**

Local English ↔ Russian translation uses separately downloaded models (~243 MB
per direction) without API keys, Python or accounts. Translation stays offline;
optional local history is disabled by default and capped at 50 entries.

The candidate fixes selected ComboBox labels, translator layout, the repeat-mode
cycle, Pomodoro completion after sleep and notification access diagnostics. It
adds independent reminder sounds with preview, thin settings scrollbars and
support links. Shutdown drains translation/download work; interrupted downloads
are safely recovered on the next installation attempt.

Candidate x64/ARM64 packages are separate from stable auto-updates. Interactive
Windows layout/DPI, audible playback and real-player acceptance remain necessary.
Models support short text (2000 characters / 255 input tokens), require substantial
memory during inference and are released between requests. See the dated
implementation report for actual build, test and native-execution evidence.
