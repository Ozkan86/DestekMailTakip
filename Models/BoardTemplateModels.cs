namespace task_list.Models;

/// <summary>
/// Yeni pano şablonları (Klasik dışındakiler) için jenerik kart ekleme/taşıma
/// izin motoru. Klasik şablonun kendi sabit kodlanmış akışı (BoardController/
/// BoardRepository'deki AddCard/MoveToTest/Approve/Reject) bu modelden
/// bağımsız olarak aynen çalışmaya devam eder; buradaki BoardTemplates.All["klasik"]
/// girdisi yalnızca liste anahtarlarının jenerik doğrulayıcılarda (IsValidListKey,
/// şablon seçici ekranı) tanınması için tutulur.
/// </summary>
public enum BoardAddCardRole
{
    None,
    Customer,
    Engineer
}

public enum BoardMoveResult
{
    Success,
    NotFound,
    InvalidTransition,
    NoteRequired,

    /// <summary>
    /// Mühendis rolündeki bir geçiş için kart kimseye atanmamış ya da başka bir
    /// mühendise atanmış (Admin haricinde, sadece atanan mühendis taşıyabilir).
    /// </summary>
    RequiresAssignment
}

public class BoardListDefinition
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string IconSvg { get; set; } = string.Empty;
    public BoardAddCardRole AddCardRole { get; set; } = BoardAddCardRole.None;
}

public class BoardListTransition
{
    /// <summary>"*" tum listelerden gelen tasimalari kabul eder (orn. Rejected/On Hold).</summary>
    public string FromListKey { get; set; } = string.Empty;
    public string ToListKey { get; set; } = string.Empty;
    public BoardAddCardRole AllowedRole { get; set; } = BoardAddCardRole.Engineer;
    public bool RequiresNote { get; set; }
    public bool IsApproval { get; set; }
    public bool IsRejection { get; set; }
}

public class BoardTemplateDefinition
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<BoardListDefinition> Lists { get; set; } = new();
    public List<BoardListTransition> Transitions { get; set; } = new();
    public bool HasSprintRounds { get; set; }

    public BoardListDefinition? GetList(string? key) =>
        Lists.FirstOrDefault(l => string.Equals(l.Key, key, StringComparison.Ordinal));

    public BoardListTransition? FindTransition(string fromListKey, string toListKey, BoardAddCardRole actorRole) =>
        Transitions.FirstOrDefault(t =>
            (t.FromListKey == "*" || string.Equals(t.FromListKey, fromListKey, StringComparison.Ordinal)) &&
            string.Equals(t.ToListKey, toListKey, StringComparison.Ordinal) &&
            t.AllowedRole == actorRole);
}

public static class BoardTemplates
{
    public const string Klasik = "klasik";
    public const string SoftwareProjectManagement = "software-project-management";
    public const string KanbanDevBoard = "kanban-devboard";
    public const string SiteReliability = "site-reliability";
    public const string SoftwareDevelopment = "software-development";
    public const string MusteriSeffaflik = "musteri-seffaflik";
    public const string HibritGelistirme = "hibrit-gelistirme";

    // Yeni sablonlardaki tum listeler icin ortak, notrsuz bir liste ikonu.
    private const string DefaultIcon = "<path d=\"M8 6h13\"/><path d=\"M8 12h13\"/><path d=\"M8 18h13\"/><path d=\"M3 6h.01\"/><path d=\"M3 12h.01\"/><path d=\"M3 18h.01\"/>";

    private static BoardListDefinition L(string key, string label, BoardAddCardRole role) =>
        new() { Key = key, Label = label, IconSvg = DefaultIcon, AddCardRole = role };

    private static BoardListTransition T(string from, string to, BoardAddCardRole role, bool requiresNote = false, bool isApproval = false, bool isRejection = false) =>
        new() { FromListKey = from, ToListKey = to, AllowedRole = role, RequiresNote = requiresNote, IsApproval = isApproval, IsRejection = isRejection };

