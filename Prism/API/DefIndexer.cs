﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Prism.API.Behaviours;
using Prism.API.Defs;
using Prism.Mods;
using Prism.Util;

namespace Prism.API
{
    public delegate bool DefIndTryGet<T>(ObjectRef or, ModDef requesting, out T ret);

    public struct DefIndexer<T> : IEnumerable<KeyValuePair<ObjectRef, T>>
    {
        Func<ObjectRef, ModDef, T> byObjRef;
        Func<int, T> byId;
        Func<int, T> byIdUnsf;
        DefIndTryGet<T> tryGet;

        IEnumerable<KeyValuePair<ObjectRef, T>> allDefs;

        public T this[int id]
        {
            get
            {
                return byId(id);
            }
        }
        public T this[ObjectRef objRef]
        {
            get
            {
                return byObjRef(objRef, ModData.ModFromAssembly(Assembly.GetCallingAssembly()));
            }
        }
        public T this[string internalName, string modName = null]
        {
            get
            {
                return byObjRef(new ObjectRef(internalName, modName), ModData.ModFromAssembly(Assembly.GetCallingAssembly()));
            }
        }
        public T this[string internalName, ModInfo mod]
        {
            get
            {
                return byObjRef(new ObjectRef(internalName, mod), ModData.ModFromAssembly(Assembly.GetCallingAssembly()));
            }
        }

        public T GetUnsafeFromID(int id)
        {
            return byIdUnsf(id);
        }

        public bool Has(ObjectRef rd)
        {
            T _;
            return tryGet(rd, ModData.ModFromAssembly(Assembly.GetCallingAssembly()), out _);
        }
        public bool TryGet(ObjectRef rd, out T ret)
        {
            return tryGet(rd, ModData.ModFromAssembly(Assembly.GetCallingAssembly()), out ret);
        }

        public IEnumerable<ObjectRef> Keys
        {
            get
            {
                return allDefs.Select(kvp => kvp.Key);
            }
        }
        public IEnumerable<T> Values
        {
            get
            {
                return allDefs.Select(kvp => kvp.Value);
            }
        }

        public DefIndexer(IEnumerable<KeyValuePair<ObjectRef, T>> allDefs,
                Func<ObjectRef, ModDef, T> byObjRef, Func<int, T> byId,
                Func<int, T> byIdUnsafe, DefIndTryGet<T> tryGet)
        {
            this.allDefs  = allDefs   ;
            this.byObjRef = byObjRef  ;
            this.byId     = byId      ;
            this.byIdUnsf = byIdUnsafe;
            this.tryGet   = tryGet    ;
        }

        public IEnumerator<KeyValuePair<ObjectRef, T>> GetEnumerator()
        {
            return allDefs.GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator()
        {
            return allDefs.GetEnumerator();
        }
    }
    public class DIHelper<T>
    {
        protected int maxIdValue;
        protected string objName;

        protected Func<ModDef, IDictionary<string, T>> GetModDefs;
        protected IDictionary<string, T> VanillaDefsByName;
        protected IDictionary<int, T> VanillaDefsById;

        public DIHelper(int maxIdValue, string objName, Func<ModDef, IDictionary<string, T>> getModDefs, IDictionary<string, T> vByName, IDictionary<int, T> vById)
        {
            this.maxIdValue = maxIdValue;
            this.objName    = objName;

            GetModDefs        = getModDefs;
            VanillaDefsByName = vByName;
            VanillaDefsById   = vById;
        }

        public T ByObjRef(ObjectRef or, ModDef requesting)
        {
            var req = requesting ?? or.requesting;
            T ret;

            if (String.IsNullOrEmpty(or.ModName) && req != null && GetModDefs(req).TryGetValue(or.Name, out ret))
                return ret;

            if (or.Mod == PrismApi.VanillaInfo)
            {
                if (VanillaDefsByName.TryGetValue(or.Name, out ret))
                    return ret;

                throw new InvalidOperationException("Vanilla " + objName + " definition '" + or.Name + "' is not found.");
            }

            ModDef md;

            if (!ModData.ModsFromInternalName.TryGetValue(or.ModName, out md))
                throw new InvalidOperationException(objName + " definition '" + or.Name + "' in mod '" + or.ModName + "' could not be returned because the mod is not loaded.");

            if (GetModDefs(md).TryGetValue(or.Name, out ret))
                return ret;

            throw new InvalidOperationException(objName + " definition '" + or.Name + "' in mod '" + or.ModName + "' could not be resolved because the " + objName + " is not loaded.");
        }
        public T ById(int id)
        {
            T r;
            if (id >= maxIdValue || !VanillaDefsById.TryGetValue(id, out r))
                throw new ArgumentOutOfRangeException("id", "The id must be a vanilla " + objName + " id.");

            return r;
        }
        public T ByIdUnsafe(int id)
        {
            return VanillaDefsById[id];
        }

        public bool TryGet(ObjectRef or, ModDef requesting, out T ret)
        {
            ret = default(T);

            var req = requesting ?? or.requesting;

            if (String.IsNullOrEmpty(or.ModName) && req != null && GetModDefs(req).TryGetValue(or.Name, out ret))
                return true;

            if (or.Mod == PrismApi.VanillaInfo)
                return VanillaDefsByName.TryGetValue(or.Name, out ret);

            ModDef md;
            if (!ModData.ModsFromInternalName.TryGetValue(or.ModName, out md))
               return false;

            return GetModDefs(md).TryGetValue(or.Name, out ret);
        }
    }
    public sealed class EntityDIH<TEntity, TBehaviour, TEntityDef> : DIHelper<TEntityDef>
        where TEntity : class
        where TBehaviour : EntityBehaviour<TEntity>
        where TEntityDef : EntityDef<TBehaviour, TEntity>
    {
        public EntityDIH(int maxIdValue, string objName, Func<ModDef, IDictionary<string, TEntityDef>> getModDefs, IDictionary<string, TEntityDef> vByName, IDictionary<int, TEntityDef> vById)
            : base(maxIdValue, objName, getModDefs, vByName, vById)
        {

        }

        public IEnumerable<KeyValuePair<ObjectRef, TEntityDef>> GetEnumerable()
        {
            // welcome to VERY GODDAMN VERBOSE functional programming
            // seriously, type inferrence FTW
            var vanillaDefs = VanillaDefsByName.Values.Select(id => new KeyValuePair<ObjectRef, TEntityDef>(new ObjectRef(id.InternalName), id));
            var modDefs = ModData.mods.Select(GetModDefsInUsefulFormat).Flatten();

            return vanillaDefs.Concat(modDefs);

            /*
             * let vanillaDefs = map toRefDefPair VanillaDefsByName
             * let modDefs     = mods ModData >>= GetModDefsInUsefulFormat
             * in vanillaDefs ++ modDefs
             *   where toRefDefPair id = ObjectRef $ InternalName id, id
             *         toModRDPair inf (n, v) = ObjectRef n inf, v
             *         GetModDefsInUsefulFormat (inf, def) =
             *             map (toModRDPair inf) $ getModDefs def
             */
        }
        IEnumerable<KeyValuePair<ObjectRef, TEntityDef>> GetModDefsInUsefulFormat(KeyValuePair<ModInfo, ModDef> kvp)
        {
            return GetModDefs(kvp.Value).SafeSelect(kvp_ => new KeyValuePair<ObjectRef, TEntityDef>(new ObjectRef(kvp_.Key, kvp.Key), kvp_.Value));
        }
    }
}
