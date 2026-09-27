using UnityEngine;
using UnityEditor;

/// <summary>
/// Часть 1 лора: диалоги знакомств.
/// Tools → Lore → 1. Build Dialogues.
/// Повторный запуск ОБНОВЛЯЕТ in place (guid целы).
/// Магазинные действия НЕ трогает смыслово, только добавляет лор-ветки:
/// OpenCook=1, CollectDishes=2, LeadToCraft=3, OpenShop=5, OpenSell=6, Custom=7.
/// </summary>
public static class LoreDialogueBuilder
{
    const string DIR = "Assets/Resources/Dialogue";

    [MenuItem("Tools/Lore/1. Build Dialogues")]
    public static void BuildAll()
    {
        BuildMayor();
        BuildFarmMayor();
        BuildCook();
        BuildBlacksmith();
        BuildSeedTrader();
        BuildBuyer();
        BuildMorek();
        BuildToolTrader();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[Lore] Диалоги собраны: Мэр, Мэр-на-ферме, Густав, Степан, Марта, Дрон, Морек, Борис.");
        ValidateAll();
    }

    // ── Проверка: битые ссылки, недостижимые узлы, дубли id, пустые тексты,
    //    условия без пары (тег в диалоге, который никто не ставит в коде, = кнопка-призрак).
    //    Проверяет ВСЕ ассеты в Resources/Dialogue, не только что собранные. ──
    // Входные узлы автодиалогов (без ссылок из хаба — так задумано, см. StartDialogueAt)
    static readonly System.Collections.Generic.HashSet<string> EntryNodes =
        new System.Collections.Generic.HashSet<string> { "Mayor_Dialogue:7" };