    public static readonly Dictionary<string, BoardTemplateDefinition> All = new()
    {
        [Klasik] = new BoardTemplateDefinition
        {
            Key = Klasik,
            Name = "Klasik",
            Description = "Yapılacaklar, Test ve Tamamlanan listelerinden oluşan standart 3 kolonlu akış.",
            Lists = new()
            {
                L(BoardLists.Todo, "Yapılacaklar", BoardAddCardRole.Customer),
                L(BoardLists.Test, "Test", BoardAddCardRole.None),
                L(BoardLists.Done, "Tamamlanan", BoardAddCardRole.None)
            },
            Transitions = new()
        },

        [SoftwareProjectManagement] = new BoardTemplateDefinition
        {
            Key = SoftwareProjectManagement,
            Name = "Software Project Management",
            Description = "Proje tanımından UAT onayı ve dokümantasyona uzanan uçtan uca yazılım proje yönetimi akışı.",
            Lists = new()
            {
                L("about-project", "About Project", BoardAddCardRole.Customer),
                L("requirement-discussion", "Requirement Discussion", BoardAddCardRole.Engineer),
                L("approved-requests", "Approved Requests", BoardAddCardRole.None),
                L("work-in-progress", "Work in Progress", BoardAddCardRole.None),
                L("unit-testing", "Unit Testing", BoardAddCardRole.None),
                L("integration-testing", "Integration Testing", BoardAddCardRole.None),
                L("uat-review", "UAT Review", BoardAddCardRole.None),
                L("documentation-review", "Documentation Review", BoardAddCardRole.None),
                L("done", "Done", BoardAddCardRole.None)
            },
            Transitions = new()
            {
                T("requirement-discussion", "approved-requests", BoardAddCardRole.Engineer),
                T("approved-requests", "work-in-progress", BoardAddCardRole.Engineer),
                T("work-in-progress", "unit-testing", BoardAddCardRole.Engineer),
                T("unit-testing", "integration-testing", BoardAddCardRole.Engineer),
                T("integration-testing", "uat-review", BoardAddCardRole.Engineer),
                T("uat-review", "documentation-review", BoardAddCardRole.Customer, isApproval: true),
                T("uat-review", "requirement-discussion", BoardAddCardRole.Customer, requiresNote: true, isRejection: true),
                T("documentation-review", "done", BoardAddCardRole.Engineer)
            }
        },

        [KanbanDevBoard] = new BoardTemplateDefinition
        {
            Key = KanbanDevBoard,
            Name = "Kanban DevBoard",
            Description = "Talepten canlıya; backlog, geliştirme, code review ve test aşamalarını içeren klasik Kanban geliştirme akışı.",
            Lists = new()
            {
                L("requirements", "Requirements", BoardAddCardRole.Customer),
                L("backlog", "Backlog", BoardAddCardRole.Engineer),
                L("committed-backlog", "Committed Backlog", BoardAddCardRole.None),
                L("dev", "Dev", BoardAddCardRole.None),
                L("dev-done", "Dev Done", BoardAddCardRole.None),
                L("code-review", "Code Review", BoardAddCardRole.None),
                L("code-review-done", "Code Review Done", BoardAddCardRole.None),
                L("testing", "Testing", BoardAddCardRole.None),
                L("testing-done", "Testing Done", BoardAddCardRole.None),
                L("deployed", "Deployed", BoardAddCardRole.None),
                L("done", "Done", BoardAddCardRole.None)
            },
            Transitions = new()
            {
                T("requirements", "backlog", BoardAddCardRole.Engineer),
                T("backlog", "committed-backlog", BoardAddCardRole.Engineer),
                T("committed-backlog", "dev", BoardAddCardRole.Engineer),
                T("dev", "dev-done", BoardAddCardRole.Engineer),
                T("dev-done", "code-review", BoardAddCardRole.Engineer),
                T("code-review", "code-review-done", BoardAddCardRole.Engineer),
                T("code-review-done", "testing", BoardAddCardRole.Engineer),
                T("testing", "testing-done", BoardAddCardRole.Engineer),
                T("testing-done", "deployed", BoardAddCardRole.Engineer),
                T("deployed", "done", BoardAddCardRole.Customer, isApproval: true),
                T("deployed", "backlog", BoardAddCardRole.Customer, requiresNote: true, isRejection: true)
            }
        },

        [SiteReliability] = new BoardTemplateDefinition
        {
            Key = SiteReliability,
            Name = "Site Reliability",
            Description = "Planlama ve sorun bildirimlerinden prod onayına; ayrıca bağımsız bir tekrarlayan bakım listesi içeren SRE akışı.",
            Lists = new()
            {
                L("planning", "Planning", BoardAddCardRole.Customer),
                L("issues-and-requests", "Issues and Requests", BoardAddCardRole.Customer),
                L("next-up", "Next Up", BoardAddCardRole.None),
                L("doing", "Doing", BoardAddCardRole.None),
                L("in-code-review", "In Code Review", BoardAddCardRole.None),
                L("staging", "Staging", BoardAddCardRole.None),
                L("prod", "Prod", BoardAddCardRole.None),
                L("done", "Done", BoardAddCardRole.None),
                L("recurring", "Recurring", BoardAddCardRole.Engineer)
            },
            Transitions = new()
            {
                T("planning", "next-up", BoardAddCardRole.Engineer),
                T("issues-and-requests", "next-up", BoardAddCardRole.Engineer),
                T("next-up", "doing", BoardAddCardRole.Engineer),
                T("doing", "in-code-review", BoardAddCardRole.Engineer),
                T("in-code-review", "staging", BoardAddCardRole.Engineer),
                T("staging", "prod", BoardAddCardRole.Engineer),
                T("prod", "done", BoardAddCardRole.Customer, isApproval: true),
                T("prod", "issues-and-requests", BoardAddCardRole.Customer, requiresNote: true, isRejection: true)
            }
        },

        [SoftwareDevelopment] = new BoardTemplateDefinition
        {
            Key = SoftwareDevelopment,
            Name = "Software Development",
            Description = "Sprint turları halinde çalışan akış: her tur Requests'ten başlar, Sprint Done'daki her karta tek tek onay/red verilmesiyle biter.",
            HasSprintRounds = true,
            Lists = new()
            {
                L("requests", "Requests", BoardAddCardRole.Customer),
                L("backlog", "Backlog", BoardAddCardRole.None),
                L("sprint-backlog", "Sprint Backlog", BoardAddCardRole.None),
                L("working-on-bugs", "Working on Bugs", BoardAddCardRole.None),
                L("testing", "Testing", BoardAddCardRole.None),
                L("sprint-done", "Sprint Done", BoardAddCardRole.None)
            },
            Transitions = new()
            {
                T("requests", "backlog", BoardAddCardRole.Engineer),
                T("backlog", "sprint-backlog", BoardAddCardRole.Engineer),
                T("sprint-backlog", "working-on-bugs", BoardAddCardRole.Engineer),
                T("working-on-bugs", "testing", BoardAddCardRole.Engineer),
                T("testing", "sprint-done", BoardAddCardRole.Engineer)
            }
        },

        [MusteriSeffaflik] = new BoardTemplateDefinition
        {
            Key = MusteriSeffaflik,
            Name = "Müşteri Odaklı Şeffaflık Panosu",
            Description = "Müşteri talebinden canlıya; müşterinin önizleme aşamasında onay/red verdiği şeffaf geliştirme akışı.",
            Lists = new()
            {
                L("customer-requests", "Customer Requests", BoardAddCardRole.Customer),
                L("under-review", "Under Review", BoardAddCardRole.None),
                L("approved-planned", "Approved & Planned", BoardAddCardRole.None),
                L("in-development", "In Development", BoardAddCardRole.None),
                L("in-testing", "In Testing", BoardAddCardRole.None),
                L("customer-preview", "Customer Preview", BoardAddCardRole.None),
                L("live", "Live", BoardAddCardRole.None),
                L("rejected-on-hold", "Rejected/On Hold", BoardAddCardRole.None)
            },
            Transitions = new()
            {
                T("customer-requests", "under-review", BoardAddCardRole.Engineer),
                T("under-review", "approved-planned", BoardAddCardRole.Engineer),
                T("approved-planned", "in-development", BoardAddCardRole.Engineer),
                T("in-development", "in-testing", BoardAddCardRole.Engineer),
                T("in-testing", "customer-preview", BoardAddCardRole.Engineer),
                T("customer-preview", "live", BoardAddCardRole.Customer, isApproval: true),
                T("customer-preview", "under-review", BoardAddCardRole.Customer, requiresNote: true, isRejection: true),
                T("*", "rejected-on-hold", BoardAddCardRole.Engineer)
            }
        },

        [HibritGelistirme] = new BoardTemplateDefinition
        {
            Key = HibritGelistirme,
            Name = "Hibrit Geliştirme Panosu",
            Description = "Kanban DevBoard'ın teknik akışını korurken, canlıya almadan önce zorunlu müşteri onay adımı ekleyen hibrit akış.",
            Lists = new()
            {
                L("customer-requests", "Customer Requests", BoardAddCardRole.Customer),
                L("requirement-discussion", "Requirement Discussion", BoardAddCardRole.None),
                L("backlog", "Backlog", BoardAddCardRole.None),
                L("dev", "Dev", BoardAddCardRole.None),
                L("code-review", "Code Review", BoardAddCardRole.None),
                L("testing", "Testing", BoardAddCardRole.None),
                L("customer-approval", "Customer Approval", BoardAddCardRole.None),
                L("deployed", "Deployed", BoardAddCardRole.None),
                L("done", "Done", BoardAddCardRole.None)
            },
            Transitions = new()
            {
                T("customer-requests", "requirement-discussion", BoardAddCardRole.Engineer),
                T("requirement-discussion", "backlog", BoardAddCardRole.Engineer),
                T("backlog", "dev", BoardAddCardRole.Engineer),
                T("dev", "code-review", BoardAddCardRole.Engineer),
                T("code-review", "testing", BoardAddCardRole.Engineer),
                T("testing", "customer-approval", BoardAddCardRole.Engineer),
                T("customer-approval", "deployed", BoardAddCardRole.Customer, isApproval: true),
                T("customer-approval", "requirement-discussion", BoardAddCardRole.Customer, requiresNote: true, isRejection: true),
                T("deployed", "done", BoardAddCardRole.Engineer)
            }
        }
    };

