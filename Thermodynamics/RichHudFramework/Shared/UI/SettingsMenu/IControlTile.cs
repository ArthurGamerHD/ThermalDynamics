using System;
using System.Collections.Generic;
using VRage;
using ApiMemberAccessor = System.Func<object, int, object>;

namespace RichHudFramework
{
    namespace UI
    {
        using Server;
        using Client;

        using ControlContainerMembers = MyTuple<
            ApiMemberAccessor, // GetOrSetMember,
            MyTuple<object, Func<int>>, // Member List
            object // ID
        >;

        public enum ControlTileAccessors : int
        {
            AddControl = 1,

            Enabled = 2,
        }

		public interface IControlTile : IEnumerable<ITerminalControl>
        {
            IReadOnlyList<TerminalControlBase> Controls { get; }

            IControlTile ControlContainer { get; }

            bool Enabled { get; set; }

            object ID { get; }

/// <summary>Adds a .</summary>
            void Add(TerminalControlBase control);

/// <summary>Returns the apidata.</summary>
            ControlContainerMembers GetApiData();
        }
    }
}