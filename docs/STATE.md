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
  Rendering/LightingShader.cs    Lambert + ambient + PCF-тени
  Rendering/ShadowMap.cs         depth-FBO 1024, ортокамера света
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
  силовые поля; столбики-генераторы; танк-игрок; направленный свет с тенями.
- **UI:** титульный экран (только меню), SETTINGS (Gameplay/Video/Audio), пауза, Game Over, карта,
  HUD, тосты, рекорды по лабиринтам.
- **Звук:** 18 процедурных эффектов (2–5 вариантов), живой эмбиент, отдельные громкости эффектов/музыки.
- **Инструменты/тесты:** извлечение карт из ATR, сверка комнат со скриншотами, 61 xUnit-тест.

## 5. Как устроен кадр (важно при правках рендера)

Порядок в `GameApp.Run`:

1. `BeginDrawing`, очистка чёрным.
2. `SceneRenderer.RenderShadow(world, alpha)` — depth-карта света в FBO 1024 (переключает FBO и
   сбрасывает матрицы, поэтому вызывается до настройки letterbox).
3. Расчёт viewport/scissor: кадр 1280x720 вписывается в окно с полями 16:9.
4. 2D-ортопроекция `0..1280x720` (UI рисуется в этих координатах и масштабируется в viewport).
5. `Draw`: сцена, HUD, меню.

`SceneRenderer.Draw`:

1. `Rig.BeginView()` — кастомная ортопроекция 16:9 (`Raymath.MatrixOrtho` + `MatrixLookAt`).
   `Raylib.BeginMode3D` не используется: он берёт аспект из окна и искажал бы сцену.
2. `LightingShader.Begin()` → `DrawWorld(...)` — пол, закрытые зоны, столбики, объекты, танк, враги,
   Shadow, пули (всё непрозрачное, освещается и отбрасывает тени).
3. `LightingShader.End()`.
4. Без света: название комнаты, pod-барьеры (аддитивные), силовые поля.
5. `Rig.EndView()`; 2D-подпись «?» для mystery.

`RenderShadow` рисует в карту глубины только «кастеров»: столбики, объекты, танк, врагов, пули.
Пол в карту не пишется, иначе он затенял бы сам себя.

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
- MSAA 4x включён на окне; FBO тени — без MSAA.

## 6. Что сделано в последней сессии

- Исправлен порядок батча/глубины: свечение больше не рисуется поверх генераторов.
- Главное меню переделано: START GAME / SETTINGS / QUIT; экран SETTINGS с разделами
  GAMEPLAY (лабиринт, seed, сложность, God mode), VIDEO (окно/полный экран), AUDIO (эффекты, музыка).
- Окно resizable, кадр letterboxed в 16:9, fullscreen — в разрешении дисплея, MSAA сохранён.
- Лучи заменены на вертикальные полупрозрачные розовые силовые поля (одна панель на отрезок стены).
- Игрок — танк: корпус, две гусеницы, башня-полусфера и пушка; поворот по `FaceX/FaceY`.
- Добавлены направленный свет сверху-слева и карта теней (адаптация `ShadowMap`/`LightingShader`
  из проекта lode-runner-remake).
- Документация обновлена.

## 7. Известные ограничения и подводные камни

- `assets/config.json` не включён в `<Content>` `Shamus.Game.csproj`: в dev-запуске он находится
  поиском вверх по дереву, но в `dotnet publish` его не будет, и игра молча возьмёт дефолты
  `GameConfig`. Починка: добавить `Content Include="..\..\assets\config.json"` с копированием.
- Высоты не согласованы: стены и столбики 0.8, pod-барьеры 1.6, танк ~0.9. Кандидат на общий масштаб.
- Силовые поля, pod-барьеры и название комнаты не освещаются (осознанно), поэтому тени на них не падают.
- Шейдеру нужен OpenGL 3.3. Если он не собрался, `LightingShader.IsAvailable == false` и сцена
  рисуется без света (игра не падает).
- Extent карты теней (`ShadowMap.HalfExtent = 27`) рассчитан на комнату 40x22. При смене размера
  комнаты/камеры править.
- `artifacts/` в `.gitignore`: ATR, PDF и скриншоты комнат на новой машине отсутствуют.
  Для сверки комнат их нужно принести отдельно.
- Рендер не покрыт тестами: проверяй через `--shot` (и `--view`, `--room`, `--settings`).
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
| Свет и тени | `LightingShader` (`LightDirection`, `Ambient`, `Diffuse`), `ShadowMap` |
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

1. Согласовать высоты: стены/столбики/pod-барьеры/танк в одном масштабе.
2. Починить копирование `assets/config.json` в выходной каталог (publish).
3. Довести свет: подсветка/эмиссия силовых полей, более мягкие тени (2048/больше PCF), лёгкий bloom.
4. Модели врагов и предметов (сейчас примитивы), эффекты попаданий/частицы.
5. CI: `dotnet build` + `dotnet test` на GitHub Actions.
6. Документация: обновлять DESIGN/TECHNICAL при изменении механик; скриншоты — в `artifacts/shots` (локально).

## 11. Git

- Ветка `main`, remote `origin` = `https://github.com/blalbabla/shamus-remake.git`.
- Стиль сообщений: Conventional Commits, коротко (`feat(beams): ...`, `fix(render): ...`).
- Перед переносом на другую машину: `git status` должен быть чистым, затем `git push`.
- `artifacts/` и `bin/obj` игнорируются.
