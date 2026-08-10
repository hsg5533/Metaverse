using System;
using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server side persistence. Progress is keyed by nickname and written to a JSON file next to
/// the player data folder, so a session can be stopped and picked up again.
/// Nickname is the only identity here, which is fine for a LAN game and nothing more.
/// </summary>
public class SaveSystem : MonoBehaviour
{
    public float SaveInterval = 15f;

    /// <summary>
    /// One line of the bag. Named rather than numbered so the file stays readable and can be
    /// edited by hand; materials carry their count, a piece of gear is always one.
    /// </summary>
    [Serializable]
    class BagEntry
    {
        public string item;
        public int count = 1;
    }

    [Serializable]
    class Record
    {
        public string name;
        public int level = 1;
        public int exp;
        public int gold;
        public int weapon;
        public int armor;
        public int hp = -1;
        public int quest = -1;
        public int questProgress;
        public int duelWins;
        public int duelLosses;
        public string gearWeapon = "";
        public string gearArmor = "";

        // What the player picked at the mirror. -1 keeps whatever they were given on the way in.
        public int bodyTint = -1;
        public int pantsTint = -1;
        public int hairTint = -1;
        public int hairStyle = -1;
        public List<BagEntry> bag = new();
    }

    [Serializable]
    class Book
    {
        public List<Record> players = new();
    }

    static SaveSystem instance;

    Book book = new();
    float nextSave;

    public const string FileName = "metaverse-save.json";

    static string FilePath => Path.Combine(RootFolder, FileName);

    /// <summary>
    /// The project folder in the editor, and the folder holding the executable in a build:
    /// both are the parent of Application.dataPath. Keeping the save here means it sits next
    /// to the game instead of hiding under AppData.
    /// A phone has no such folder - the app is installed read-only - so it uses the one place
    /// the system does let it write.
    /// </summary>
    static string RootFolder
    {
        get
        {
            if (Application.isMobilePlatform)
            {
                return Application.persistentDataPath;
            }

            var parent = Directory.GetParent(Application.dataPath);
            return parent != null ? parent.FullName : Application.persistentDataPath;
        }
    }

    void Awake()
    {
        instance = this;
        Load();
    }

    void OnDestroy()
    {
        if (instance == this)
        {
            SaveAll();
            instance = null;
        }
    }

    void OnApplicationQuit()
    {
        SaveAll();
    }

    void Update()
    {
        var manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsServer || Time.time < nextSave)
        {
            return;
        }

