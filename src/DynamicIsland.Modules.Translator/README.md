# Offline translator / Офлайн-переводчик

Откройте «Переводчик», выберите направление и нажмите «Скачать». В коротком менеджере
пакетов показаны языки, точный размер, источник и лицензия. Подтвердите загрузку нужного
направления; обратное направление устанавливается отдельно. После установки перевод
работает без интернета, аккаунта и API-ключа. Кнопка отмены доступна во время загрузки
или перевода. Длинные предложения разделяйте на части: предел модели — 255 входных
токенов; дополнительный предел ввода — 2000 знаков. Превышение отклоняется, текст не
обрезается молча.

История выключена по умолчанию. Её можно включить в настройках или окне истории;
сохраняются только последние 50 переводов в локальном `translator-history.json`.
Запись атомарна, доступно восстановление, копирование, удаление записи и очистка с
повторным подтверждением в течение четырёх секунд. Отключение прекращает новые
записи, существующие удаляются отдельно. Тексты не отправляются в сеть и не пишутся
в журналы. Существующие ключи прежней облачной интеграции не читаются и не удаляются;
облачный адаптер сохранён для совместимости тестов, но не зарегистрирован в приложении.

The translator uses local ONNX/Marian inference and downloadable RU→EN / EN→RU model
packages. Windows users do not need Python, a separate runtime, accounts, or paid keys.
The dedicated HTTP client only downloads explicitly requested model files. Package
size and pinned SHA-256 are checked before atomic installation; models live separately
under the app data directory. Native inference sessions are scoped to each translation,
so no model remains resident between requests. See the model catalog and third-party
notices for pinned sources and licences.

Local history is opt-in and bounded to 50 entries. The existing atomic storage service
persists the preference and history together; failed writes do not update in-memory
saved state. Per-entry deletion and confirmed clearing are explicit user actions.
Shutdown cancels and drains translation, package and history operations before DI
services are disposed. The shared ComboBox fix and localized display names apply to
both dropdown and selected items.

Cross-compilation and automated logic tests do not validate interactive WPF focus,
clipboard availability, typography, scale/DPI layout, or Windows x64/ARM64 native
execution. Those require the Windows candidate verification and visual matrix.
