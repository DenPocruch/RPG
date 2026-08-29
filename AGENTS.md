# RPG Farm — заметки для AI-ассистента

Unity 6 (6000.0.59f2), 2D ферма-RPG в стиле Stardew Valley, платформа Android.
Владелец проекта говорит по-русски — общаться на русском.
Проект под git — ПЕРЕД сессиями с правками сцен убеждаться, что есть свежий коммит.

## Архитектура
- **PersistentRoot** (DontDestroyOnLoad): Player, Canvas (весь UI), все менеджеры (SaveManager, CurrencyManager, PlayerLevel, SkillTreeManager, HotbarManager, InventoryUI и т.д.)
- **Сцены**: SampleScene (база/ферма), City (город), Beginner Forest. Сцены содержат только «мир» (тайлмапы, NPC, сундуки, склады)
- **SaveManager**: атомарная запись (save_temp → save.json + save_backup.json), версия 2, dirty-flag. Ключи per-scene: `ИмяСцены/ключ` для сценовых объектов, глобальные для PersistentRoot (см. `SaveManager.FullKey`). Оффлайн-прогресс через `DateTime.UtcNow.Ticks` (растения, животные, спрос скупщика)
- **Синглтоны**: ВСЕ имеют защиту от дубликатов в Awake (`Instance != null → Destroy`). Сценовые (FarmManager, склады) — с проверкой сцены. UI-синглтоны в Canvas переподписываются на события пересозданных складов через `BindToStorage()` (вызывают сами склады в Start)

## Дерево навыков (68 перков, `Resources/SkillNodes/Tree/`)
- Вкладки: Combat / Farming / Crafting. 3 очка за уровень
- Лимит грядок: база 10 (`FarmManager.basePlotLimit`) + перки (+2×5, +1×10, +5×2)
- Семена открываются перками (тег `seed_*` в `unlocksFeature`, эффект UnlockItem): у торговца (TraderStall) товар с `unlockTag` виден только после покупки перка
- Качество урожая: серебро (перк ур.5 ×10 рангов), золото (ур.10 ×10), пурпур (ур.15 ×10, +10%/ранг → 100% на максе). Ассеты в `Resources/Items/Quality/` — имя = `Урожай Silver/Gold/Purple`, спрайты со звёздами из `Assets/Art/Crops/All Crops.png` (ВНИМАНИЕ: Unity Y снизу вверх! tdY = 288 - metaY - 16). Свёкла без звёздных спрайтов — фолбэк на обычную
- Повар (CookStorage) принимает звёздные плоды как ингредиенты: `IsSameCrop` по префиксу имени + суффиксы Silver/Gold/Purple. Списание: обычные → серебро → золото → пурпур. Считает инвентарь И хотбар (`AllSlots()`)
- UI рецептов (CookUI) считает через `CookStorage.CountIngredients()` — НЕ имеет своего счётчика

## Скупщик урожая (работает)
- `BuyerManager.cs` (объект «BuyerManager» в City, там же NPC Дрон): базовые цены 18 культур, спрос дня ×2 (меняется раз в 4ч РЕАЛЬНОГО времени, пул — только разблокированные перками культуры, теги `seed_*`), репутация 5 уровней (0/2000/6000/15000/40000g → +0/5/10/15/20%)
- Цена: база × качество (×1.15/1.3/1.5) × спрос (×2) × репутация
- `SellUI.cs` — ДОСТРОЕН и работает. ВАЖНО: скрипт САМ строит/чинит свой UI кодом (`EnsureBuiltUI` в Awake) — шапку (заголовок, спрос, репутация), строки списка, автовайр CloseButton. Ссылки в инспекторе НЕ нужны (MCP не умеет биндить сценовые ссылки — поэтому так). Панель: Canvas/Sell Panel (650×650, спрайт dialogue box_0), список: RecipeScrollView/Viewport/Content. Флаг `debugAutoOpen` — автооткрытие панели через 2.5с для отладки
- Грабли UI-кода: на GO с Image нельзя AddComponent<TextMeshProUGUI> (2 Graphic запрещено — текст только дочерним объектом «Label»); правые элементы строки — якоря (1,0)-(1,1) с ОТРИЦАТЕЛЬНЫМИ offsetMin.x (иначе кнопка уезжает за левый край и накрывает строку); всем текстам overflowMode=Ellipsis
- Продажа: `BuyerManager.Sell()` → AddGold + репутация. Кнопки «×1»/«Всё», клик по строке = продать 1

