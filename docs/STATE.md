# Shamus 3D: состояние разработки (handoff)

Документ для продолжения работы на другой машине. Дата: 2026-11-16.
Репозиторий: <https://github.com/blalbabla/shamus-remake>, ветка `main`.

Это снимок текущего состояния, а не замена [DESIGN.md](DESIGN.md), [TECHNICAL.md](TECHNICAL.md) и
[MAZE_FORMAT.md](MAZE_FORMAT.md). Если меняешь механику или архитектуру, обновляй и их.

## 1. Что это

Бесплатный ремейк Shamus (Synapse, 1982): 128 комнат, 4 уровня, враги, ключи и замки, pod-комнаты,
Shadow, процедурные лабиринты. Вид сверху или изометрия, всё из 3D-примитивов, звук синтезируется кодом.
Стек: .NET 10, C#, Raylib-cs 8.1.0, xUnit.

Статус: играбельный MVP. Все базовые механики оригинала на месте, плюс процедурные лабиринты,
карта, рекорды, настройки, освещение с тенями.

## 2. Быстрый старт на новой машине

Нужны:

- Windows 10/11 (проект проверялся на Windows; Raylib-пакет тянет нативные библиотеки сам).
- .NET SDK 10.0.x (проверено на 10.0.302). Проверка: `dotnet --version`.
- GPU/драйвер с OpenGL 3.3 (шейдер освещения). Без него игра запустится без света (fallback).
- Интернет для `dotnet restore` (NuGet: Raylib-cs, xUnit).

```bash
git clone https://github.com/blalbabla/shamus-remake.git
cd shamus-remake
dotnet build          # решение Shamus.sln
dotnet test           # 61 тест ядра
dotnet run --project src/Shamus.Game
```

> Каталог называется `shamus-remake` после клонирования; в этом документе пути указаны от корня репозитория.

Полезные флаги:

| Флаг | Назначение |
|---|---|
| `--maze c64\|atari\|holmes\|cluseau\|marlowe\|bond\|random` | лабиринт |
| `--seed N` | зерно для `random` |
| `--room N` | сразу в комнату N, без титульного экрана |
| `--view top\|iso` | вид камеры |
| `--settings` | открыть экран настроек (для отладки/скриншотов) |
| `--winsize WxH` | стартовый размер окна (проверка полей 16:9) |
| `--shot файл.png` | сохранить кадр (8-й кадр) и выйти |
| `--zoom F`, `--look X,Y` | отладочный зум и точка обзора |
| `--mute` | без звука |
| `--debug` | `[` / `]` переключают комнаты |

Настройки пользователя и рекорды: `%APPDATA%\Shamus3D\settings.json` (на новой машине создаются заново).
Там же хранятся `Fullscreen`, громкость, выбранный лабиринт, сложность, God mode, вид камеры.

`assets/config.json` читается из исходного дерева (игра ищет каталог, где есть `assets` и `src`), поэтому
в dev-запуске правки конфига подхватываются без пересборки (F5 в игре). Для publish см. ограничение в §7.

## 3. Структура

```
src/Shamus.Core/                 логика без Raylib
  GameConfig.cs                  константы, JSON, пресеты сложности
  Simulation/GameWorld.cs        детерминированная симуляция (тик 60 Hz), события
  Simulation/Enemy.cs            враги
  Mazes/MazeData.cs              640 байт, 128 комнат
  Mazes/RoomGrid.cs              сетка комнаты 40x22 (Thick/Thin)
  Mazes/RoomWalls.cs             отрезки стен + столбики-генераторы
  Mazes/ProceduralMaze.cs        генератор
  Mazes/MazeValidator.cs         проверки структуры/проходимости
  Mazes/MapLayout.cs             карта по уровням
src/Shamus.Game/                 Raylib-фронтенд
  GameApp.cs                     окно, состояния, letterbox, меню, настройки
  Program.cs                     вход
  Rendering/SceneRenderer.cs     3D-сцена: пол, объекты, танк, враги, пули
  Rendering/RoomView.cs          пол/закрытые зоны, столбики, силовые поля, название комнаты
  Rendering/CameraRig.cs         ортокамера top/iso + кастомная 16:9 проекция
  Rendering/LightingShader.cs    Lambert + ambient + PCF-тени 5x5
  Rendering/ShadowMap.cs         depth-FBO 2048, ортокамера света
  Rendering/GlowMap.cs           эмиссия в 1/4-кадр, 2 блюра, аддитивная композиция
  Rendering/RoomMetrics.cs       единый масштаб высот (стены = 1.0 клетки)
  Rendering/AtariPalette.cs      палитра Atari
  Ui/                            Hud, Menu, SettingsMenu, Settings, MapView
  Audio/                         Dsp, ProceduralSfx, AmbientSynth, AudioManager
tools/Shamus.MazeExtract/        ATR -> .maze
tools/Shamus.RoomCompare/        сверка RoomGrid со скриншотами
tests/Shamus.Core.Tests/         xUnit, 61 тест
assets/                          mazes/*.maze, maps/*.map, config.json, fonts
docs/                            DESIGN.md, TECHNICAL.md, MAZE_FORMAT.md, этот файл
artifacts/                       исходники (ATR, PDF, скриншоты); в .gitignore, на новой машине отсутствует
```

