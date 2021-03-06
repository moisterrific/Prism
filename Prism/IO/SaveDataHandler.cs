﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Ionic.Zlib;
using Prism.API;
using Prism.API.Defs;
using Prism.Debugging;
using Prism.Mods;
using Prism.Mods.BHandlers;
using Prism.Mods.DefHandlers;
using Prism.Mods.Hooks;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.IO;

namespace Prism.IO
{
    /*
     * On compression:
     * * a better compression ratio would be achieved if data was reordered
     *   (see http://www.farbrausch.com/~fg/seminars/workcompression_download.pdf )
     * * Write2DArray and Read2DArray require seeking: fix this first
     *   (or write it in a way that it doesn't require copying another binbuffer
     *    -- maybe just drop the ModIdMap altogether?)
     * * using compression makes writing slow af (worse than bearable, not just
     *   'slow'); are there faster alternatives to gzip?
     *
     * ~~A player Prism file is compressed, though. (Not that it matters much,
     * compared to a world file.)~~ gzip is giving bad CRC errors for some
     * unknown reason...
     */

    /// <summary>
    /// Class containing the hooks used by Prism when saving and/or loading Players and Worlds.
    /// </summary>
    static class SaveDataHandler
    {
        [Flags]
        enum TileDataEntryFlags : byte
        {
            Finished = 0,
            Tile     = 1,
            Type     = 2
        }

        /// <summary>
        /// Save file version for .plr.prism files. Change whenever the format changes, and make checks in the loading code for backwards compatibility. Please add a changelog entry when changing.
        /// </summary>
        /// <remarks>
        /// VERSION 0: created
        /// VERSION 1: [breaking] small header changes
        /// VERSION 2: [breaking] removed encryption, removed redundant data, ~~added compression~~
        /// </remarks>
        const byte PLAYER_VERSION = 2;
        /// <summary>
        /// Save file version for .wld.prism files. Change whenever the format changes, and make checks in the loading code for backwards compatibility. Please add a changelog entry when changing.
        /// </summary>
        /// <remarks>
        /// VERSION 0: created
        /// VERSION 1: added wall type support, fixed some issues
        /// VERSION 2: added tile type support
        /// VERSION 3: added TileBehaviour support
        /// VERSION 4: rewritten TileBehaviour support, so it's not a dirty hack anymore.
        /// VERSION 5: [breaking] removed encryption, removed redundant data, ~~added compression~~
        /// </remarks>
        const byte WORLD_VERSION  = 5;

        const byte
            MIN_PLAYER_SUPPORT_VER = 2,
            MIN_WORLD_SUPPORT_VER  = 5;

        /// <summary>
        /// Save <paramref name="slots" /> items from <paramref name="inventory" /> to the <paramref name="bb" />.
        /// </summary>
        /// <param name="bb">The writer for storing data</param>
        /// <param name="inventory">The array of items</param>
        /// <param name="slots">The amount of items in the inventory to save</param>
        static void SaveItemSlots(BinBuffer bb, Item[] inventory, int slots = 0)
        {
            if (slots <= 0) slots = inventory.Length;

            for (int i = 0; i < slots; i++)
                if (inventory[i].type < ItemID.Count)
                    bb.Write(String.Empty); // write an empty string instead of 'Vanilla'
                else
                {
                    // Save basic item data
                    ItemDef item = Handler.ItemDef.DefsByType[inventory[i].type];

                    bb.Write(item.Mod.InternalName);
                    bb.Write(item.InternalName);
                }

            // Save Mod Data
            // this is done in a separate loop to decrease shannon entropy,
            // and thus to make the file compress better
            for (int i = 0; i < slots; i++)
                if (inventory[i].P_BHandler != null)
                {
                    ItemBHandler handler = (ItemBHandler)inventory[i].P_BHandler;

                    handler.Save(bb);
                }
                else bb.Write(0);
        }
        /// <summary>
        /// Load <paramref name="slots" /> items to <paramref name="inventory" /> from the <paramref name="bb" />.
        /// </summary>
        /// <param name="bb">The reader for loading data</param>
        /// <param name="inventory">The array of items</param>
        /// <param name="slots">The amount of items in the inventory to load</param>
        static void LoadItemSlots(BinBuffer bb, Item[] inventory, int slots = 0)
        {
            if (slots <= 0) slots = inventory.Length;

            for (int i = 0; i < slots; i++)
            {
                // Load basic item data
                string mod = bb.ReadString();
                if (!String.IsNullOrEmpty(mod) && mod != PrismApi.VanillaString)
                {
                    string item = bb.ReadString();

                    ModDef md;
                    ItemDef id;
                    if (!ModData.modsFromInternalName.TryGetValue(mod, out md) || !md.ItemDefs.TryGetValue(item, out id))
                    {
                        inventory[i].SetDefaults(ItemDefHandler.UnknownItemID);

                        inventory[i].ToolTip = new[] { (ObjectName)mod, (ObjectName)item }.ToTooltip();
                    }
                    else inventory[i].SetDefaults(id.Type);
                }
            }

            for (int i = 0; i < slots; i++)
            {
                // Load Mod Data
                if (inventory[i].P_BHandler == null)
                {
                    inventory[i].P_BHandler = new ItemBHandler();

                    ((ItemBHandler)inventory[i].P_BHandler).Create();
                }

                ((ItemBHandler)inventory[i].P_BHandler).Load(bb);
            }
        }

