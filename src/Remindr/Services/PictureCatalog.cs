namespace Remindr.Services;

public enum AvatarCategory { People, FamilyAndRoles, Animals, FantasyAndFun, Food, Nature, SportsAndHobbies, Objects }

public enum ReminderCategory { School, Work, Health, Home, Food, Money, Travel, Celebrations, Sports }

/// <summary>One built-in picture (Fluent UI Emoji, see THIRD-PARTY-NOTICES.md). Either group may be null when it isn't offered for that use.</summary>
public sealed record PictureEntry(string Key, string Label, string[] Tags, AvatarCategory? AvatarGroup, ReminderCategory? ReminderGroup)
{
    /// <summary>Value stored in profiles.avatar / reminders.icon.</summary>
    public string Value => PictureCatalog.BuiltinPrefix + Key;
}

/// <summary>
/// The single shared picture library (Assets/Pictures/&lt;key&gt;.png). Pure data, no UI types.
/// AvatarCatalog and the reminder picker are filters over it. Generated from docs/pictures-manifest.csv;
/// keys are file names without .png and must never be renamed (stored values are 'builtin:&lt;key&gt;').
/// </summary>
public static class PictureCatalog
{
    public const string BuiltinPrefix = "builtin:";

    private static readonly (string Key, AvatarCategory? Avatar, ReminderCategory? Reminder)[] Table =
    {
        ("bear", AvatarCategory.Animals, null),
        ("butterfly", AvatarCategory.Animals, null),
        ("dolphin", AvatarCategory.Animals, null),
        ("fox", AvatarCategory.Animals, null),
        ("frog", AvatarCategory.Animals, null),
        ("hamster", AvatarCategory.Animals, null),
        ("hatching_chick", AvatarCategory.Animals, null),
        ("hedgehog", AvatarCategory.Animals, null),
        ("koala", AvatarCategory.Animals, null),
        ("lion", AvatarCategory.Animals, null),
        ("monkey_face", AvatarCategory.Animals, null),
        ("mouse_face", AvatarCategory.Animals, null),
        ("otter", AvatarCategory.Animals, null),
        ("owl", AvatarCategory.Animals, null),
        ("panda", AvatarCategory.Animals, null),
        ("parrot", AvatarCategory.Animals, null),
        ("penguin", AvatarCategory.Animals, null),
        ("pig_face", AvatarCategory.Animals, null),
        ("rabbit_face", AvatarCategory.Animals, null),
        ("sloth", AvatarCategory.Animals, null),
        ("tiger_face", AvatarCategory.Animals, null),
        ("turtle", AvatarCategory.Animals, null),
        ("artist", AvatarCategory.FamilyAndRoles, null),
        ("astronaut", AvatarCategory.FamilyAndRoles, null),
        ("cook", AvatarCategory.FamilyAndRoles, null),
        ("detective", AvatarCategory.FamilyAndRoles, null),
        ("farmer", AvatarCategory.FamilyAndRoles, null),
        ("firefighter", AvatarCategory.FamilyAndRoles, null),
        ("pilot", AvatarCategory.FamilyAndRoles, null),
        ("scientist", AvatarCategory.FamilyAndRoles, null),
        ("singer", AvatarCategory.FamilyAndRoles, null),
        ("technologist", AvatarCategory.FamilyAndRoles, null),
        ("alien", AvatarCategory.FantasyAndFun, null),
        ("cowboy_hat_face", AvatarCategory.FantasyAndFun, null),
        ("dragon_face", AvatarCategory.FantasyAndFun, null),
        ("ghost", AvatarCategory.FantasyAndFun, null),
        ("nerd_face", AvatarCategory.FantasyAndFun, null),
        ("ninja", AvatarCategory.FantasyAndFun, null),
        ("prince", AvatarCategory.FantasyAndFun, null),
        ("princess", AvatarCategory.FantasyAndFun, null),
        ("robot", AvatarCategory.FantasyAndFun, null),
        ("sauropod", AvatarCategory.FantasyAndFun, null),
        ("smiling_face_with_sunglasses", AvatarCategory.FantasyAndFun, null),
        ("unicorn", AvatarCategory.FantasyAndFun, null),
        ("baby", AvatarCategory.People, null),
        ("boy", AvatarCategory.People, null),
        ("child", AvatarCategory.People, null),
        ("girl", AvatarCategory.People, null),
        ("man", AvatarCategory.People, null),
        ("man_beard", AvatarCategory.People, null),
        ("man_red_hair", AvatarCategory.People, null),
        ("old_man", AvatarCategory.People, null),
        ("old_woman", AvatarCategory.People, null),
        ("older_person", AvatarCategory.People, null),
        ("person", AvatarCategory.People, null),
        ("person_white_hair", AvatarCategory.People, null),
        ("woman", AvatarCategory.People, null),
        ("woman_curly_hair", AvatarCategory.People, null),
        ("cat_face", AvatarCategory.Animals, ReminderCategory.Home),
        ("dog_face", AvatarCategory.Animals, ReminderCategory.Home),
        ("health_worker", AvatarCategory.FamilyAndRoles, ReminderCategory.Health),
        ("student", AvatarCategory.FamilyAndRoles, ReminderCategory.School),
        ("teacher", AvatarCategory.FamilyAndRoles, ReminderCategory.School),
        ("beaver", AvatarCategory.Animals, null),
        ("black_cat", AvatarCategory.Animals, null),
        ("chicken", AvatarCategory.Animals, null),
        ("cow_face", AvatarCategory.Animals, null),
        ("crab", AvatarCategory.Animals, null),
        ("deer", AvatarCategory.Animals, null),
        ("duck", AvatarCategory.Animals, null),
        ("eagle", AvatarCategory.Animals, null),
        ("elephant", AvatarCategory.Animals, null),
        ("flamingo", AvatarCategory.Animals, null),
        ("giraffe", AvatarCategory.Animals, null),
        ("gorilla", AvatarCategory.Animals, null),
        ("honeybee", AvatarCategory.Animals, null),
        ("horse_face", AvatarCategory.Animals, null),
        ("kangaroo", AvatarCategory.Animals, null),
        ("lady_beetle", AvatarCategory.Animals, null),
        ("leopard", AvatarCategory.Animals, null),
        ("llama", AvatarCategory.Animals, null),
        ("octopus", AvatarCategory.Animals, null),
        ("peacock", AvatarCategory.Animals, null),
        ("polar_bear", AvatarCategory.Animals, null),
        ("poodle", AvatarCategory.Animals, null),
        ("raccoon", AvatarCategory.Animals, null),
        ("spouting_whale", AvatarCategory.Animals, null),
        ("swan", AvatarCategory.Animals, null),
        ("tropical_fish", AvatarCategory.Animals, null),
        ("wolf", AvatarCategory.Animals, null),
        ("zebra", AvatarCategory.Animals, null),
        ("baby_angel", AvatarCategory.FamilyAndRoles, null),
        ("construction_worker", AvatarCategory.FamilyAndRoles, null),
        ("factory_worker", AvatarCategory.FamilyAndRoles, null),
        ("man_astronaut", AvatarCategory.FamilyAndRoles, null),
        ("man_firefighter", AvatarCategory.FamilyAndRoles, null),
        ("man_health_worker", AvatarCategory.FamilyAndRoles, null),
        ("man_pilot", AvatarCategory.FamilyAndRoles, null),
        ("man_scientist", AvatarCategory.FamilyAndRoles, null),
        ("man_teacher", AvatarCategory.FamilyAndRoles, null),
        ("mechanic", AvatarCategory.FamilyAndRoles, null),
        ("mrs_claus", AvatarCategory.FamilyAndRoles, null),
        ("office_worker", AvatarCategory.FamilyAndRoles, null),
        ("people_hugging", AvatarCategory.FamilyAndRoles, null),
        ("police_officer", AvatarCategory.FamilyAndRoles, null),
        ("pregnant_woman", AvatarCategory.FamilyAndRoles, null),
        ("santa_claus", AvatarCategory.FamilyAndRoles, null),
        ("woman_astronaut", AvatarCategory.FamilyAndRoles, null),
        ("woman_firefighter", AvatarCategory.FamilyAndRoles, null),
        ("woman_health_worker", AvatarCategory.FamilyAndRoles, null),
        ("woman_pilot", AvatarCategory.FamilyAndRoles, null),
        ("woman_scientist", AvatarCategory.FamilyAndRoles, null),
        ("woman_teacher", AvatarCategory.FamilyAndRoles, null),
        ("alien_monster", AvatarCategory.FantasyAndFun, null),
        ("dragon", AvatarCategory.FantasyAndFun, null),
        ("phoenix_bird", AvatarCategory.FantasyAndFun, null),
        ("rocket", AvatarCategory.FantasyAndFun, null),
        ("t_rex", AvatarCategory.FantasyAndFun, null),
        ("teddy_bear", AvatarCategory.FantasyAndFun, null),
        ("woman_fairy", AvatarCategory.FantasyAndFun, null),
        ("woman_mage", AvatarCategory.FantasyAndFun, null),
        ("woman_merpeople", AvatarCategory.FantasyAndFun, null),
        ("woman_superhero", AvatarCategory.FantasyAndFun, null),
        ("cookie", AvatarCategory.Food, null),
        ("cupcake", AvatarCategory.Food, null),
        ("doughnut", AvatarCategory.Food, null),
        ("french_fries", AvatarCategory.Food, null),
        ("ice_cream", AvatarCategory.Food, null),
        ("popcorn", AvatarCategory.Food, null),
        ("strawberry", AvatarCategory.Food, null),
        ("cactus", AvatarCategory.Nature, null),
        ("cherry_blossom", AvatarCategory.Nature, null),
        ("evergreen_tree", AvatarCategory.Nature, null),
        ("four_leaf_clover", AvatarCategory.Nature, null),
        ("maple_leaf", AvatarCategory.Nature, null),
        ("rainbow", AvatarCategory.Nature, null),
        ("sun_with_face", AvatarCategory.Nature, null),
        ("sunflower", AvatarCategory.Nature, null),
        ("camera", AvatarCategory.Objects, null),
        ("crown", AvatarCategory.Objects, null),
        ("gem_stone", AvatarCategory.Objects, null),
        ("headphone", AvatarCategory.Objects, null),
        ("sunglasses", AvatarCategory.Objects, null),
        ("face_with_tears_of_joy", AvatarCategory.People, null),
        ("grinning_face", AvatarCategory.People, null),
        ("grinning_face_with_big_eyes", AvatarCategory.People, null),
        ("hugging_face", AvatarCategory.People, null),
        ("man_bald", AvatarCategory.People, null),
        ("man_blonde_hair", AvatarCategory.People, null),
        ("man_curly_hair", AvatarCategory.People, null),
        ("man_white_hair", AvatarCategory.People, null),
        ("saluting_face", AvatarCategory.People, null),
        ("smiling_face_with_halo", AvatarCategory.People, null),
        ("smiling_face_with_heart_eyes", AvatarCategory.People, null),
        ("smiling_face_with_smiling_eyes", AvatarCategory.People, null),
        ("star_struck", AvatarCategory.People, null),
        ("thinking_face", AvatarCategory.People, null),
        ("winking_face", AvatarCategory.People, null),
        ("woman_bald", AvatarCategory.People, null),
        ("woman_blonde_hair", AvatarCategory.People, null),
        ("woman_red_hair", AvatarCategory.People, null),
        ("woman_white_hair", AvatarCategory.People, null),
        ("artist_palette", AvatarCategory.SportsAndHobbies, null),
        ("guitar", AvatarCategory.SportsAndHobbies, null),
        ("video_game", AvatarCategory.SportsAndHobbies, null),
        ("hamburger", AvatarCategory.Food, ReminderCategory.Food),
        ("pizza", AvatarCategory.Food, ReminderCategory.Food),
        ("red_apple", AvatarCategory.Food, ReminderCategory.Food),
        ("balloon", AvatarCategory.Objects, ReminderCategory.Celebrations),
        ("light_bulb", AvatarCategory.Objects, ReminderCategory.Home),
        ("partying_face", AvatarCategory.People, ReminderCategory.Celebrations),
        ("baseball", AvatarCategory.SportsAndHobbies, ReminderCategory.Sports),
        ("basketball", AvatarCategory.SportsAndHobbies, ReminderCategory.Sports),
        ("soccer_ball", AvatarCategory.SportsAndHobbies, ReminderCategory.Sports),
        ("tennis", AvatarCategory.SportsAndHobbies, ReminderCategory.Sports),
        ("volleyball", AvatarCategory.SportsAndHobbies, ReminderCategory.Sports),
        ("birthday_cake", null, ReminderCategory.Celebrations),
        ("bouquet", null, ReminderCategory.Celebrations),
        ("christmas_tree", null, ReminderCategory.Celebrations),
        ("confetti_ball", null, ReminderCategory.Celebrations),
        ("fireworks", null, ReminderCategory.Celebrations),
        ("party_popper", null, ReminderCategory.Celebrations),
        ("red_heart", null, ReminderCategory.Celebrations),
        ("ring", null, ReminderCategory.Celebrations),
        ("sparkler", null, ReminderCategory.Celebrations),
        ("wedding", null, ReminderCategory.Celebrations),
        ("wrapped_gift", null, ReminderCategory.Celebrations),
        ("bread", null, ReminderCategory.Food),
        ("carrot", null, ReminderCategory.Food),
        ("cooking", null, ReminderCategory.Food),
        ("egg", null, ReminderCategory.Food),
        ("fork_and_knife", null, ReminderCategory.Food),
        ("glass_of_milk", null, ReminderCategory.Food),
        ("green_salad", null, ReminderCategory.Food),
        ("hot_beverage", null, ReminderCategory.Food),
        ("shopping_cart", null, ReminderCategory.Food),
        ("adhesive_bandage", null, ReminderCategory.Health),
        ("anatomical_heart", null, ReminderCategory.Health),
        ("droplet", null, ReminderCategory.Health),
        ("glasses", null, ReminderCategory.Health),
        ("hospital", null, ReminderCategory.Health),
        ("person_getting_haircut", null, ReminderCategory.Health),
        ("person_in_lotus_position", null, ReminderCategory.Health),
        ("pill", null, ReminderCategory.Health),
        ("stethoscope", null, ReminderCategory.Health),
        ("syringe", null, ReminderCategory.Health),
        ("thermometer", null, ReminderCategory.Health),
        ("tooth", null, ReminderCategory.Health),
        ("toothbrush", null, ReminderCategory.Health),
        ("bathtub", null, ReminderCategory.Home),
        ("bed", null, ReminderCategory.Home),
        ("broom", null, ReminderCategory.Home),
        ("couch_and_lamp", null, ReminderCategory.Home),
        ("house", null, ReminderCategory.Home),
        ("house_with_garden", null, ReminderCategory.Home),
        ("key", null, ReminderCategory.Home),
        ("potted_plant", null, ReminderCategory.Home),
        ("soap", null, ReminderCategory.Home),
        ("toolbox", null, ReminderCategory.Home),
        ("wastebasket", null, ReminderCategory.Home),
        ("bank", null, ReminderCategory.Money),
        ("coin", null, ReminderCategory.Money),
        ("credit_card", null, ReminderCategory.Money),
        ("dollar_banknote", null, ReminderCategory.Money),
        ("euro_banknote", null, ReminderCategory.Money),
        ("money_bag", null, ReminderCategory.Money),
        ("money_with_wings", null, ReminderCategory.Money),
        ("receipt", null, ReminderCategory.Money),
        ("shopping_bags", null, ReminderCategory.Money),
        ("abacus", null, ReminderCategory.School),
        ("backpack", null, ReminderCategory.School),
        ("blue_book", null, ReminderCategory.School),
        ("books", null, ReminderCategory.School),
        ("graduation_cap", null, ReminderCategory.School),
        ("memo", null, ReminderCategory.School),
        ("microscope", null, ReminderCategory.School),
        ("notebook", null, ReminderCategory.School),
        ("open_book", null, ReminderCategory.School),
        ("pencil", null, ReminderCategory.School),
        ("school", null, ReminderCategory.School),
        ("straight_ruler", null, ReminderCategory.School),
        ("american_football", null, ReminderCategory.Sports),
        ("bowling", null, ReminderCategory.Sports),
        ("boxing_glove", null, ReminderCategory.Sports),
        ("flag_in_hole", null, ReminderCategory.Sports),
        ("person_lifting_weights", null, ReminderCategory.Sports),
        ("person_running", null, ReminderCategory.Sports),
        ("person_swimming", null, ReminderCategory.Sports),
        ("ping_pong", null, ReminderCategory.Sports),
        ("trophy", null, ReminderCategory.Sports),
        ("airplane", null, ReminderCategory.Travel),
        ("automobile", null, ReminderCategory.Travel),
        ("beach_with_umbrella", null, ReminderCategory.Travel),
        ("bicycle", null, ReminderCategory.Travel),
        ("bus", null, ReminderCategory.Travel),
        ("camping", null, ReminderCategory.Travel),
        ("fuel_pump", null, ReminderCategory.Travel),
        ("high_speed_train", null, ReminderCategory.Travel),
        ("hotel", null, ReminderCategory.Travel),
        ("luggage", null, ReminderCategory.Travel),
        ("passport_control", null, ReminderCategory.Travel),
        ("taxi", null, ReminderCategory.Travel),
        ("ticket", null, ReminderCategory.Travel),
        ("world_map", null, ReminderCategory.Travel),
        ("alarm_clock", null, ReminderCategory.Work),
        ("briefcase", null, ReminderCategory.Work),
        ("calendar", null, ReminderCategory.Work),
        ("chart_increasing", null, ReminderCategory.Work),
        ("clipboard", null, ReminderCategory.Work),
        ("envelope", null, ReminderCategory.Work),
        ("file_folder", null, ReminderCategory.Work),
        ("laptop", null, ReminderCategory.Work),
        ("megaphone", null, ReminderCategory.Work),
        ("mobile_phone", null, ReminderCategory.Work),
        ("necktie", null, ReminderCategory.Work),
        ("paperclip", null, ReminderCategory.Work),
        ("printer", null, ReminderCategory.Work),
        ("pushpin", null, ReminderCategory.Work),
        ("tear_off_calendar", null, ReminderCategory.Work),
        ("telephone_receiver", null, ReminderCategory.Work),
    };
    /// <summary>All pictures in display order.</summary>
    public static IReadOnlyList<PictureEntry> All { get; } =
        Table.Select(t => new PictureEntry(t.Key, Label(t.Key), MakeTags(t.Key, t.Avatar, t.Reminder), t.Avatar, t.Reminder)).ToList();

