﻿using System;
using System.Collections.Generic;
using System.Linq;
using Prism.API.Behaviours;
using Prism.Mods.BHandlers;
using Prism.Util;
using Terraria;
using Terraria.Localization;
using Terraria.UI;

namespace Prism.API
{
    public static class Extensions
    {
        static TWanted GetBehaviour<TBehaviour, TWanted>(IOBHandler<TBehaviour> handler)
            where TBehaviour : IOBehaviour
            where TWanted : TBehaviour
        {
            if (handler == null)
                return null;

            return (TWanted)handler.behaviours.FirstOrDefault(b => b is TWanted);
        }

        public static TBehaviour GetBuffBehaviour<TBehaviour>(this Player p, int index)
            where TBehaviour : BuffBehaviour
        {
            if (p.P_BuffBHandler != null)
                return GetBehaviour<BuffBehaviour, TBehaviour>(p.P_BuffBHandler[index] as BuffBHandler);

            return null;
        }
        public static TBehaviour GetBuffBehaviour<TBehaviour>(this NPC    n, int index)
            where TBehaviour : BuffBehaviour
        {
            if (n.P_BuffBHandler != null)
                return GetBehaviour<BuffBehaviour, TBehaviour>(n.P_BuffBHandler[index] as BuffBHandler);

            return null;
        }

        public static TBehaviour GetBehaviour<TBehaviour>(this Item       i )
            where TBehaviour : ItemBehaviour
        {
            return GetBehaviour<ItemBehaviour, TBehaviour>(i.P_BHandler as ItemBHandler);
        }
        public static TBehaviour GetBehaviour<TBehaviour>(this Mount      m )
            where TBehaviour : MountBehaviour
        {
            var bh = m.P_BHandler as MountBHandler;

            if (bh == null)
                return null;

            return (TBehaviour)bh.behaviours.FirstOrDefault(b => b is TBehaviour);
        }
        public static TBehaviour GetBehaviour<TBehaviour>(this NPC        n )
            where TBehaviour : NpcBehaviour
        {
            return GetBehaviour<NpcBehaviour, TBehaviour>(n.P_BHandler as NpcBHandler);
        }
        public static TBehaviour GetBehaviour<TBehaviour>(this Projectile pr)
            where TBehaviour : ProjectileBehaviour
        {
            return GetBehaviour<ProjectileBehaviour, TBehaviour>(pr.P_BHandler as ProjBHandler);
        }
        public static TBehaviour GetBehaviour<TBehaviour>(this Player     p )
            where TBehaviour : PlayerBehaviour
        {
            return GetBehaviour<PlayerBehaviour, TBehaviour>(p.P_BHandler as PlayerBHandler);
        }

        public static bool IsEmpty(this Entity e)
        {
            return e == null || !e.active;
        }
        public static bool IsEmpty(this Item i)
        {
            return i == null || i.type == 0 || i.stack <= 0 || !i.active;
        }
        public static bool IsDead(this NPC n)
        {
            return n == null || n.type == 0 || n.life == 0 || !n.active;
        }
        /// <summary>
        /// Is not a valid instance (<see cref="Player.statLifeMax" /> &lt;= 0)
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        public static bool IsEmpty(this Player p)
        {
            return p == null || p.statLifeMax <= 0 || !p.active;
        }
        /// <summary>
        /// Is dead (<see cref="Player.statLife" /> &lt;= 0, <see cref="Player.dead" />, <see cref="Player.ghost" />)
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        public static bool IsDead(this Player p)
        {
            return p.IsEmpty() || p.dead || p.statLife <= 0 || p.ghost;
        }

        public static ItemTooltip ToTooltip(this ObjectName[] lines)
        {
            var r = new ItemTooltip();

            r._tooltipLines = lines == null
                ? Empty<String>.Array
                : lines.Select(l => l.ToString()).ToArray();
            r._text = new LocalizedText(String.Empty /* ... */,
                    r._processedText = String.Join(Environment.NewLine, r._tooltipLines));

            r._lastCulture = Language.ActiveCulture;

            return r;
        }
        public static ObjectName[] ToLines(this ItemTooltip tooltip)
        {
            tooltip.ValidateTooltip();
            if (tooltip._tooltipLines == null)
                return Empty<ObjectName>.Array;

            return tooltip._tooltipLines.Select(l => (ObjectName)l).ToArray();
        }
    }
}