        /// <summary>
        /// Save mod data to a .plr.prism file
        /// </summary>
        /// <param name="playerFile">The player being saved</param>
        internal static void SavePlayer(PlayerFileData playerFile)
        {
            string path = playerFile.Path;
            Player player = playerFile.Player;

            if (Main.ServerSideCharacter || String.IsNullOrEmpty(path))
                return;

            path += ".prism";

            if (File.Exists(path))
                File.Copy(path, playerFile.Path + ".bak.prism", true);

            using (FileStream fileStream = new FileStream(path, FileMode.Create))
            {
                fileStream.WriteByte(PLAYER_VERSION); // write this before doing the crypto stuff, so we can change it between versions

                //using (GZipStream zs = new GZipStream(fileStream, CompressionMode.Compress, CompressionLevel.Level9, false))
                using (BinBuffer bb = new BinBuffer(fileStream, dispose: false))
                {
                    #region Player Data
                    if (player.P_BHandler != null)
                    {
                        var bh = (PlayerBHandler)player.P_BHandler;

                        bh.Save(bb);
                    }
                    else bb.Write(0);
                    #endregion Player Data

                    #region Item Data
                    SaveItemSlots(bb, player.armor, player.armor.Length);
                    SaveItemSlots(bb, player.dye, player.dye.Length);
                    SaveItemSlots(bb, player.inventory, Main.maxInventory);
                    SaveItemSlots(bb, player.miscEquips, player.miscEquips.Length);
                    SaveItemSlots(bb, player.bank.item, Chest.maxItems);
                    SaveItemSlots(bb, player.bank2.item, Chest.maxItems);
                    SaveItemSlots(bb, player.bank3.item, Chest.maxItems);
                    #endregion Item Data

                    #region Buff Data
                    for (int i = 0; i < Player.maxBuffs; i++)
                    {
                        if (Main.buffNoSave[player.buffType[i]] || player.buffType[i] < BuffID.Count || player.buffTime[i] <= 0)
                            bb.Write(String.Empty);
                        else
                        {
                            var buff = Handler.BuffDef.DefsByType[player.buffType[i]];

                            bb.Write(buff.Mod.InternalName);
                            bb.Write(buff.InternalName);
                        }

                        if (player.P_BuffBHandler[i] != null)
                        {
                            var bh = (BuffBHandler)player.P_BuffBHandler[i];

                            bh.Save(bb);
                        }
                        else bb.Write(0);
                    }
                    #endregion Buff Data
                }
            }
        }
        /// <summary>
        /// Load player data from a .plr.prism file
        /// </summary>
        /// <param name="playerPath">The path to the vanilla .plr file</param>
        internal static void LoadPlayer(Player player, string playerPath)
        {
            playerPath += ".prism";

            // If mod data doesn't exist, don't try to load it
            if (!File.Exists(playerPath))
                return;

            try
            {
                using (FileStream fileStream = File.OpenRead(playerPath))
                {
                    byte version = (byte)fileStream.ReadByte(); // should be safe to cast here, very unlikely that the file is empty
                    if (version > PLAYER_VERSION)
                        throw new FileFormatException("Tried to load a player file from a future version of Prism.");
                    if (version < MIN_PLAYER_SUPPORT_VER)
                        throw new FileFormatException("This player is saved in a format that is too old and unsupported.");

                    //using (GZipStream zs = new GZipStream(fileStream, CompressionMode.Decompress, CompressionLevel.Level9, false))
                    using (BinBuffer bb = new BinBuffer(fileStream))
                    {
                        #region Player Data
                        if (player.P_BHandler == null)
                        {
                            player.P_BHandler = new PlayerBHandler();

                            ((PlayerBHandler)player.P_BHandler).Create();
                        }

                        ((PlayerBHandler)player.P_BHandler).Load(bb);
                        #endregion Player Data

                        #region Item Data
                        LoadItemSlots(bb, player.armor, player.armor.Length);
                        LoadItemSlots(bb, player.dye, player.dye.Length);
                        LoadItemSlots(bb, player.inventory, Main.maxInventory);
                        LoadItemSlots(bb, player.miscEquips, player.miscEquips.Length);
                        LoadItemSlots(bb, player.bank.item, Chest.maxItems);
                        LoadItemSlots(bb, player.bank2.item, Chest.maxItems);
                        LoadItemSlots(bb, player.bank3.item, Chest.maxItems);
                        #endregion Item Data

                        #region Buff Data
                        for (int i = 0; i < Player.maxBuffs; i++)
                        {
                            var mod = bb.ReadString();

                            ModDef md;
                            if (String.IsNullOrEmpty(mod) || !ModData.modsFromInternalName.TryGetValue(mod, out md))
                                continue;

                            var buff = bb.ReadString();
                            var t = bb.ReadInt32();

                            if (!md.BuffDefs.ContainsKey(buff))
                                continue;

                            player.AddBuff(md.BuffDefs[buff].Type, t);

                            if (player.P_BuffBHandler[i] == null)
                            {
                                player.P_BuffBHandler[i] = new BuffBHandler();

                                ((BuffBHandler)player.P_BuffBHandler[i]).Create();
                            }

                            ((BuffBHandler)player.P_BuffBHandler[i]).Save(bb);
                        }
                        #endregion Buff Data
                    }
                }
            }
            catch (Exception e)
            {
                // Character could not be properly loaded, report and prevent playing
                //TODO: report in UI
                Logging.LogError("Could not load player " + player.name + ": " + e);
                Trace.WriteLine ("Could not load player " + player.name + ": " + e.Message);
                player.loadStatus = 1;
            }
        }