## Взаимодействие с NPC (механика)
- Атака = взаимодействие: `PlayerMovement.Attack()` → `InteractionDetector.TryInteract()` (OverlapBox по маске слоя 8 «Interactable», m_Bits 256). Просто подойти недостаточно!
- `NPCInteractable`: detectRadius 2 (показ «!»), talkRadius 1 (ближе!) — иначе в консоль падает «Подойди ближе» (на телефоне невидимо)
- Зона `InteractZone` (trigger, слой 8) обязана сидеть НА САМОМ NPC (localPos ~0,0). Была case: зона скупщика улетела на +7.66м и «воровала» удары у лавки семян → магазин не открывался
- Магазин семян: TraderStall → `ShopInteraction` (без диалога, удар сразу открывает) → `ShopUI.Open()`. Биндинги ShopUI в инспекторе полные
- Диалоговые ассеты (`Resources/Dialogue/*.asset`): YAML пустых значений требует хвостовой пробел (`conditionTag: `) — без него Unity не парсит ассет (ошибка «Parser Failure»), диалог = null, NPC молчит

## Запуск игры
- Play ТОЛЬКО из SampleScene (там живёт PersistentRoot: Player, Canvas, менеджеры). Прямой запуск City = нет игрока и UI
- Для отладки города: SampleScene + City аддитивно, либо играть через портал

## NPC города
CookNPC=Густав (повар), BlacksmithNPC=Кузнец Степан, TraderStall (Лавка, семена), BuyerNPC=Скупщик Дрон, LumberjackHouse, StonemasonHouse. Все синглтоны с защитой от дубликатов (копии PersistentRoot при возврате в сцену!).

## MCP Unity — ПОДКЛЮЧЁН
- opencode.json настроен (сервер: Library/PackageCache/com.gamelovers.mcp-unity@0e9fdb65b3cc/Server~/build/index.js, порт 8090)
- Unity: Tools → MCP Unity → Server Window → Server Online
- Инструменты: create/update GameObject, update_component (поля!), add_asset_to_scene, save_scene, get_console_logs, play mode, batch_execute
- **Грабли MCP** (проверено): `update_component` НЕ умеет ссылки на объекты СЦЕНЫ (только ассеты) — скрипты должны сами резолвить референсы в Awake; неактивные объекты не находятся ни по имени, ни по пути; `recompile_scripts` часто таймаутит — просто повторить запрос (компиляция при этом проходит); instanceId меняются после каждой рекомпиляции; `get_gameobject` с большим maxDepth даёт мега-дампы — брать maxDepth 1-3 и includeComponentProperties=false
- Ошибка консоли «McpUnityServerBatchModeTests.cs has no meta file» — шум самого пакета, игнорировать

## Правила работы
1. Правишь C# — Play-режим должен быть выключен
2. Правишь файлы сцен (City.unity/SampleScene.unity) — сцена в Unity должна быть закрыта, иначе юзер перезапишет правки. После правок юзер перезагружает сцену БЕЗ сохранения
3. Unity Y-координаты спрайтов в мете — снизу вверх (tdY = H - metaY - 16)
4. enum'ы эффектов (SkillEffectType) — новые значения добавлять только В КОНЕЦ (ассеты хранят int)
5. Коммитить перед сессиями с правками сцен
6. Git: EOL-конвертация ВЫКЛЮЧЕНА (.gitattributes `* -text`, autocrlf=false) — НЕ возвращать обратно, иначе 15k файлов снова станут «изменёнными» и git начнёт грузить CPU

