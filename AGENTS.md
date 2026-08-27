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

## Покупка животных (работает)
- Предметы-детёныши: `Resources/Items/Animals/*Baby.asset` (Chicken/Cow/Pig/Ostrich), itemType=AnimalBaby (14), иконки-морды из skilltree_sheet_3, `animalPrefab` = префаб из `Assets/Prefab/`
- Спавн: детёныш в хотбаре → активен → удар = спавн рядом (`PlayerMovement.SpawnAnimal`)
- Лимиты: перк animal_* ранг 1 = 2 шт, +1 за ранг, макс 10 (`ShopManager`, ранг через `GetNodeRankByFeature`), купленное сохраняется (`animal_shop`)
- Персистентность: `AnimalSaveManager.RestoreState` спавнит купленных из сейва (префаб из `AnimalData.animalPrefab`)
- ИСПРАВЛЕНО: префабы Cow/Pig использовали данные страуса — теперь Cow Black.prefab→Cow Black Brown, Pig Mud Pink→Pig Mud Pink Brown 1
- Без перков/префабов пока: Duck, Goose (нет префабов), Goat, Sheep (нет перков)

## Хвосты / идеи
- Свёкла (Beetroot) — нет звёздных спрайтов в All Crops.png (фолбэк)
- Покупка животных (теги animal_* в перках ждут), рыбалка, квесты, звуки
- Blueberry/Melon семена — нет узлов разблокировки в дереве (не продаются у торговца)
- Стиль панелей: юзер правит тексты панелей ВРУЧНУЮ в редакторе (Editor-тул PanelStyler есть, но юзер предпочёл руками)