    private static readonly Dictionary<string, PictureEntry> ByKey = All.ToDictionary(e => e.Key);

    public static PictureEntry? Find(string? key) => key is not null && ByKey.TryGetValue(key, out var e) ? e : null;

    /// <summary>Resolves 'builtin:&lt;key&gt;' to an entry, or null for null/unknown/other values.</summary>
    public static PictureEntry? FromValue(string? value) =>
        value is not null && value.StartsWith(BuiltinPrefix, StringComparison.Ordinal) ? Find(value.Substring(BuiltinPrefix.Length)) : null;

    public static IEnumerable<PictureEntry> ForAvatars() => All.Where(e => e.AvatarGroup is not null);
    public static IEnumerable<PictureEntry> ForReminders() => All.Where(e => e.ReminderGroup is not null);

    /// <summary>"smiling_face_with_sunglasses" becomes "Smiling Face With Sunglasses".</summary>
    public static string Label(string key) =>
        System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(key.Replace('_', ' '));

    /// <summary>Display name of a category in the current language, e.g. FamilyAndRoles becomes "Family and roles".</summary>
    public static string CategoryLabel(AvatarCategory c) => Loc.Get("PictureCat_" + c);

    public static string CategoryLabel(ReminderCategory c) => Loc.Get("PictureCat_" + c);

    // Search tags stay English (and lower case) like the picture labels, whatever the UI language is.
    private static string EnglishName(AvatarCategory c) => c switch
    {
        AvatarCategory.FamilyAndRoles => "family and roles",
        AvatarCategory.FantasyAndFun => "fantasy and fun",
        AvatarCategory.SportsAndHobbies => "sports and hobbies",
        _ => c.ToString().ToLowerInvariant()
    };

    private static string[] MakeTags(string key, AvatarCategory? a, ReminderCategory? r)
    {
        var tags = key.Split('_').Where(w => w.Length > 0).ToList();
        if (a is { } ac) tags.Add(EnglishName(ac));
        if (r is { } rc) tags.Add(rc.ToString().ToLowerInvariant());
        return tags.Distinct().ToArray();
    }
    /// <summary>Case-insensitive match of every search word against the label and tags. Empty text matches everything.</summary>
    public static bool Matches(PictureEntry e, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        foreach (var w in search.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (e.Label.Contains(w, StringComparison.OrdinalIgnoreCase)) continue;
            if (e.Tags.Any(t => t.Contains(w, StringComparison.OrdinalIgnoreCase))) continue;
            return false;
        }
        return true;
    }
}