## Скупщик урожая (работает полностью)
- `BuyerManager.cs` (объект в City у NPC): базовые цены 18 культур (+Rice 40, Onion 40), спрос дня ×2 (меняется раз в 4ч РЕАЛЬНОГО времени, пул — только разблокированные перками культуры, теги `seed_*`), репутация 5 уровней (0/2000/6000/15000/40000g → +0/5/10/15/20%)
- Цена: база × качество (×1.15/1.3/1.5) × спрос (×2) × репутация
- `SellUI.cs` — ДОСТРОЕН и работает. ВАЖНО: скрипт САМ строит/чинит свой UI кодом (`EnsureBuiltUI` в Awake) — шапку (заголовок, спрос, репутация), строки списка, автовайр CloseButton. Ссылки в инспекторе НЕ нужны (MCP не умеет биндить сценовые ссылки — поэтому так). Панель гасится на старте (`SetActive(false)` в Start)
- Грабли UI-кода: на GO с Image нельзя AddComponent<TextMeshProUGUI> (2 Graphic запрещено — текст только дочерним объектом «Label»); правые элементы строки — якоря (1,0)-(1,1) с ОТРИЦАТЕЛЬНЫМИ offsetMin.x (иначе кнопка уезжает за левый край и накрывает строку); всем текстам overflowMode=Ellipsis
- Продажа: `BuyerManager.Sell()` → AddGold + репутация. Кнопки «×1»/«Всё», клик по строке = продать 1