        static void Write2DArray(BinBuffer bb, ModIdMap map, int xLen, int yLen, Func  <int, int, bool> isEmpty , Func  <int, int, int      > getElemV, Func<int, int, ObjectRef> getElemM)
        {
            var ov = 0;
            var ot = ObjectRef.Null;
            bool isOV = true;

            int amt = 0;
            bool once = true;

            int dictOffsetPosition = bb.Position;
            bb.Write(0); // dictionary position

            for (int y = 0; y < yLen; y++)
                for (int x = 0; x < xLen; x++)
                {
                    var e = isEmpty(x, y);
                    var v = e ? 0 : getElemV(x, y);
                    var t = ObjectRef.Null;
                    var isV = e || v > 0;

                    if (!e && v == 0)
                    {
                        t = getElemM(x, y);
                        isV = false;
                    }

                    if (once)
                    {
                        if (isV) ov = v;
                        else     ot = t;
                        isOV = isV;

                        bb.Write((uint)(isV ? map.Register(v) : map.Register(t)));

                        once = false;
                    }
                    else if (isV == isOV && (isV ? v == ov : t == ot) && amt < UInt16.MaxValue)
                        amt++;
                    else
                    {
                        bb.Write((ushort)amt); // write the amount of successing elements of the same type,
                                               // instead of the type over and over again, to save some space
                        amt = 0; // amt == 0 -> one element

                        if (isV) ov = v;
                        else     ot = t;
                        isOV = isV;

                        bb.Write((uint)(isV ? map.Register(v) : map.Register(t)));
                    }
                }

            bb.Write((ushort)amt); // write final amt

            var afterData = bb.Position;
            bb.Position = dictOffsetPosition;
            bb.Write(afterData); // dictionary position
            bb.Position = afterData;

            map.WriteDictionary(bb);
        }
        static void Read2DArray (BinBuffer bb, ModIdMap map, int xLen, int yLen, Action<int, int, int > setElemV, Action<int, int, ObjectRef> setElemM)
        {
            var dictPos = bb.ReadInt32();
            var dataStart = bb.Position;
            bb.Position = dictPos;

            map.ReadDictionary(bb);

            var endOfStream = bb.Position;

            bb.Position = dataStart;

            int amt = 0;

            bool isM = false;
            ObjectRef curM = ObjectRef.Null;
            int curV = 0;

            for (int y = 0; y < yLen; y++)
                for (int x = 0; x < xLen; x++)
                    if (amt == 0)
                    {
                        map.GetRef(bb.ReadUInt32(),
                            oid =>
                            {
                                curV = oid;
                                isM = false;

                                setElemV(x, y, curV); // amt == 0 -> one element
                            },
                            or  =>
                            {
                                curM = or;
                                isM = true;

                                setElemM(x, y, curM); // amt == 0 -> one element
                            });
                        amt = bb.ReadUInt16();
                    }
                    else
                    {
                        if (isM) setElemM(x, y, curM);
                        else     setElemV(x, y, curV);

                        amt--;
                    }

            bb.Position = endOfStream;
        }

