using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Prism.Mods;
using Prism.Mods.DefHandlers;
using Prism.Util;
using Terraria;
using Terraria.ID;

namespace Prism.API.Defs
{
    public class TileRef : EntityRefWithId<TileDef>
    {
        static string ToResName(int id)
        {
            TileDef td = null;
            if (Handler.TileDef.DefsByType.TryGetValue(id, out td))
                return td.InternalName;

            string r = null;
            if (Handler.TileDef.IDLUT.TryGetValue(id, out r))
                return r;

            throw new ArgumentException("id", "Unknown Tile ID '" + id + "'.");
        }

        public TileRef(int resourceId)
            : base(resourceId, ToResName)
        {
            if (unchecked((uint)resourceId) >= unchecked((uint)TileID.Count))
                throw new ArgumentOutOfRangeException("resourceId", "The resourceId must be a vanilla Tile type (" + resourceId + "/" + TileID.Count + ").");
        }
        public TileRef(ObjectRef objRef)
            : base(objRef, Assembly.GetCallingAssembly())
        {

        }
        public TileRef(string resourceName, ModInfo mod)
            : base(new ObjectRef(resourceName, mod), Assembly.GetCallingAssembly())
        {

        }
        public TileRef(string resourceName, string modName = null)
            : base(new ObjectRef(resourceName, modName, Assembly.GetCallingAssembly()), Assembly.GetCallingAssembly())
        {

        }

        [ThreadStatic]
        static TileDef td;
        TileRef(int resourceId, object ignore)
            : base(resourceId, id => Handler.TileDef.DefsByType.TryGetValue(id, out td) ? td.InternalName : String.Empty)
        {

        }

        public static TileRef FromIDUnsafe(int resourceId)
        {
            return new TileRef(resourceId, null);
        }

        public override TileDef Resolve()
        {
            TileDef r;

            if (ResourceID.HasValue && Handler.TileDef.DefsByType.TryGetValue(ResourceID.Value, out r))
                return r;

            if (String.IsNullOrEmpty(ModName) && Requesting != null && Requesting.TileDefs.TryGetValue(ResourceName, out r))
                return r;

            if (IsVanillaRef)
            {
                if (!Handler.TileDef.VanillaDefsByName.TryGetValue(ResourceName, out r))
                    throw new InvalidOperationException("Vanilla tile reference '" + ResourceName + "' is not found.");

                return r;
            }

            ModDef m;
            if (!ModData.ModsFromInternalName.TryGetValue(ModName, out m))
                throw new InvalidOperationException("Tile reference '" + ResourceName + "' in mod '" + ModName + "' could not be resolved because the mod is not loaded.");
            if (!m.TileDefs.TryGetValue(ResourceName, out r))
                throw new InvalidOperationException("Tile reference '" + ResourceName + "' in mod '" + ModName + "' could not be resolved because the tile is not loaded.");

            return r;
        }

        public static implicit operator Either<TileRef, CraftGroup<TileDef, TileRef>>(TileRef r)
        {
            return Either<TileRef, CraftGroup<TileDef, TileRef>>.NewRight(r);
        }
        public static implicit operator Either<TileRef, TileGroup>(TileRef r)
        {
            return Either<TileRef, TileGroup>.NewRight(r);
        }

        public static implicit operator TileRef(Tile t)
        {
            if (t.type < TileID.Count)
                return new TileRef(t.type);

            TileDef d;
            if (Handler.TileDef.DefsByType.TryGetValue(t.type, out d))
                return d;

            throw new InvalidOperationException("Tile '" + t + "' (" + t.type + ") is not in the def database.");
        }
    }
}
