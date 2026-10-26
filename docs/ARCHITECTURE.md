# Архитектура Dynamic Island for Windows

> Составлено агентом-архитектором ECC (`ecc:architect`) на задаче 0, принято как базовое.
> Отклонения по ходу работы фиксируются в [DECISIONS.md](DECISIONS.md).

## 1. Цели и бюджеты
- Анимации 60 fps. В простое: CPU < 1 %, RAM < 150 MB, нет таймеров чаще 1 Гц
  (часы — разовый таймер до границы минуты).
- Один процесс, одно окно острова, отдельное окно настроек, иконка в трее.
- Добавить модуль = новый проект + одна строка регистрации. Окно острова при этом не меняется.

## 2. Проекты и зависимости
| Проект | TFM | Что внутри |
|---|---|---|
| `DynamicIsland.Core` | `net10.0` (без WPF и WinRT) | контракты, машина состояний, арбитр live activity, математика формы и пружин, настройки, JSON-хранилище, `TimeProvider` |
| `DynamicIsland.Platform.Windows` | `net10.0-windows10.0.19041.0`, без WPF | Win32/WinRT-сервисы общего назначения: окна, мониторы, полноэкранный режим, батарея, камера/микрофон, хоткей, single instance, автозапуск, toast, миниатюры Shell, запуск приложений |
| `DynamicIsland.UI` | то же + `UseWPF` | дизайн-система: тема, иконки, `SpringEase`, `AnimationClock`, построитель `StreamGeometry`, контролы, локализация |
| `DynamicIsland.Modules.{Media,Pomodoro,Water,Shelf,Todo}` | то же + `UseWPF` | модуль: логика, ViewModels, Views (XAML), свои resx, свой узкий interop |
| `DynamicIsland.App` | `WinExe` | composition root (Host), окно острова, навигация, окно настроек, трей, Velopack |
| `tests/DynamicIsland.Tests` | `net10.0-windows…` | xUnit v3, `FakeTimeProvider`, архитектурные тесты |

Граф ссылок (стрелка = «ссылается на»):
```
App ──► Core, UI, Platform.Windows, Modules.*
Modules.* ──► Core, UI          (не друг на друга, не на App и не на Platform)
UI ──► Core
Platform.Windows ──► Core
Tests ──► Core, Platform.Windows, UI, Modules.*
```
Правила:
- Core собран под чистый `net10.0`, поэтому компилятор сам не пустит туда WPF и WinRT.
- Модули получают ОС-сервисы через интерфейсы Core, реализации регистрирует App.
- Interop, который нужен только одному модулю (GSMTC и пиковый измеритель у Media), лежит
  в папке `Interop/` этого модуля за `internal`-интерфейсом. Тесты подставляют фейк.
- Архитектурный тест проверяет: типы `*.ViewModels.*` и `*.Services.*` не ссылаются
  на WPF-сборки, модули не ссылаются друг на друга.

Структура модуля:
```
Modules.Media/
  MediaModule.cs                 // IIslandModule
  MediaRegistration.cs           // services.AddMediaModule()
  Services/   // чистая логика: PositionInterpolator, LastTrackStore, RestoreFlow, DominantColor
  Interop/    // internal: GsmtcSessionSource, AudioPeakMeter
  ViewModels/ // CommunityToolkit.Mvvm, без типов WPF (цвета через Core.ColorRgb)
  Views/      // MediaTemplates.xaml (DataTemplate по типу VM), *.xaml
  Resources/  // Strings.resx, Strings.ru.resx
```

## 3. Контракт модуля (Core)
```csharp
public interface IIslandModule
{
    string Id { get; }                 // "media"; ключ настроек и файлов
    string TitleKey { get; }           // ключ локализации
    string IconKey { get; }            // "music"
    int Order { get; }                 // порядок страниц по умолчанию
    bool IsEnabled { get; }            // из настроек, меняется в рантайме
    object? CompactViewModel { get; }
    object ExpandedViewModel { get; }
    object? SettingsViewModel { get; }
    ILiveActivitySource? LiveActivities { get; }
    bool AcceptsDrop => false;         // Shelf = true
    Task InitializeAsync(CancellationToken ct);
    Task ShutdownAsync();              // сохранить состояние, ≤ 2 с
}
```
View подключаются так: модуль в `AddXxxModule()` регистрирует `IIslandModule` (singleton) и
`ModuleResources(pack URI словаря с DataTemplate)`. На старте App вливает словари в
`Application.Resources.MergedDictionaries`. Окно показывает
`ContentPresenter Content="{Binding CurrentPage.ExpandedViewModel}"`, WPF находит шаблон по `DataType`.
App ничего не знает о внутренностях модулей. Не `Func<FrameworkElement>`: это тянет WPF в контракт Core
и ломает тестируемость VM.

## 4. Live activity и арбитраж
- Индикаторы приватности (камера, микрофон, запись экрана) — не activity, а точки-оверлей поверх
  любого состояния, всегда видны и в очереди не участвуют.