    static void ValidateAll()
    {
        int errors = 0, warnings = 0;
        var guids = AssetDatabase.FindAssets("t:DialogueData");
        var usedTags = new System.Collections.Generic.HashSet<string>();
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var d = AssetDatabase.LoadAssetAtPath<DialogueData>(path);
            if (d == null || d.nodes == null) continue;
            string who = d.name;

            var byId = new System.Collections.Generic.Dictionary<int, int>();
            foreach (var n in d.nodes)
            {
                if (n == null) continue;
                if (byId.ContainsKey(n.id))
                {
                    Debug.LogError("[Lore-check] " + who + ": дубль узла id=" + n.id);
                    errors++;
                }
                else byId[n.id] = 1;
                if (string.IsNullOrEmpty(n.text))
                {
                    Debug.LogWarning("[Lore-check] " + who + ": узел " + n.id + " без текста");
                    warnings++;
                }
                if (n.options == null) continue;
                foreach (var o in n.options)
                {
                    if (o == null) continue;
                    if (o.nextNodeId >= 0 && !byId.ContainsKey(o.nextNodeId) && !HasNodeId(d, o.nextNodeId))
                    {
                        Debug.LogError("[Lore-check] " + who + ": узел " + n.id + " → нет узла " + o.nextNodeId + " («" + o.text + "»)");
                        errors++;
                    }
                    if (!string.IsNullOrEmpty(o.conditionTag)) usedTags.Add(o.conditionTag);
                }
            }
            // Достижимость от стартового узла
            if (d.GetNode(d.startNodeId) == null)
            {
                Debug.LogError("[Lore-check] " + who + ": нет стартового узла " + d.startNodeId);
                errors++;
            }
            else
            {
                var seen = new System.Collections.Generic.HashSet<int>();
                var stack = new System.Collections.Generic.Stack<int>();
                stack.Push(d.startNodeId);
                while (stack.Count > 0)
                {
                    int id = stack.Pop();
                    if (!seen.Add(id)) continue;
                    var n = d.GetNode(id);
                    if (n == null || n.options == null) continue;
                    foreach (var o in n.options)
                        if (o != null && o.nextNodeId >= 0) stack.Push(o.nextNodeId);
                }
                foreach (var n in d.nodes)
                {
                    if (n != null && !seen.Contains(n.id) && !EntryNodes.Contains(who + ":" + n.id))
                    {
                        Debug.LogWarning("[Lore-check] " + who + ": узел " + n.id + " недостижим от старта");
                        warnings++;
                    }
                }
            }
        }
        // Условия: тег в диалогах, но никто не ставит в коде.
        // Тег может прийти строкой ("food_ready") или переменной
        // (const NoRodTag = "morek_norod") — резолвим оба случая.
        var setTags = new System.Collections.Generic.HashSet<string>();
        var varValues = new System.Collections.Generic.Dictionary<string, string>();
        var files = System.IO.Directory.GetFiles("Assets/Scripts", "*.cs",
            System.IO.SearchOption.AllDirectories);
        foreach (string file in files)
        {
            string code;
            try { code = System.IO.File.ReadAllText(file); }
            catch { continue; }
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(code,
                         "(?:const\\s+string|public\\s+string|string)\\s+(\\w+)\\s*=\\s*\"([^\"]+)\""))
                varValues[m.Groups[1].Value] = m.Groups[2].Value;
        }
        foreach (string file in files)
        {
            string code;
            try { code = System.IO.File.ReadAllText(file); }
            catch { continue; }
            foreach (System.Text.RegularExpressions.Match m in
                     System.Text.RegularExpressions.Regex.Matches(code,
                         "SetCondition\\(\\s*(?:\"([^\"]+)\"|(\\w+))"))
            {
                if (m.Groups[1].Success) setTags.Add(m.Groups[1].Value);
                else if (m.Groups[2].Success && varValues.TryGetValue(m.Groups[2].Value, out string v))
                    setTags.Add(v);
            }
        }
        foreach (string tag in usedTags)
        {
            if (!setTags.Contains(tag))
            {
                Debug.LogError("[Lore-check] Условие '" + tag + "' есть в диалогах, но нигде не ставится в коде — кнопка никогда не покажется!");
                errors++;
            }
        }
        foreach (string tag in setTags)
        {
            if (!usedTags.Contains(tag))
            {
                Debug.LogWarning("[Lore-check] Условие '" + tag + "' ставится в коде, но не используется в диалогах (мёртвое?)");
                warnings++;
            }
        }
        Debug.Log("[Lore-check] Итог: ошибок " + errors + ", предупреждений " + warnings + ".");
    }

    static bool HasNodeId(DialogueData d, int id)
    {
        if (d.nodes == null) return false;
        foreach (var n in d.nodes)
            if (n != null && n.id == id) return true;
        return false;
    }

    static DialogueData GetOrCreate(string file)
    {
        string path = DIR + "/" + file + ".asset";
        var d = AssetDatabase.LoadAssetAtPath<DialogueData>(path);
        if (d == null)
        {
            d = ScriptableObject.CreateInstance<DialogueData>();
            AssetDatabase.CreateAsset(d, path);
        }
        return d;
    }

    static DialogueOption Opt(string text, int next, DialogueActionType act = DialogueActionType.None, string param = "", string cond = "")
    {
        return new DialogueOption { text = text, nextNodeId = next, action = act, actionParam = param, conditionTag = cond };
    }

    static DialogueNode Node(int id, string text, params DialogueOption[] opts)
    {
        return new DialogueNode { id = id, text = text, options = opts };
    }

    static void Save(DialogueData d, string npc, int start, params DialogueNode[] nodes)
    {
        d.npcName = npc;
        d.startNodeId = start;
        d.nodes = nodes;
        EditorUtility.SetDirty(d);
    }

    // ── МЭР НА ФЕРМЕ (вторая копия, хвалит + набор) ──
    static void BuildFarmMayor()
    {
        var d = GetOrCreate("MayorFarm_Dialogue");
        Save(d, "Мэр Аврелий", 0,
            Node(0,
                "Отличная работа, пациент! Я верил — с этим пустяком ты справишься. Участок чист, слизни разбежались. Держи набор новосёла: мотыга, серп, лейка и семена пшеницы.",
                Opt("Забрать набор", -1, DialogueActionType.Custom, "GiveTools"),
                Opt("Спасибо, мэр!", -1))
        );
    }

    // ── МЭР АВРЕЛИЙ ──
    static void BuildMayor()
    {
        var d = GetOrCreate("Mayor_Dialogue");
        // портрет не трогаем — поставишь руками в инспекторе
        Save(d, "Мэр Аврелий", 0,
            Node(0,
                "Так-с... Пациент 217... переломы... ага. Поздравляю! Вы живы. Условно. Я — мэр Аврелий. Долг ваш — 500 000 кредитов. Отработаете — проснётесь. Вопросы?",
                Opt("Где я? Что это за место?", 1),
                Opt("Как мне выйти отсюда?", 2),
                Opt("Что делать прямо сейчас?", 3),
                Opt("Дай мне меч", 9, DialogueActionType.None, "", "mayor_sword"),
                Opt("Как там слизни? Что делать?", 8, DialogueActionType.None, "", "mayor_clear"),
                Opt("Слизней прогнал! Что дальше?", 6, DialogueActionType.None, "", "mayor_tools")),
            Node(1,
                "Долина Зари — место, где пациенты идут на поправку. У каждого здесь свой участок и своё дело: кто сажает, кто куёт, кто рыбу ловит. Работа — это лечение: руки заняты, голова отдыхает, а тело в капсуле восстанавливается.",
                Opt("А как мне выйти отсюда?", 2)),
            // Круг обучения: вопросы возвращают в цепочку страниц, единственный выход
            // с квестом — финал стр. 13 (или повтор 9). Случайно «не туда» выйти нельзя.
            Node(2,
                "Протокол прост: 1 золото здесь = 1 кредит там. Закроете 500 000 — открою вам глаза. Честно работайте, не деритесь в городе — тут сервер строгий. Вопросы?",
                Opt("Что делать прямо сейчас?", 3)),
            Node(3,
                "Участок №9 теперь твой — иди туда. Сначала разберись с мечом — он уже в рюкзаке, жёлтая стрелка и подсказки проведут. Потом иди на участок через портал на юге, гони слизней. Вернёшься — выдам инструмент и семена. Урожай потом понесёшь Дрону — он скупает.",
                Opt("А меч где?", 10)),
            Node(7,
                "Очнулся! Отлично... Пульс есть, рефлексы есть! Я — мэр Аврелий, твой куратор.",
                Opt("Что дальше?", 12)),
            Node(12,
                "Это Долина Зари — лечебная виртуалка. Тело твоё спит в капсуле, а здесь ты работаешь и выздоравливаешь. Долг за лечение — 500 000. Отработаешь — проснёшься.",
                Opt("И что мне делать?", 10)),
            Node(10,
                "Так, меч твой. Он уже лежит у тебя в рюкзаке — сам в руки не прыгнет.",
                Opt("А где рюкзак?", 11)),
            Node(11,
                "Жёлтая стрелка покажет рюкзак — открой его и найди меч.",
                Opt("Нашёл. Дальше?", 13)),
            Node(13,
                "Теперь зажми меч и тащи его в нижний ряд — это хотбар. Нажми на меч там, чтобы взять в руки. А уже потом — иди на СВОЙ участок №9 через портал на юге: он теперь твой, там слизни завелись — гони их всех. Закончишь — приходи ко мне, ещё поговорим.",
                Opt("Понял, погнал!", -1)),
            Node(8,
                "Слизни сами себя не убьют, пациент! Меч в руки — и на участок через портал на юге города. Гони их всех, потом приходи. Если меч потерял — скажи, выдам... хотя нет, не потеряй. Второй раз не дам!",
                Opt("Понял, погнал!", -1)),
            Node(9,
                "Меч уже у тебя в рюкзаке, пациент! Открой рюкзак, перетащи меч в нижний ряд и нажми на него. Потом — участок №9 и слизни. Приходи, как закончишь.",
                Opt("Забрать меч", -1, DialogueActionType.Custom, "GiveSword")),
            Node(6,
                "Отличная работа, пациент! Я верил — с этим пустяком ты справишься. Держи набор новосёла: мотыга, серп, лейка и 6 семян пшеницы. Мотыга уже будет в руках! Сажай рядом с домом — там недалеко колодец, воду для полива брать проще. Вскопай 6 грядок, посади, полей.",
                Opt("Давай инструменты, и я побежал!", -1, DialogueActionType.Custom, "GiveTools", "mayor_tools"))
        );
    }

    // ── ГУСТАВ (расширяем, кухня сохраняется) ──
    static void BuildCook()
    {
        var d = GetOrCreate("Cook_Dialogue");
        Save(d, "Повар Густав", 0,
            Node(0,
                "О-о-о! Новый желудок! Мон ами, ты вовремя — у меня как раз лук плачет! Чего хотел?",
                Opt("Приготовь мне еду", 1, DialogueActionType.OpenCook),
                Opt("Ты приготовил мою еду?", 2, DialogueActionType.CollectDishes, "", "food_ready"),
                Opt("Мэр прислал за хлебом", 12),
                Opt("Ты кто такой, Густав?", 10),
                Opt("Ничего, пока", -1)),
            Node(1,
                "Отлично! Неси ингредиенты, и я всё приготовлю.",
                Opt("А ты сам откуда?", 10)),
            Node(2,
                "Да, всё готово! Забирай.",
                Opt("Спасибо!", -1)),
            Node(10,
                "Я? Ха! В той жизни я баранку крутил — дальнобой, заправки, сосиски в тесте... Желудок угробил, сердце угробил. А здесь... здесь я учусь готовить заново. Настоящее! Понимаешь?",
                Opt("А «суп как у мамы» — правда?", 11),
                Opt("Вкусно пахнет. Пойду.", -1)),
            Node(11,
                "Правда! Маман делала луковый, когда я болел... Я вкус забыл, понимаешь? Забыл! Принесёшь звёздные овощи — серебро, золото — я вспомню. И тебя накормлю так, что нейроны запоют!",
                Opt("Принесу. Обещаю.", 0),
                Opt("Договорились.", -1)),
            Node(12,
                "А-а-а, ученик мэра! Пшеницу принёс? Две штуки — и будет тебе хлеб, тёплый, как маман пекла. Давай сюда!",
                Opt("Отдать 2 пшеницы", -1, DialogueActionType.Custom, "BakeBread"),
                Opt("Сейчас нет, потом", -1))
        );
    }

    // ── СТЕПАН (расширяем, ковка сохраняется) ──
    static void BuildBlacksmith()
    {
        var d = GetOrCreate("Blacksmith_Dialogue");
        Save(d, "Кузнец Степан", 0,
            Node(0,
                "Чего тебе, путник?",
                Opt("Выкуй мне вещь", 1, DialogueActionType.LeadToCraft),
                Opt("Степан, нужна работа", 12),
                Opt("Ты всегда такой молчаливый?", 10),
                Opt("Ничего", -1)),
            Node(1,
                "Пошли к станку. Там поговорим.",
                Opt("Иду.", -1)),
            Node(10,
                "Руки... болят. Раньше людей чинил. Маленьких. А теперь... молот. Железо не плачет.",
                Opt("Ты был врачом?", 11),
                Opt("Не буду лезть.", -1)),
            Node(11,
                "Хирургом. Детским. Тремор после аварии... Здесь кую, чтобы точность вернуть. Принесёшь обсидиан с глубины — выкую тебе меч. И... спасибо, что спросил.",
                Opt("Принесу обсидиан.", 0),
                Opt("Держись, Степан.", -1)),
            Node(12,
                "Работа? Есть одна. Под городом шахта, там руда. Мне нужна — тебе практика. Держи кирку, она уже в руках. Добудь 5 жил и возвращайся.",
                Opt("Давай кирку, и я пошёл!", -1, DialogueActionType.Custom, "GivePick"))
        );
    }

    // ── МАРТА (семена, магазин сохраняется) ──
    static void BuildSeedTrader()
    {
        var d = GetOrCreate("DialogueSeedTrader");
        Save(d, "Семенщица Марта", 0,
            Node(0,
                "Привет! Свежие семена с моей грядки — бери пока свежие! Ой, ты с девятого участка? Земля там добрая, я её помню...",
                Opt("Показать семена", -1, DialogueActionType.OpenShop, "Семена"),
                Opt("Что взять новичку?", 12),
                Opt("Расскажи про книгу прокачки", 13),
                Opt("Ты давно здесь, Марта?", 10),
                Opt("До встречи!", -1)),
            Node(10,
                "Давно, милый. Я тут каждый росточек как дитё... Ой, тыковка моя, смотри как лезет! В той жизни детей не случилось. А здесь — вон их сколько, грядок-то!",
                Opt("А что за история с 100 тыквами?", 11),
                Opt("Семена посмотрю.", -1, DialogueActionType.OpenShop, "Семена")),
            Node(11,
                "Хи-хи! Посадишь сто тыкв за сезон — открою тебе свои запасы: дыню, арбуз. По блату! Тыквы — они как дети, честно. Шумные, большие, всех радуют.",
                Opt("Замётано! Беру семена.", -1, DialogueActionType.OpenShop, "Семена"),
                Opt("Я подумаю.", -1)),
            Node(12,
                "Новичку? Пшеницу, милый, пшеницу! Растёт быстро, Дрон её берёт охотно. Возьми штуки три, посади, полей — через пару дней уже хлеб. А захочешь разнообразия — приходи, перки в книге качай, ассортимент откроется!",
                Opt("Беру пшеницу!", -1, DialogueActionType.OpenShop, "Семена"),
                Opt("Спасибо, Марта!", -1)),
            Node(13,
                "Книга-то? Ой, это главное сокровище Долины! За каждый уровень — 3 очка. Вкладки три: Бой, Ферма, Ремесло. Хочешь новые семена — качай ветку Фермы, там перки с замочками на культурах. Снаряжение из меди и выше — тоже за перками, вкладка своя. Понемногу, милый, не жадничай — очки не сгорают!",
                Opt("А где сама книга?", 14),
                Opt("Поняла, спасибо!", -1)),
            Node(14,
                "Ой... а вот где она открывается — хоть убей, не помню! Спроси у мэра, он точно знает. А лучше — сама потыкай кнопки возле рюкзака, она где-то там!",
                Opt("Ладно, поищу!", -1))
        );
    }

    // ── ДРОН (скупка сохраняется) ──
    static void BuildBuyer()
    {
        var d = GetOrCreate("DialogueBuyer");
        Save(d, "Скупщик Дрон", 0,
            Node(0,
                "Так-так-так! Новенький! С девятого? Урожай? Неси сюда! За востребованное плачу вдвое! Репутация у нас с тобой ещё ого-го будет!",
                Opt("Показать цены", -1, DialogueActionType.OpenSell),
                Opt("А ты сам кто, Дрон?", 10),
                Opt("До встречи!", -1)),
            Node(10,
                "Я? Ха! Я — лучший друг фермера! Беру всё: морковку, тыкву, вино, сыр... Слушай... тебе счётчик не врёт? 500 тысяч, ага... Ты считай, считай. А захочешь по-быстрому — заходи. Есть... варианты.",
                Opt("Что за варианты?", 11),
                Opt("Продать хочу.", -1, DialogueActionType.OpenSell)),
            Node(11,
                "Тс-с! Не здесь. Приходи, когда долг перевалит за половину. Поговорим про ядро, про шахту... про то, как проценты накручивают. А пока — неси урожай, неси! Я честно плачу. Пока честно.",
                Opt("Ладно... пока продам.", -1, DialogueActionType.OpenSell),
                Opt("Звучит мутно.", -1))
        );
    }

    // ── МОРЕК (удочка/продажа/коллекция сохраняются) ──
    static void BuildMorek()
    {
        var d = GetOrCreate("Morek");
        Save(d, "Морек", 0,
            Node(0,
                "Йо-хо! Заходи, рыбак. Море сегодня щедрое — проверь сам. Чего надо?",
                Opt("Взять удочку", -1, DialogueActionType.Custom, "GiveRod", "morek_norod"),
                Opt("Продать рыбу (+50%)", -1, DialogueActionType.Custom, "SellFish"),
                Opt("Коллекция", -1, DialogueActionType.Custom, "Collection"),
                Opt("Ты давно на пляже, дед?", 10),
                Opt("Пока!", -1)),
            Node(10,
                "Двенадцать лет, салага. Долг-то я давно закрыл... А куда мне? Жена умерла, дети в другом городе. А здесь — море, трубка и ты. Моречко, оно всё помнит...",
                Opt("А коллекция рыб — это что?", 11),
                Opt("Байку трави!", 12)),
            Node(11,
                "Дневник мой. 43 рыбки — 43 года... Каждую помню, где взял. Соберёшь всех — попрошу тебя об одном. Отпустить последнюю. И... уйти мне надо будет. А лодку тебе оставлю.",
                Opt("Соберу. Обещаю.", 0)),
            Node(12,
                "Во-о-от такую щуку видал! С дом! Не веришь? А зря! Она мне подмигнула и говорит: «Морек, ты опять врёшь!» Ха-ха-ха! Ладно, иди рыбачь, салага!",
                Opt("Ха! Пойду.", 0))
        );
    }

    // ── БОРИС (инструменты, минимум лора) ──
    static void BuildToolTrader()
    {
        var d = GetOrCreate("DialogueToolTrader");
        Save(d, "Торговец Борис", 0,
            Node(0,
                "Нужен инструмент? У меня всё надёжное, как земля! Кирки, топоры, молотки — бери, девятка без инструмента никуда.",
                Opt("Показать товар", -1, DialogueActionType.OpenShop, "Инструменты"),
                Opt("А мотыга для новичка найдётся?", 10),
                Opt("До встречи!", -1)),
            Node(10,
                "Для девятого участка — найдётся! Мэр уже черкнул мне. Держи совет: сперва камень в шахте бери, потом медь. Кирка твоя пока только камень и берёт. А захочешь лучше — к Степану, он за руду прокачает.",
                Opt("Покажи товар.", -1, DialogueActionType.OpenShop, "Инструменты"),
                Opt("Понял, спасибо!", -1))
        );
    }
}