        static void SaveGlobalData(BinBuffer bb)
        {
            HookManager.GameBehaviour.Save(bb);
        }
        static void SaveTileTypes (BinBuffer bb)
        {
            var map = new ModIdMap(TileID.Count, or => TileDef.Defs[or].Type, id => Handler.TileDef.DefsByType[id]);

            Write2DArray(bb, map, Main.maxTilesX, Main.maxTilesY,
                (x, y) => Main.tile[x, y] == null || Main.tile[x, y].type <= 0, // 0 -> dirt wall, but this works here, too
                (x, y) => Main.tile[x, y].type >= TileID.Count ? 0 : Main.tile[x, y].type,
                (x, y) =>
                {
                    TileDef d;
                    return Main.tile[x, y].type < TileID.Count || !Handler.TileDef.DefsByType.TryGetValue(Main.tile[x, y].type, out d) ? ObjectRef.Null : d;
                });
        }
        static void SaveChestItems(BinBuffer bb)
        {
            var chests = Main.chest.Where(c => c != null);

            bb.Write((short)chests.Count());
            bb.WriteByte(Chest.maxItems);

            foreach (var c in chests)
                SaveItemSlots(bb, c.item, Chest.maxItems);
        }
        static void SaveNpcData   (BinBuffer bb)
        {
            for (int i = 0; i < Main.npc.Length; i++)
            {
                var n = Main.npc[i];
                if (n == null || !n.active || !n.townNPC || n.type == NPCID.TravellingMerchant || n.type == NPCID.SkeletonMerchant)
                    continue;

                bb.Write(true);
                if (n.P_BHandler == null)
                    bb.Write(0); // as length for the IOBHandler
                else
                {
                    var bh = n.P_BHandler as NpcBHandler;

                    bh.Save(bb);
                }
            }
            //bb.Write(false); // don't write, loops can be unified in the Load method (only mod data is written)

            // blame red for the double loop. see Terraria.IO.WorldFile.SaveNPCs/LoadNPCs
            for (int i = 0; i < Main.npc.Length; i++)
            {
                var n = Main.npc[i];
                if (n == null || !n.active || !NPCID.Sets.SavesAndLoads[n.type] || (n.townNPC && n.type != NPCID.TravellingMerchant && n.type != NPCID.SkeletonMerchant))
                    continue;

                bb.Write(true);
                if (n.P_BHandler == null)
                    bb.Write(0); // as length for the IOBHandler
                else
                {
                    var bh = n.P_BHandler as NpcBHandler;

                    bh.Save(bb);
                }
            }
            bb.Write(false);
        }
        static void SaveWallTypes (BinBuffer bb)
        {
            var map = new ModIdMap(WallID.Count, or => WallDef.Defs[or].Type, id => Handler.WallDef.DefsByType[id]);

            Write2DArray(bb, map, Main.maxTilesX, Main.maxTilesY,
                (x, y) => Main.tile[x, y] == null || Main.tile[x, y].wall <= 0,
                (x, y) => Main.tile[x, y].wall >= unchecked((ushort)WallID.Count) ? 0 : Main.tile[x, y].wall,
                (x, y) =>
                {
                    WallDef d;
                    return Main.tile[x, y].wall <  unchecked((ushort)WallID.Count)
                                || !Handler.WallDef.DefsByType.TryGetValue(Main.tile[x, y].wall, out d)
                            ? ObjectRef.Null : d;
                });
        }
        static void SaveTileData  (BinBuffer bb)
        {
            for (int y = 0; y < Main.maxTilesY; y++)
                for (int x = 0; x < Main.maxTilesX; x++)
                {
                    var p = new Point16(x, y);
                    var t = Main.tile[x, y].type;

                    var fl = TileDataEntryFlags.Finished;

                    if (TileHooks.TileSpecificHandlers.ContainsKey(p))
                        fl |= TileDataEntryFlags.Tile;
                    if (TileHooks.TypeSpecificHandlers.ContainsKey(t))
                        fl |= TileDataEntryFlags.Type;

                    if (fl == TileDataEntryFlags.Finished)
                        continue;

                    bb.Write((byte)fl);
                    bb.Write(p.X);
                    bb.Write(p.Y);

                    if ((fl & TileDataEntryFlags.Tile) != 0)
                    {
                        var bh = TileHooks.TileSpecificHandlers[p];

                        bh.Save(bb);
                    }
                    if ((fl & TileDataEntryFlags.Type) != 0)
                    {
                        var bh = TileHooks.TypeSpecificHandlers[t];

                        foreach (var b in bh.behaviours)
                            b.Position = p;

                        bh.Save(bb);
                    }
                }

            bb.Write(0);
        }