    public static BoardTemplateDefinition Get(string? key) =>
        key is not null && All.TryGetValue(key, out var template) ? template : All[Klasik];

    public static bool IsValidKey(string? key) => key is not null && All.ContainsKey(key);

    /// <summary>
    /// Kart su an oturdugu listede HALA mühendis aksiyonu bekliyor mu? Yani o
    /// listeden mühendis rolüyle yapilabilecek en az bir gecis var mi?
    /// Klasik sablonda bu her zaman Yapilacaklar listesidir (Test'e tasindiktan
    /// sonra sira musteride oldugu icin kart mühendisin isi sayilmaz).
    ///
    /// "Pano Görevlerim" listesi (BoardController.MyTasks) ve "İstatistiklerim >
    /// Panolar > Bana Ait" sayaci ayni tanimi kullanmak zorunda; ikisinin farkli
    /// sayi gostermemesi icin kural burada tek noktada tutulur.
    /// </summary>
    public static bool HasPendingEngineerAction(string? templateKey, string? listKey)
    {
        if (string.Equals(templateKey, Klasik, StringComparison.Ordinal))
        {
            return string.Equals(listKey, BoardLists.Todo, StringComparison.Ordinal);
        }

        var template = Get(templateKey);
        if (template.GetList(listKey) is null)
        {
            return false;
        }

        return template.Transitions.Any(t =>
            (t.FromListKey == "*" || string.Equals(t.FromListKey, listKey, StringComparison.Ordinal)) &&
            t.AllowedRole == BoardAddCardRole.Engineer);
    }
}
