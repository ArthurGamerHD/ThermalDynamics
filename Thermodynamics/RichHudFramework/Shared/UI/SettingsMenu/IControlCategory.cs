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

		public enum ControlCatAccessors : int
        {
            HeaderText = 1,

            SubheaderText = 2,

            Enabled = 3,

            AddMember = 4
        }

		public interface IControlCategory : IControlCategory<ControlTile>
        {
            IReadOnlyList<ControlTile> Tiles { get; }

            IControlCategory TileContainer { get; }
        }

		public interface IControlCategory<TElementContainer> : IEnumerable<TElementContainer>
        {
            string HeaderText { get; set; }

            string SubheaderText { get; set; }

            bool Enabled { get; set; }

            object ID { get; }


            void Add(TElementContainer tile);


            ControlContainerMembers GetApiData();
        }
    }
}