        nextSave = Time.time + SaveInterval;
        SaveAll();
    }

    /// <summary>
    /// Server side: called once the nickname is known, which is the key everything hangs off.
    /// </summary>
    public static void LoadInto(PlayerAvatar avatar)
    {
        if (instance == null || avatar == null)
        {
            return;
        }

        Record record = instance.Find(avatar.Nickname.Value.ToString());
        if (record == null)
        {
            return;
        }

        if (record.bodyTint >= 0)
        {
            avatar.SetLook(record.bodyTint, record.pantsTint, record.hairTint, record.hairStyle);
        }

        var stats = avatar.GetComponent<PlayerStats>();
        if (stats != null)
        {
            stats.Level.Value = Mathf.Max(1, record.level);
            stats.Exp.Value = Mathf.Max(0, record.exp);
            stats.Gold.Value = Mathf.Max(0, record.gold);
            stats.WeaponLevel.Value = Mathf.Max(0, record.weapon);
            stats.ArmorLevel.Value = Mathf.Max(0, record.armor);
            stats.Hp.Value = record.hp > 0 ? Mathf.Min(record.hp, stats.MaxHp) : stats.MaxHp;
            stats.RestoreDuels(record.duelWins, record.duelLosses);
        }

        // The bag holds both: stacks of material and pieces of gear, told apart by name.
        var counts = new int[PlayerInventory.Slots.Length];
        var pieces = new List<int>();
        foreach (var entry in record.bag)
        {
            int material = PlayerInventory.IndexOf(entry.item);
            if (material >= 0)
            {
                counts[material] += Mathf.Max(0, entry.count);
                continue;
            }

            int piece = PlayerGear.IndexOf(entry.item);
            for (int i = 0; i < Mathf.Max(1, entry.count) && piece >= 0; i++)
            {
                pieces.Add(piece);
            }
        }



        var quests = avatar.GetComponent<PlayerQuests>();
        if (quests != null)
        {
            quests.Restore(record.quest, record.questProgress);
        }

        // Gear first: restoring it empties the bag, and the stacks put their markers back in.
        var gear = avatar.GetComponent<PlayerGear>();
        if (gear != null)
        {
            gear.Restore(PlayerGear.IndexOf(record.gearWeapon), PlayerGear.IndexOf(record.gearArmor), pieces);
        }

        var inventory = avatar.GetComponent<PlayerInventory>();
        if (inventory != null)
        {
            inventory.SetAll(counts[0], counts[1], counts[2]);
        }

        Debug.Log($"[Metaverse] loaded save for {record.name} (Lv.{record.level}, {record.gold} G)");
    }

    void SaveAll()
    {
        var manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsServer)
        {
            return;
        }

        foreach (var client in manager.ConnectedClientsList)
        {
            var player = client.PlayerObject;
            if (player == null)
            {
                continue;
            }

            var avatar = player.GetComponent<PlayerAvatar>();
            var stats = player.GetComponent<PlayerStats>();
            if (avatar == null || stats == null)
            {
                continue;
            }

            string name = avatar.Nickname.Value.ToString();
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            Record record = Find(name);
            if (record == null)
            {
                record = new Record { name = name };
                book.players.Add(record);
            }

            record.level = stats.Level.Value;
            record.exp = stats.Exp.Value;
            record.gold = stats.Gold.Value;
            record.weapon = stats.WeaponLevel.Value;
            record.armor = stats.ArmorLevel.Value;
            record.hp = stats.Hp.Value;
            record.duelWins = stats.DuelWins.Value;
            record.duelLosses = stats.DuelLosses.Value;

            record.bodyTint = avatar.BodyTint.Value;
            record.pantsTint = avatar.PantsTint.Value;
            record.hairTint = avatar.HairTint.Value;
            record.hairStyle = avatar.HairStyle.Value;

            record.bag.Clear();

            var inventory = player.GetComponent<PlayerInventory>();
            if (inventory != null)
            {
                for (int i = 0; i < PlayerInventory.Slots.Length; i++)
                {
                    int count = inventory.CountOf(i);
                    if (count > 0)
                    {
                        record.bag.Add(new BagEntry { item = PlayerInventory.Slots[i], count = count });
                    }
                }
            }

            var quests = player.GetComponent<PlayerQuests>();
            if (quests != null)
            {
                record.quest = quests.Quest.Value;
                record.questProgress = quests.Progress.Value;
            }

            var gear = player.GetComponent<PlayerGear>();
            if (gear != null)
            {
                record.gearWeapon = PlayerGear.NameOf(gear.Weapon.Value);
                record.gearArmor = PlayerGear.NameOf(gear.Armor.Value);
                foreach (int piece in gear.Bag)
                {
                    // Negative entries are material-carried markers, not gear; the materials
                    // themselves are already saved above from the inventory counts.
                    if (piece < 0)
                    {
                        continue;
                    }

                    record.bag.Add(new BagEntry { item = PlayerGear.Pieces[piece].Name });
                }
            }
        }

        Write();
    }

    Record Find(string name)
    {
        foreach (var record in book.players)
        {
            if (record.name == name)
            {
                return record;
            }
        }

        return null;
    }

    void Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                book = JsonUtility.FromJson<Book>(File.ReadAllText(FilePath)) ?? new Book();
                Debug.Log($"[Metaverse] save file loaded: {FilePath} ({book.players.Count} players)");
            }
        }
        catch (Exception error)
        {
            Debug.LogWarning($"[Metaverse] could not read the save file: {error.Message}");
            book = new Book();
        }
    }

    void Write()
    {
        try
        {
            File.WriteAllText(FilePath, JsonUtility.ToJson(book, true));
        }
        catch (Exception error)
        {
            Debug.LogWarning($"[Metaverse] could not write the save file: {error.Message}");
        }
    }
}