## 4. Что реализовано

- **Мир:** 128 комнат, 4 уровня; chamber/corridor/pod; двери с ключами; границы комнат 0 и 127 запечатаны.
- **Симуляция:** детерминированный `GameWorld.Tick` (60 Hz), 3 типа врагов, пули, объекты, Shadow,
  pod-барьеры, новые круги; сложность и God mode через `GameConfig`.
- **Лабиринты:** 6 оригинальных `.maze` + процедурный генератор с валидатором; `.map`-раскладка карты.
- **Рендер:** ортокамера сверху/изометрия; letterbox 16:9 в окне любого размера и в fullscreen
  (рендер в родном разрешении окна/дисплея, MSAA 4x); стены — вертикальные полупрозрачные розовые
  силовые поля; столбики-генераторы; танк-игрок; направленный свет с тенями; свечение (glow) силовых
  полей, «шапок» генераторов, барьеров pod-комнат и пуль. Высоты стен, столбиков, барьеров, танка,
  врагов и предметов согласованы через `RoomMetrics`.
- **UI:** титульный экран (только меню), SETTINGS (Gameplay/Video/Audio), пауза, Game Over, карта,
  HUD, тосты, рекорды по лабиринтам.
- **Звук:** 18 процедурных эффектов (2–5 вариантов), живой эмбиент, отдельные громкости эффектов/музыки.
- **Инструменты/тесты:** извлечение карт из ATR, сверка комнат со скриншотами, 61 xUnit-тест.

## 5. Как устроен кадр (важно при правках рендера)

Порядок в `GameApp.Run`:

1. `BeginDrawing`, очистка чёрным.
2. `SceneRenderer.RenderShadow(world, alpha)` — depth-карта света в FBO 2048 (переключает FBO и
   сбрасывает матрицы, поэтому вызывается до настройки letterbox).
3. `SceneRenderer.RenderGlow(world, config)` — только эмиттеры (силовые поля, «шапки» генераторов,
   пули, барьеры pod, глаза Shadow) в FBO 320x180, затем два separable-блюра. Тоже переключает FBO,
   поэтому тоже до letterbox.
4. Расчёт viewport/scissor: кадр 1280x720 вписывается в окно с полями 16:9.
5. 2D-ортопроекция `0..1280x720` (UI рисуется в этих координатах и масштабируется в viewport).
6. `Draw`: сцена, HUD, меню.

`SceneRenderer.Draw`:

1. `Rig.BeginView()` — кастомная ортопроекция 16:9 (`Raymath.MatrixOrtho` + `MatrixLookAt`).
   `Raylib.BeginMode3D` не используется: он берёт аспект из окна и искажал бы сцену.
2. `LightingShader.Begin()` → `DrawWorld(...)` — пол, закрытые зоны, столбики, объекты, танк, враги,
   Shadow, пули (всё непрозрачное, освещается и отбрасывает тени).
3. `LightingShader.End()`.
4. Без света: название комнаты, pod-барьеры (аддитивные), силовые поля.
5. `Rig.EndView()`; `_glow.Composite()` — готовое свечение аддитивно поверх сцены (2D-проход в
   координатах 1280x720, поэтому строго после `EndView`).
6. 2D-подпись «?» для mystery.

`RenderShadow` рисует в карту глубины только «кастеров»: столбики, объекты, танк, врагов, пули.
Пол в карту не пишется, иначе он затенял бы сам себя. `RenderGlow` пишет в свой FBO только эмиттеры
(без теста глубины: сравнивать не с чем), блюр — 9-tap гаусс по горизонтали и вертикали.

Подводные камни, уже проверенные на практике:

