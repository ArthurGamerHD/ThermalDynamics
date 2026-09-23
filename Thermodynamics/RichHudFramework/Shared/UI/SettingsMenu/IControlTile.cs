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
            ApiMemberAccessor,
            MyTuple<object, Func<int>>,
            object
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


            void Add(TerminalControlBase control);


            ControlContainerMembers GetApiData();
        }
    }
}