        internal static void SaveWorld(bool toCloud)
        {
            var path = Main.worldPathName + ".prism";

            if (File.Exists(path))
                File.Copy(path, Main.worldPathName + ".bak.prism", true);

            using (FileStream fs = File.OpenWrite(path))
            {
                fs.WriteByte(WORLD_VERSION);

                //using (GZipStream zs = new GZipStream(fs, CompressionMode.Compress, CompressionLevel.Level9, false))
                using (BinBuffer bb = new BinBuffer(fs/*zs*/, dispose: false))
                {
                    //TODO: item frame item IDs are still written as ints
                    //TODO: mannequins are probably broken

                    Main.statusText = "Saving Prism data: global data";
                    SaveGlobalData(bb);
                    Main.statusText = "Saving Prism data: chests";
                    SaveChestItems(bb);
                    Main.statusText = "Saving Prism data: NPCs";
                    SaveNpcData   (bb);
                    Main.statusText = "Saving Prism data: walls";
                    SaveWallTypes (bb);
                    Main.statusText = "Saving Prism data: tile IDs";
                    SaveTileTypes (bb);
                    Main.statusText = "Saving Prism data: tile data";
                    SaveTileData  (bb);
                }
            }
        }

        static void LoadGlobalData(BinBuffer bb, int v)
        {
            HookManager.GameBehaviour.Load(bb);
        }
        static void LoadTileTypes (BinBuffer bb, int v)
        {
            var map = new ModIdMap(TileID.Count, or => TileDef.Defs[or].Type, id => Handler.TileDef.DefsByType[id]);

            Read2DArray(bb, map, Main.maxTilesX, Main.maxTilesY, (x, y, id) => Main.tile[x, y].type = (ushort)id, (x, y, or) => Main.tile[x, y].type = (ushort)TileDef.Defs[or].Type);
        }
        // TODO: make this faster (it's the slowest part of Load)
        static void LoadChestItems(BinBuffer bb, int v)
        {
            int chestAmt = bb.ReadInt16();
            int items    = bb.ReadByte ();

            for (int i = 0; i < chestAmt; i++)
                LoadItemSlots(bb, Main.chest[i].item, items);
        }
        static void LoadNpcData   (BinBuffer bb, int v)
        {
            int i = 0;
            while (bb.ReadBoolean())
            {
                NPC n = Main.npc[i++];

                if (n.P_BHandler == null)
                    n.P_BHandler = new NpcBHandler();

                ((NpcBHandler)n.P_BHandler).Load(bb);
            }
        }
        static void LoadWallTypes (BinBuffer bb, int v)
        {
            var map = new ModIdMap(WallID.Count, or => WallDef.Defs[or].Type, id => Handler.WallDef.DefsByType[id]);

            Read2DArray(bb, map, Main.maxTilesX, Main.maxTilesY,
                    (x, y, id) => Main.tile[x, y].wall = (ushort)id,
                    (x, y, or) => Main.tile[x, y].wall = (ushort)WallDef.Defs[or].Type);
        }
        static void LoadTileData  (BinBuffer bb, int v)
        {
            TileDataEntryFlags fl;
            while ((fl = (TileDataEntryFlags)bb.ReadByte()) != TileDataEntryFlags.Finished)
            {
                var x = bb.ReadInt16();
                var y = bb.ReadInt16();
                var p = new Point16(x, y);
                var t = Main.tile[x, y].type;

                var bs = TileHooks.CreateBHandler(p);

                if ((fl & TileDataEntryFlags.Tile) != 0)
                {
                    var bh = bs.Item1;

                    foreach (var b in bh.behaviours)
                    {
                        b.HasTile  = true;
                        b.Position = p;
                    }

                    bh.Load(bb);
                }
                if ((fl & TileDataEntryFlags.Type) != 0)
                {
                    var bh = bs.Item2;

                    foreach (var b in bh.behaviours)
                    {
                        b.HasTile = true;
                        b.Position = p;
                    }

                    bh.Load(bb);
                }
            }
        }

