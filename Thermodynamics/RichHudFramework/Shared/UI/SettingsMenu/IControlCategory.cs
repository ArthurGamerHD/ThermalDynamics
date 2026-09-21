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

/// <summary>Adds a .</summary>
            void Add(TElementContainer tile);

/// <summary>Returns the apidata.</summary>
            ControlContainerMembers GetApiData();
        }
    }
}