- `LiveActivityArbiter` (Core, чистый) собирает `Current` всех источников и выбирает:
  1) `IsAlert` (конец помодоро, напоминание о воде) — очередь FIFO, показ `Ttl` (по умолчанию 4 с);
  2) затем максимальный `Priority`: таймер ≤ 60 с до конца (90) > медиа играет (70) >
     таймер идёт (60) > медиа на паузе < 5 мин (20); при равенстве — более свежий `StartedAt`;
  3) вторая по приоритету activity идёт в слот `Secondary` (мини-индикатор справа).
- Гистерезис: смена основной не чаще раза в 1,5 с (кроме алертов).
- Выход арбитра: `ArbiterResult(Primary?, Secondary?)`, его потребляет машина состояний.

## 5. Машина состояний острова (Core, `IslandStateMachine`)
Состояния: `Hidden`, `Compact`, `LiveActivity`, `Expanded`, `Pinned`, `DropTarget`.
«Покой» = `Primary == null ? Compact : LiveActivity`.

| Вход | Переход |
|---|---|
| `PointerEntered` | Покой → `Expanded` через 150 мс (отмена при `PointerExited`) |
| `PointerExited` | `Expanded` → Покой через 450 мс (отмена при повторном входе) |
| `Clicked` (по фону) | `Expanded` → `Pinned`; `Pinned` → Покой |
| `HotkeyPressed` | любое → `Pinned` (в т.ч. из `Hidden`); `Pinned` → Покой |
| `FullscreenChanged(true/false)` | не-`Pinned` → `Hidden`; `Hidden` → Покой |
| `ArbiterChanged` | `Compact` ⇄ `LiveActivity` |
| `DragNear` / `DragEnter` | Покой/`Expanded` → `DropTarget` (если Shelf включён) |
| `DragLeave` / `Dropped` | → Покой / → `Expanded` на странице Shelf |
| `TextEditStarted/Ended` | флаг `EditLock`: держит `Expanded`, игнорирует `PointerExited` |

Задержки — через `TimeProvider`, поэтому таблица покрыта тестами с `FakeTimeProvider`.
Размеры: Compact 200×34, Live 300×34, Expanded 580×160, Drop 580×120.

## 6. Рендер и анимации
```
Window (AllowsTransparency=True, Background=Transparent, фикс. размер = max + плечи + запас)
 └ Grid
    ├ Path  IslandBody  Fill=Black, Data=StreamGeometry
    └ Grid  ContentHost Clip=та же геометрия
        ├ CompactLayer   ┐ фиксированные размеры, только Opacity
        ├ LiveLayer      ├ и RenderTransform (Scale/Translate),
        └ ExpandedLayer  ┘ без изменения Width/Height → нет Measure/Arrange в кадре
```
- `Spring` (Core): аналитическое/полунеявное решение затухающего осциллятора (stiffness, damping),
  метод `Step(dt)` чистый и тестируется.
- `AnimationClock` (UI) подписан на `CompositionTarget.Rendering` только пока хотя бы одна пружина
  не в покое, затем отписывается. dt ограничен 1/20 с.
- Каждый кадр: `IslandShapeMath.Build(w, h, r, s)` (Core) → сегменты → `StreamGeometry` (Freeze) →
  `Path.Data` и `Clip`. Верх слит с краем экрана, плечи — вогнутые дуги, нижние углы — squircle
  (кубические Безье с непрерывной кривизной).
- `SpringEase : EasingFunctionBase` (UI) — для вторичных Storyboard-анимаций (кнопки, иконки).
- Визуализатор: пиковый уровень `AudioMeterInformation.MasterPeakValue` (CoreAudio), опрос 30 Гц только
  если музыка играет и остров виден.
- Часы: разовый таймер до следующей минуты/секунды только когда видны секунды.

## 7. Окно и прозрачность для кликов
- Стили: `WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST`, `ShowActivated=false`, ставятся
  в `SourceInitialized`. Per-Monitor DPI v2 задаётся в `app.manifest`.
- Позиция: верх по центру `rcMonitor` выбранного монитора через `SetWindowPos` в физических пикселях.
  Пересчёт на `WM_DPICHANGED`, `WM_DISPLAYCHANGE`, `DisplaySettingsChanged` и после сна.

| Вариант | Плюсы | Минусы |
|---|---|---|
| **A. Per-pixel alpha layered (`AllowsTransparency`)** | ОС сама пропускает alpha=0, сглаженные края, без таймеров | кадр копируется в системную память; тень с alpha>0 ловит клики |
| B. `SetWindowRgn` | ОС режет точно | без сглаживания, регион каждый кадр, мерцание |
| C. Переключать `WS_EX_TRANSPARENT` по hit-test | любой рендер | нужен опрос курсора → бюджет простоя |