## Кормушки и поилки (новое, требует проверки в Play)
- **Кормушка**: предмет `Resources/Items/Animals/Feeder.asset` (itemType 26), префаб `Assets/Prefab/Feeder.prefab` (спрайт Barn objects_17), скрипт `FeederStorage.cs`. **Поилка**: `WaterTrough.asset` (27), `WaterTrough.prefab` (Barn objects_22), `WaterTrough.cs`
- Размещение: `PlayerMovement.UpdatePlacementGhost` — выбрал в хотбаре → полупрозрачный ghost ездит ПЕРЕД игроком (зелёный=можно/красный=занято), атака = поставить, смена слота = отмена. **Поворот**: повторный тап по АКТИВНОМУ слоту (`HotbarManager.SetActiveSlot` → `PlayerMovement.NotifySlotRetapped`, +90°). `CanPlaceAt` запрещает ставить на коллайдеры (игрок/животные не мешают)
- Сохранение: `PlaceablesSaveManager` (автосоздаётся из `SaveManager.ProcessScene`, ключ `ИмяСцены/placeables`). ВАЖНО: в `ProcessScene` EnsureInScene-вызовы стоят ДО early-return `initialSceneResolved` — иначе при возврате в стартовую сцену менеджеры не создаются и ничего не восстанавливается. Сейв зовётся сразу при постановке/наливе/загрузке корма (не ждём автосейва 60с)
- Удар по кормушке: если в хотбаре КОРМ (`FeedUI.IsAnimalFeed`, кэш по `AnimalData.feedItem`) → быстрая загрузка всего стака (`QuickLoad`); другой корм в кормушке → замена, старый в рюкзак; пустые/другие руки → окно FeedUI
- Надписи над кормушкой/поилкой: WorldLabel (иконка корма/лейки + «2/5», TMP 3D, обводка через fontMaterial-OUTLINE)
- FeedUI (окно корма) строит себя кодом, создаётся при первом ударе по кормушке под Canvas. Показывает только предметы-корма (match `AnimalData.feedItem` по имени). Кнопки +1/+5/Всё, «Забрать всё»
- **Голод**: после продукции животное `wantsFood=true` → само идёт к ближайшей кормушке С ЕГО кормом (`FeederStorage.FindNearest`, радиус 15) → ест 2с → уходит (leaveDir в wanderBias). Нет корма/кормушек — старое поведение (с руки). `HandleProduction` стоит при `wantsWater`
- **Жажда**: тик `drinkInterval=180с` ТОЛЬКО если есть поилки в мире → идёт пить 1 ед. воды, производство стоит пока не попьёт
- Вместимость: кормушка 5 + `feeder` (+1/ранг) + `feeder_big` (+1/ранг); поилка 30 + `trough`×10. Перки-ассеты: `Resources/SkillNodes/Tree/Farming/Animals/Unlock_Feeder|Farming_BigFeeder|Unlock_Trough` (эффект UnlockFeature, теги в `unlocksFeature`)
- Магазин: `ShopInteraction.EnsureFarmStock` добавляет кормушку (500g, тег `feeder`) и поилку (400g, тег `trough`) КОДОМ во вкладку животных торговца (НЕ `animal_*` — иначе TryBuy применит лимит детёнышей!)
- SkillTreeManager.MergeFarmNodes — рантайм-фолбэк: новые перки автодобавляются в allNodes из Resources. UI-кнопки в дереве (SkillNodeUI) юзер добавляет ВРУЧНУЮ в сцене
- Индикатор голода: иконка корма над головой (`HungerIcon`, качается, синий tint при жажде)
- **Сбор кормушки/поилки**: предмет `Resources/Items/Hammer.asset` (itemType Hammer, добавлен В КОНЕЦ enum ItemType), продаётся у торговца кодом (ShopInteraction.EnsureFarmStock, 200g, без unlockTag). Иконка — временно от кирки, ЗАМЕНИТЬ в инспекторе. В хотбаре: молоток → объект перед игроком в радиусе `hammerRange` подсвечивается ЗЕЛЁНЫМ (PlayerMovement.UpdateHammerHighlight, tint SpriteRenderers с восстановлением цвета); удар = разобрать в рюкзак (корм сначала выгружается TakeAllBack, если рюкзак полон — откат; вода поилки пропадает). Проверка молотка в `Attack()` стоит ДО InteractionDetector.TryInteract — иначе удар по кормушке открыл бы FeedUI. Иконку молотка нельзя делать из «All Crops» — в ассет-паке иконок (RPG icons) молотка нет
- **Оффлайн-расход корма/воды** — работает: `AnimalController.SimulateOfflineNeeds` (очередь offlineQueue, запуск из SaveManager.ProcessScene после спавна кормушек/поилок). Списание сохраняется через `SaveManager.ScheduleDelayedSave(3f)` (мгновенный Save при загрузке сцены поймал бы полусозданное состояние систем)

## Покупка животных (работает)
- Предметы-детёныши: `Resources/Items/Animals/*Baby.asset` (Chicken/Cow/Pig/Ostrich), itemType=AnimalBaby (14), иконки-морды из skilltree_sheet_3, `animalPrefab` = префаб из `Assets/Prefab/`
- Спавн: детёныш в хотбаре → активен → удар = спавн рядом (`PlayerMovement.SpawnAnimal`)
- Лимиты: перк animal_* ранг 1 = 2 шт, +1 за ранг, макс 10 (`ShopManager`, ранг через `GetNodeRankByFeature`), купленное сохраняется (`animal_shop`). ИСПРАВЛЕНО: `ShopUI.OnBuyClick` сбрасывает `selectedQuantity` после ЛЮБОЙ попытки покупки (раньше накопленное количество «докупалось» после прокачки перка), `GetAllowedMax` клэмпит +/− до остатка лимита
- Персистентность: `AnimalSaveManager.RestoreState` спавнит купленных из сейва (префаб из `AnimalData.animalPrefab`)
- ИСПРАВЛЕНО: префабы Cow/Pig использовали данные страуса — теперь Cow Black.prefab→Cow Black Brown, Pig Mud Pink→Pig Mud Pink Brown 1
- Без перков/префабов пока: Duck, Goose (нет префабов), Goat, Sheep (нет перков)

