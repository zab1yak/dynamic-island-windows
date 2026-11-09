## 🇷🇺 Версия 0.3.3 — музыка и понятное обновление

- Возвращена прежняя композиция страницы «Музыка»: крупная обложка слева,
  название и исполнитель с прогрессом в центре, предыдущий/play-pause/следующий
  справа. Поддерживаемая перемотка, shuffle/repeat и переключение плееров сохранены.
- В «О программе» проверка обновлений показывает результат, установленную и
  доступную версии, реальный прогресс загрузки и понятные ошибки с повтором.
  Ручная проверка больше не скачивает обновление без отдельного нажатия.
- Готовое обновление устанавливается при полном выходе из приложения через трей
  или кнопкой «Выйти и установить». Автоматического перезапуска нет. Запуск
  установщика Velopack перенесён на фактический выход, чтобы его ожидание не
  истекало, пока приложение продолжает работать. Фоновая автопроверка сохранена.
- В portable/dev-сборке явно указано, что автоматическая установка недоступна;
  доступна ссылка на страницу релиза.

Для перехода с 0.3.2 можно закрыть приложение через трей и запустить Setup.exe
из этого релиза. Используйте x64 для обычного ПК Intel/AMD или ARM64 для Windows
на ARM. Пользовательские данные и настройки сохраняются. После установки
проверка обновлений доступна в Настройки → О программе.

## 🇬🇧 Version 0.3.3 — music layout and clear update feedback

- Restored the reference music composition: larger artwork on the left,
  title/artist and progress in the middle, transport controls on the right.
  Seeking, shuffle/repeat and session switching remain available when supported.
- About now shows update checking, actual versions, download progress, completion,
  and actionable errors. Manual checking offers a separate download action.
- Verified downloads install on full exit, including the explicit Exit and install
  action. Velopack's exit helper is launched only at actual shutdown, avoiding its
  timeout while the app stays open. Automatic background checks remain enabled.
- Portable/development builds explicitly offer the release page instead of
  pretending automatic installation is available.

To upgrade from 0.3.2, fully exit from the tray and run the x64 or ARM64 Setup.exe
from this release. User files and settings are preserved.
