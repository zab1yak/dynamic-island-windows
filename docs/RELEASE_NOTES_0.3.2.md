## 🇷🇺 Версия 0.3.2 — перевод, шрифты и прозрачность

### Изменения

- Встроенный статический шрифт Inter с настоящими начертаниями 400/500/600/700
  и поддержкой русского текста. Единые размеры и веса текста в острове и настройках.
- При раскрытии текст больше не масштабируется анимацией целого слоя.
  Пользовательский размер острова 70–130 % сохраняется.
- Полупрозрачный остров не темнеет после остановки анимации: устранена отложенная
  тень под прозрачным телом. Нейтральные карточки используют общую полупрозрачную
  палитру; чёрная и светло-серая темы и акцентные кнопки сохраняются.
- Новая ревизия офлайн-моделей английский ↔ русский: TC-big int8 с поиском beam 4
  и разбиением предложений. На 66 фразах независимое слепое AI-ревью выявило
  7 серьёзных ошибок против 18 у исходного варианта. Это небольшой диагностический
  набор, а не гарантия точности: идиомы и неоднозначные слова всё ещё ошибаются.
- В настройках видны источник, ревизия, размер и ход загрузки модели.
  Обновление языкового пакета выполняется отдельно; предыдущая модель сохраняется
  и доступна кнопка «Откатить». История переводов не удаляется.

### Установка и обновление

Обычный ПК Intel/AMD: `DynamicIslandForWindows-win-Setup.exe`.
Windows ARM64: `DynamicIslandForWindows-win-arm64-Setup.exe`.
Установленное приложение: Настройки → О программе → Проверить обновления.
ZIP не требуется.

Для новых моделей откройте настройки переводчика и нажмите «Обновить» у нужного
направления. Каждое направление занимает около 373 МБ; сохранённая старая версия
требует дополнительного места. Уже установленная старая модель продолжает работать
до явного обновления. После загрузки перевод полностью локальный, без API-ключей,
облачного аккаунта или Python. Новая модель требует больше памяти и времени, чем
предыдущая. После перевода процесс может сохранять сотни мегабайт памяти даже
после освобождения сессий модели; возврат к 150 МБ не гарантируется. Результаты
измерений и ограничения опубликованы в документации.

## 🇬🇧 Version 0.3.2 — translation, fonts and transparency

Embedded static Inter faces replace machine-dependent text fallback. Text layers
no longer scale during expansion. The translucent body keeps a stable tint after
settling, and neutral cards share a translucent palette.

A new optional TC-big int8 offline language-package revision uses beam search and
sentence segmentation. A blinded AI review of 66 diagnostic inputs found fewer
major errors, with documented regressions and higher resource use. This is not a
human evaluation or a general quality guarantee. Installed older models remain
usable; explicit updates preserve the old revision for rollback and keep history.

Use the x64 or ARM64 Setup.exe from this release, or Settings → About → Check for
updates. Update each language package separately in Translator settings. No ZIP,
cloud account, API key or Python is needed.