        [Obsolete]
        static void LoadBehaviours(BinBuffer bb, int v)
        {
            while (bb.ReadBoolean())
            {
                bb.ReadInt16();
                bb.ReadInt16();
            }
        }

        internal static void LoadWorld(bool fromCloud)
        {
            var path = Main.worldPathName + ".prism";

            if (!File.Exists(path))
                return;

            Main.statusText = "Loading Prism data...";

            using (FileStream fs = File.OpenRead(path))
            {
                var v = fs.ReadByte();

                if (v > WORLD_VERSION)
                    throw new FileFormatException("Tried to load world file from a future version of Prism.");
                if (v < MIN_WORLD_SUPPORT_VER)
                    throw new FileFormatException("This world is saved in a format that is too old and unsupported.");

                //using (GZipStream zs = new GZipStream(fs, CompressionMode.Decompress, CompressionLevel.Level9, false))
                using (BinBuffer bb = new BinBuffer(fs/*zs*/, dispose: false))
                {
                    Main.statusText = "Loading Prism data: global data";
                    LoadGlobalData(bb, v);
                    Main.statusText = "Loading Prism data: chests";
                    LoadChestItems(bb, v);
                    Main.statusText = "Loading Prism data: NPCs";
                    LoadNpcData   (bb, v);
                    Main.statusText = "Loading Prism data: walls";
                    LoadWallTypes (bb, v);
                    Main.statusText = "Loading Prism data: tile IDs";
                    LoadTileTypes (bb, v);
#pragma warning disable 612
                    // TileHooks.CreateBHandlers(); // after all tiles have their correct type

                    Main.statusText = "Loading Prism data: tile code";
                    LoadBehaviours(bb, v); // after the bhandlers are created (i.e. can load)
#pragma warning restore 612
                    Main.statusText = "Loading Prism data: tile data";
                    LoadTileData  (bb, v);
                }
            }
        }
    }
}