- Перед `Rlgl.DisableDepthMask()` нужен `Rlgl.DrawRenderBatchActive()`. Иначе raylib сбрасывает
  накопленный батч уже без записи глубины, и полупрозрачное ложится поверх столбиков.
- Тень-текстура привязывается через `Raylib.SetShaderValueTexture(...)`: raylib трактует id текстуры
  как юнит сэмплера. Ручная привязка к юниту 1 через `Rlgl` не сработала.
- `LightViewProjection = Matrix4x4.Multiply(Rlgl.GetMatrixProjection(), Rlgl.GetMatrixModelview())` —
  порядок аргументов именно такой (raylib хранит матрицы column-vector стилем).
- Танк рисуется через `Rlgl.PushMatrix/Translatef/Rotatef`. rlgl трансформирует вершины на CPU,
  поэтому `fragPos` в шейдере остаётся мировым. Если заменить на шейдерный model-матрикс, тени на
  танке поедут.
- MSAA 4x включён на окне; FBO тени и FBO свечения — без MSAA.
- Свечение (`GlowMap`) растеризуется в FBO 320x180 (1/4 кадра). Это отдельный проход без геометрии,
  поэтому сравнивать глубину не с чем: эмиттер за стеной всё равно светится. Для bloom это приемлемо,
  но не пытайтесь «починить» это тестом глубины в этом FBO — нужен depth-буфер основной сцены.
- `_glow.Composite()` вызывается только после `Rig.EndView()`: это 2D-квад в координатах 1280x720,
  в 3D-проекции он уедет. И только до HUD — интерфейс не должен светиться.

## 6. Что сделано в последней сессии

- Введён `RoomMetrics`: один масштаб для всего, что стоит в комнате (1 единица = 1 клетка). Стена
  (силовое поле) = 1.0, столбик 0.9x1.0, pod-барьер = высота стены, танк 0.73, враги не выше 0.96,
  предметы не выше 0.96, Shadow 1.45 — выше стен намеренно. Всё «прижато» к полу: раньше враги
  (до 1.8) и предметы (до 1.4) торчали выше стен (0.8), а pod-барьеры были 1.6.
- Добавлено свечение (`GlowMap`): эмиссия в FBO 1/4 кадра, два separable-блюра (9-tap гаусс),
  аддитивная композиция поверх сцены. Светятся силовые поля и их кромки, «шапки» генераторов,
  барьеры pod-комнат, пули, куб mystery и глаза Shadow (`Strength = 0.55`).
- Тени: карта 1024 → 2048, PCF 3x3 → 5x5 с радиальными весами, bias уменьшен.
- Танк переделан под читаемый силуэт: гусеницы с траками, корпус с верхней плитой, кольцо башни,
  небольшая полусфера и ствол вперёд (раньше полусфера была крупнее корпуса).
- Корпус танка опущен на половину высоты гусеницы (`RoomMetrics.TrackHeight / 2` = 0.17): гусеницы
  теперь обрамляют корпус, а не несут его сверху, высота танка 0.73. Проверено по близкому скриншоту.
- Поворот танка сглажен: `SceneRenderer.UpdateTank` экспоненциально доводит курс до `FaceX/FaceY`
  (tau 0.045 с, разворот 180° ≈ 0.23 с; 0 → 27.9 → 47.1 → 60.4 → 69.5 → 75.9 → 80.3 → 83.3 → 85.4°
  по кадрам при 60 fps). Симуляция по-прежнему меняет `FaceX/FaceY` мгновенно — выстрел летит в
  нажатом направлении сразу. Курс обновляется раз в кадр до обоих проходов рендера, `ResetTank()`
  вызывается на старте игры.
- Глаза Shadow вынесены на поверхность головы (были утоплены внутрь сферы и не рисовались видимо).
- `assets/config.json` добавлен в `<Content>` и копируется в выходной каталог и в publish; dev-запуск
  по-прежнему читает исходное дерево (проверено правкой `StartLives` без пересборки).
- Убран неиспользуемый `AudioManager._frame` (единственный warning сборки — теперь 0 warnings).
- Документация обновлена.

## 7. Известные ограничения и подводные камни

- `assets/config.json` теперь копируется в выходной каталог и в publish, но приоритет у исходного
  дерева: `GameApp.ConfigPath` ищет вверх каталог, где есть и `assets`, и `src`. Это и есть F5-сценарий;
  если правило поменять на простое `File.Exists`, игра начнёт брать копию из `bin` и правки перестанут
  подхватываться без пересборки.
- Силовые поля, pod-барьеры и название комнаты не освещаются (осознанно), у них своя эмиссия/аддитив,
  поэтому тени на них не падают.