## Хвосты / идеи
- Свёкла (Beetroot) — нет звёздных спрайтов в All Crops.png (фолбэк)
- Покупка животных (теги animal_* в перках ждут), рыбалка, квесты, звуки
- Blueberry/Melon семена — нет узлов разблокировки в дереве (не продаются у торговца)
- Стиль панелей: юзер правит тексты панелей ВРУЧНУЮ в редакторе (Editor-тул PanelStyler есть, но юзер предпочёл руками)

## Враги (прототип Slime)
- Анимация КОДОМ, без Animator (как у животных): `Scripts/Enemy/EnemyData.cs` (SO, Create → RPG/Enemy: направления up/down/side как в AnimalData + damage/dead, FPS, sideFacesLeft) + `Scripts/Enemy/EnemyAnimator.cs`. SimpleEnemyAI сам AddComponent EnemyAnimator в Awake и Init(enemyData) в Start — биндить в инспекторе ничего не надо
- Листы врагов нарезаны рядами 4 кадра × 3 направления: ВЕРХНИЙ ряд = side (смотрит ВЛЕВО), средний = down, нижний = up. «Право» = flipX (sideFacesLeft=1). Проверено по старым клипам Assets/Animation/Slime (контроллер больше не используется, можно удалить). Dead.png имеет только 9 кадров (up-ряд недорезан) — up пустой, фолбэк на down
- Ассеты: `Resources/Enemies/*.asset` (SlimeBlue — Blue Slime, собран кодом парсингом .meta). Пивот кадров пока {0,0}, нужен {0.5,0} (TODO, править скриптом все .meta в Art/Enemy)
- `SimpleEnemyAI.cs` (UTF-8): патруль → погоня → урон касанием (damageToPlayer/damageCooldown), Alert (радиус/скорость), респавн на месте спавна. Направление анимации из вектора движения (DirFromVector). OnDamage/OnDeath зовутся из EnemyHealth
- Slime.prefab: Animator-компонент УДАЛЁН из YAML (конфликтовал бы с код-аниматором), в SimpleEnemyAI прописан enemyData → SlimeBlue.asset
- ВАЖНО: в сценах лежат 8 старых инстансов Slime (Beginner Forest ×7, SampleScene ×1) — у них enemyData=null (ворнинг «не назначен EnemyData», анимации нет): удалить и накидать префаб заново
- Новые типы врагов: создать EnemyData (кадры по направлениям из папки в Art/Enemy) + дубликат Slime.prefab с этим ассетом
- **Myconid** (`Assets/Art/Enemy/Myconid/<Цвет>/`, 5 цветов: Blue/Green/Pink/Purple/Red): листы Attack 6×4 / Walk 6×4 / Idle 4×4 / Damage 4×4 / Dead 5×4, кадры 32×32. Ряды: 0 = лицом к зрителю (down), 1 = спиной (up), 2 = повёрнут ВПРАВО (sideRight), 3 = ВЛЕВО (side). У микота НЕТ глаз — не ориентироваться на «белые точки» шляпки. Нарезка всех 25 .meta переписана на сетку 32×32, pivot {0.5, 0.1875} (ноги = 6px от низа ячейки — YSort считает по ногам), alignment 9 (script Temp-скриптом, guid/fileID сохранены из git)
- **Ближняя атака с анимацией**: EnemyData.DirectionalFrames получил `sideRight` (отдельные кадры «вправо», без flipX) + EnemyData.attack; EnemyAnimState — Attack (добавлен В КОНЕЦ). EnemyAnimator: Attack = one-shot (как Damage, возврат к циклу); SimpleEnemyAI: если attack-кадры заполнены → режим «подошёл на attackRange → стоит, бьёт с анимацией (удар наносится через attackHitDelay, кулдаун attackCooldown)», урон касанием отключён; у слаймов attack пустой → старое поведение касания
- Ассеты Myconid: Tools → Enemy → Create Myconid EnemyData (`Assets/Editor/MyconidDataBuilder.cs`) → `Resources/Enemies/Myconid<Цвет>.asset`. Префаб — дубликат Slime.prefab (SpriteRenderer + CircleCollider2D + EnemyHealth + SimpleEnemyAI с enemyData = Myconid<Цвет>), Animator-компонент не добавлять
- Все слаймы (`Assets/Art/Enemy/Slimes/<Цвет>/`): 6 цветов, у каждого 2 вида. EnemyData-ассеты `Resources/Enemies/`: SlimeBlue (средний, из папки Blue\Slime — полный набор Idle/Walk/Damage/Dead PNG) + Slime<Цвет>Big / Slime<Цвет>Small (Black/Golden/Green/Pink/Pupple, собраны кодом парсингом .meta). Big = `Big Slime.png` (128×384, 12 рядов: idle/walk/damage/dead — по 3 ряда side,down,up; dead-ряд 5 кадров, у Golden 4). Small = `Small Slime.png` (128×128, 4 ряда side-only: idle/walk/damage/dead, up/down пустые — фолбэк аниматора). Нарезка Small-мета переписана на сетку 32×32 кодом (auto-slice терял/склеивал кадры). Префабы для новых видов пока не созданы (дубликат Slime.prefab + свой EnemyData). Средние: Slime<Цвет>.asset из `Slime.png` (нарезка переписана на сетку 32×32, 48 кадров — авто-нарезка склеивала кадры смерти; в смерти 4 кадра). Blue-medium = SlimeBlue (из папки Blue\Slime)