**Решение: A**, тень только символическая внутри формы. Проверяется в задаче 2.

- Горизонтальная прокрутка: `WM_MOUSEHWHEEL` через `HwndSource.AddHook` + Shift+колесо, порог 120,
  пауза между перелистываниями 350 мс.
- Ввод текста (Todo): на время ввода снимается `WS_EX_NOACTIVATE` и вызывается активация, на
  `LostKeyboardFocus`/Esc/сворачивании стиль возвращается.
- Полноэкранный режим: `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` → `SHQueryUserNotificationState`
  (`QUNS_BUSY`, `QUNS_RUNNING_D3D_FULL_SCREEN`, `QUNS_PRESENTATION_MODE`) или bounds активного окна
  ≥ `rcMonitor`, исключая рабочий стол (`Progman`, `WorkerW`).

## 8. Interop
- `Platform.Windows/Native/`: P/Invoke через `LibraryImport` (source-generated, без runtime-маршалинга).
- Делегаты WinEvent-хуков хранятся в полях (иначе GC их соберёт и процесс упадёт).
- WinRT (GSMTC, `Battery`, `PowerManager`) — через проекцию TFM, без доп. пакетов. `IAsyncOperation` →
  `.AsTask(ct)`, никаких `.Result`.
- Миниатюры: `IShellItemImageFactory` на STA, `HBITMAP` освобождается через `DeleteObject`.

## 9. Сервисы индикаторов (Platform)
- Батарея: `Battery.AggregateBattery.ReportUpdated` + `PowerManager.EnergySaverStatusChanged`.
  Зарядка — зелёный, < 20 % — красный, экономия — жёлтый, `NotPresent` → индикатор скрыт.
- Камера/микрофон: `HKCU\Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{webcam,microphone}`
  с подключами и `NonPackaged\*`. «Используется» = `LastUsedTimeStop == 0 && LastUsedTimeStart != 0`.
  Слежение: `RegNotifyChangeKeyValue(subtree)` + `RegisterWaitForSingleObject`, перевзвод, debounce 300 мс.
- Запись экрана: эвристика по процессам, по умолчанию выключена.

## 10. Потоки
- UI-поток — единственный, кто трогает VM и визуальные элементы.
- События WinRT, реестра и пайпа приходят в пул потоков; сервис публикует состояние через
  `IUiDispatcher.Post` (Core-абстракция, реализация на `Dispatcher`).
- Долгие операции — `async` с `CancellationToken`, токен модуля отменяется в `ShutdownAsync`.

## 11. Хранение данных
- Корень `%LOCALAPPDATA%\DynamicIsland\`: `settings.json`, `media-lasttrack.json`, `art\`,
  `pomodoro.json`, `water.json`, `todo.json`, `shelf\index.json`, `shelf\items\<guid>\<имя>`, `logs\`.
- `JsonStorageService` (Core): временный файл в той же папке → flush на диск → `File.Replace`;
  при первой записи `File.Move`. `SemaphoreSlim` на файл. Битый JSON переименовывается в `*.corrupt`.
- Настройки: `ISettingsService` (Core) с версией схемы, миграциями на JSON-дереве и событием `Changed`.

## 12. Логи и ошибки
- Serilog: `%LOCALAPPDATA%\DynamicIsland\logs\island-.log`, ротация по дням, 7 файлов, 10 МБ на файл.
  Названия треков и тексты задач в лог не пишутся (приватность).
- `DispatcherUnhandledException` (лог, `Handled=true`; > 5 за 10 с → сообщение и выход),
  `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`.
- Ошибка модуля изолируется: модуль помечается `Faulted` и скрывается, остров живёт.

## 13. Жизненный цикл
- `Main`: `VelopackApp.Build().Run()` → single instance (`Mutex`, при занятости — аргументы в
  named pipe с ACL текущего пользователя, выход) → Host (DI, Serilog) → `App`.
  Аргументы: `--background`, `--settings`, `--show`.
- Сон/пробуждение (`PowerModeChanged`): пересоздать GSMTC-менеджер, пересчитать таймеры по
  `EndsAtUtc`, батарею, монитор, сброс дня у Water. `SessionSwitch` — пауза анимаций и визуализатора.

## 14. Локализация, настройки, иконки
- resx (`Strings.resx` + `Strings.ru.resx`), `Loc`-синглтон с индексатором, язык меняется на лету.
- Окно настроек в стиле macOS System Settings: сайдбар + `ContentPresenter`.
- Иконки: Lucide (ISC) как `Geometry` через `IconProvider`; лицензия в `THIRD_PARTY_NOTICES.md`.

## 15. Тестирование
- Core: машина состояний, арбитр, пружина, геометрия, хранилище, расписание Pomodoro и Water
  на `FakeTimeProvider`.
- Модули: логика за интерфейсами с фейками; защита Shelf от обхода пути — набор вредных имён.
- Ручной чек-лист: `docs/QA_CHECKLIST.md`.