- Шейдеру нужен OpenGL 3.3. Если он не собрался, `LightingShader.IsAvailable == false` и сцена
  рисуется без света (игра не падает). Свечение при этом продолжает работать: у `GlowMap` свой шейдер,
  и `Composite` проверяет только его валидность.
- Extent карты теней (`ShadowMap.HalfExtent = 27`) рассчитан на комнату 40x22. При смене размера
  комнаты/камеры править.
- `artifacts/` в `.gitignore`: ATR, PDF и скриншоты комнат на новой машине отсутствуют.
  Для сверки комнат их нужно принести отдельно.
- Рендер не покрыт тестами: проверяй через `--shot` (и `--view`, `--room`, `--settings`, `--zoom`/`--look`).
- Настройки/рекорды лежат в `%APPDATA%` и не переносятся с репозиторием.
- CI (GitHub Actions) нет: сборка и тесты запускаются локально.
- Если правишь ассеты, помни: файлы копируются в выходной каталог при сборке.

## 8. Куда смотреть для типовых задач

| Задача | Файл / место |
|---|---|
| Правила, тайминги, баланс | `src/Shamus.Core/Simulation/GameWorld.cs`, `assets/config.json` |
| Новый враг | `EnemyKind`, ветки в `GameWorld.Decide/UpdateEnemies`, отрисовка в `SceneRenderer` |
| Внешний вид стен | `RoomView`: `FieldPink`, `FieldHeight`, `FieldThickness`, `FieldAlpha` |
| Столбики-генераторы | `RoomView.DrawPosts`, `PostBody/Top` |
| Высоты в комнате | `Rendering/RoomMetrics.cs` (единый масштаб; стена = 1.0 клетки) |
| Свет и тени | `LightingShader` (`LightDirection`, `Ambient`, `Diffuse`), `ShadowMap` |
| Свечение/bloom | `GlowMap` (`Strength`), `RoomView.DrawEmissive`, `SceneRenderer.DrawEmissive` |
| Танк | `SceneRenderer.DrawTank` |
| Камера/letterbox | `CameraRig`, `GameApp.Run`, `SceneRenderer.VirtualWidth/Height` |
| Меню и настройки | `GameApp.BuildMenus/BuildSettings`, `Ui/SettingsMenu.cs`, `Ui/Settings.cs` |
| Звук | `Audio/ProceduralSfx.cs`, `Audio/AmbientSynth.cs`, `Audio/AudioManager.cs` |
| Формат лабиринта | `docs/MAZE_FORMAT.md`, `MazeData`, `RoomGrid`, `RoomWalls` |
| Карта | `MapLayout`, `Ui/MapView.cs`, `assets/maps/c64.map` |

## 9. Проверка перед коммитом

```bash
dotnet build Shamus.sln
dotnet test Shamus.sln --no-build
```

Ожидаемо: 0 ошибок, 61/61 тест. Для визуальной проверки:

```bash
dotnet run --project src/Shamus.Game -- --maze c64 --room 3 --view iso --mute --shot shot.png
dotnet run --project src/Shamus.Game -- --settings --mute --shot settings.png
dotnet run --project src/Shamus.Game -- --winsize 1000x700 --room 3 --view iso --mute --shot bars.png
```

Если в `settings.json` включён `Fullscreen`, `--shot` снимет полноэкранный кадр. Для оконных
скриншотов выключи fullscreen в SETTINGS/VIDEO или временно поправь `settings.json`.

## 10. Ближайшие шаги (предложения)

1. Модели врагов и предметов (сейчас примитивы), эффекты попаданий/частицы.
2. CI: `dotnet build` + `dotnet test` на GitHub Actions.
3. Свечение: настраивать `GlowMap.Strength`, при желании — реальный bloom с depth-буфером основной
   сцены (сейчас эмиттеры светятся и сквозь стены).
4. Ещё мягче тени: 2048 на краях читаются резко; кандидаты — больший радиус PCF или второй проход
   размытия карты глубины.
5. Документация: обновлять DESIGN/TECHNICAL при изменении механик; скриншоты — в `artifacts/shots` (локально).

## 11. Git

- Ветка `main`, remote `origin` = `https://github.com/blalbabla/shamus-remake.git`.
- Стиль сообщений: Conventional Commits, коротко (`feat(beams): ...`, `fix(render): ...`).
- Перед переносом на другую машину: `git status` должен быть чистым, затем `git push`.
- `artifacts/` и `bin/obj` игнорируются.