## Гоблины (Spear + Archer)
- Спрайты `Assets/Art/Enemy/Goblins/Spear Goblin|Archer Goblin/` (Idle 4 / Walk 6 / Run 8 / Spear 6 / Bow 7 / Damage 4 / Dead 4 кадров, сетка 32×32, 3 ряда): ряд 0 = лицом (down), 1 = спина (up), 2 = ВПРАВО (sideRight). Ряда «влево» НЕТ — в EnemyData side = sideRight, sideFacesLeft = false (лево = зеркало, работает в EnemyAnimator без правок)
- Все 12 .meta переписаны с auto-slice (были кривые прямоугольники 17×20, pivot 0,0) на сетку 32×32, pivot {0.5, 0.1875}, alignment 9, guid/internalID сохранены
- Ассеты: Tools → Enemy → Create Goblin EnemyData (`Assets/Editor/GoblinDataBuilder.cs`) → `Resources/Enemies/GoblinSpear|GoblinArcher.asset` (walk = Walk; Run 8 кадров нарезан, можно переключить в ассете)
- **Дальний бой** (стрелок): SimpleEnemyAI получил поля `arrowPrefab` (если назначен → режим стрелка), `firePoint` (Transform — ОТКУДА вылетает стрела, дочерний пустой объект), shootRange/minShootDistance/shootCooldown/arrowSpeed/arrowDamage. Стрелок держит дистанцию: в shootRange стоит и стреляет с анимацией Attack (стрела через attackHitDelay), ближе minShootDistance — отходит спиной, глядя на игрока. detectionRange автоматом не меньше shootRange. Урон касанием у стрелка отключён
- `Arrow.cs`: добавлен `InitEnemy(dir, dmg, spd, rng)` (fromEnemy) — игнорирует врагов (тег Enemy И их дочерние хитбоки без тега через `GetComponentInParent<EnemyHealth>()` — иначе стрела умирает в коллайдере стрелявшего), бьёт PlayerHealth, уничтожается о всё. Обычная стрела игрока (Init) не тронута. SimpleEnemyAI при спавне стрелы включает isTrigger на коллайдерах (иначе OnTriggerEnter2D не срабатывает) и стреляет из `Assets/Prefab/Arrow.prefab`
- Префабы: Tools → Enemy → Create Goblin Prefabs → `Assets/Prefab/Enemy/Goblins/GoblinSpear.prefab` + `GoblinArcher.prefab` (база Slime.prefab: наследуют EnemyHealth/лут/YSort; у лучника child FirePoint (0.2, 0.5) — двигать в префабе, куда нужно)
